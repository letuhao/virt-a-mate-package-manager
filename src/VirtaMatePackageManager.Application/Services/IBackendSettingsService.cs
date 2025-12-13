using VirtaMatePackageManager.Core.ValueObjects;

namespace VirtaMatePackageManager.Application.Services;

/// <summary>
/// Service interface for managing backend settings (connection strings, etc.).
/// </summary>
public interface IBackendSettingsService
{
    /// <summary>
    /// Gets the current connection string.
    /// </summary>
    Task<Result<string>> GetConnectionStringAsync(CancellationToken ct);

    /// <summary>
    /// Saves the connection string to settings.
    /// </summary>
    Task<Result> SaveConnectionStringAsync(string connectionString, CancellationToken ct);

    /// <summary>
    /// Tests the database connection using the provided connection string.
    /// </summary>
    Task<Result> TestConnectionAsync(string connectionString, CancellationToken ct);

    /// <summary>
    /// Tests the database connection using the current saved connection string.
    /// </summary>
    Task<Result> TestCurrentConnectionAsync(CancellationToken ct);
}

