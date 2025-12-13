using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VirtaMatePackageManager.Core.ValueObjects;
using VirtaMatePackageManager.Infrastructure.Data;

namespace VirtaMatePackageManager.Application.Services;

/// <summary>
/// Service for managing backend settings (connection strings, etc.).
/// </summary>
public class BackendSettingsService : IBackendSettingsService
{
    private readonly IConfiguration _configuration;
    private readonly string _appsettingsPath;

    public BackendSettingsService(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        
        // Find appsettings.json path (same logic as ServiceProviderFactory)
        _appsettingsPath = FindAppsettingsPath();
    }

    public Task<Result<string>> GetConnectionStringAsync(CancellationToken ct)
    {
        try
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return Task.FromResult(Result<string>.Failure(
                    "Connection string 'DefaultConnection' not found in configuration.",
                    ErrorCode.ConfigurationError));
            }

            return Task.FromResult(Result<string>.Success(connectionString));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure(
                $"Failed to get connection string: {ex.Message}",
                ErrorCode.ConfigurationError));
        }
    }

    public async Task<Result> SaveConnectionStringAsync(string connectionString, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Result.Failure("Connection string cannot be null or empty.", ErrorCode.InvalidInput);
        }

        try
        {
            // Test connection before saving
            var testResult = await TestConnectionAsync(connectionString, ct);
            if (!testResult.IsSuccess)
            {
                return Result.Failure(
                    $"Cannot save invalid connection string. Test failed: {testResult.Error}",
                    ErrorCode.InvalidInput);
            }

            // Update appsettings.json
            if (string.IsNullOrEmpty(_appsettingsPath) || !File.Exists(_appsettingsPath))
            {
                return Result.Failure(
                    "Cannot find appsettings.json file. Settings cannot be saved.",
                    ErrorCode.FileNotFound);
            }

            // Read current appsettings.json
            var jsonContent = await File.ReadAllTextAsync(_appsettingsPath, ct);
            var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
            var root = jsonDoc.RootElement;

            // Update connection string
            var newJson = UpdateConnectionStringInJson(jsonContent, connectionString);

            // Write back to file
            await File.WriteAllTextAsync(_appsettingsPath, newJson, ct);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(
                $"Failed to save connection string: {ex.Message}",
                ErrorCode.ConfigurationError);
        }
    }

    public async Task<Result> TestConnectionAsync(string connectionString, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Result.Failure("Connection string cannot be null or empty.", ErrorCode.InvalidInput);
        }

        try
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseNpgsql(connectionString);

            using var context = new ApplicationDbContext(optionsBuilder.Options);
            
            // Test connection with a simple query
            await context.Database.CanConnectAsync(ct);
            
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(
                $"Connection test failed: {ex.Message}",
                ErrorCode.DatabaseError);
        }
    }

    public async Task<Result> TestCurrentConnectionAsync(CancellationToken ct)
    {
        var connectionStringResult = await GetConnectionStringAsync(ct);
        if (!connectionStringResult.IsSuccess)
        {
            return Result.Failure(
                $"Cannot test connection: {connectionStringResult.Error}",
                connectionStringResult.ErrorCode);
        }

        return await TestConnectionAsync(connectionStringResult.Value, ct);
    }

    private string? FindAppsettingsPath()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        
        var possiblePaths = new[]
        {
            Path.Combine(basePath, "appsettings.json"),
            Path.Combine(basePath, "..", "..", "..", "..", "VirtaMatePackageManager.Infrastructure", "appsettings.json"),
            Path.Combine(basePath, "..", "..", "..", "..", "src", "VirtaMatePackageManager.Infrastructure", "appsettings.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "VirtaMatePackageManager.Infrastructure", "appsettings.json")
        };

        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    private string UpdateConnectionStringInJson(string jsonContent, string newConnectionString)
    {
        try
        {
            // Parse the JSON into a dictionary structure
            using var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
            var root = jsonDoc.RootElement;

            // Build a dictionary with all properties
            var dict = new Dictionary<string, object?>();
            
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name == "ConnectionStrings")
                {
                    // Update connection strings
                    dict["ConnectionStrings"] = new Dictionary<string, string>
                    {
                        ["DefaultConnection"] = newConnectionString
                    };
                }
                else
                {
                    // Copy other properties as-is
                    dict[property.Name] = System.Text.Json.JsonSerializer.Deserialize<object>(
                        property.Value.GetRawText());
                }
            }

            // Serialize back to JSON with formatting
            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            };

            return System.Text.Json.JsonSerializer.Serialize(dict, options);
        }
        catch (Exception ex)
        {
            // Fallback: simple regex-based replacement
            var escaped = newConnectionString.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var pattern = @"""DefaultConnection""\s*:\s*""([^""]*)""";
            var replacement = $"\"DefaultConnection\": \"{escaped}\"";
            
            var result = System.Text.RegularExpressions.Regex.Replace(
                jsonContent,
                pattern,
                replacement,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // If replacement didn't work, log and throw
            if (result == jsonContent)
            {
                throw new InvalidOperationException(
                    $"Failed to update connection string in JSON. Original error: {ex.Message}");
            }
            
            return result;
        }
    }
}

