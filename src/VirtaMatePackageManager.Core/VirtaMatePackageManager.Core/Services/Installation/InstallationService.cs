using Entities = VirtaMatePackageManager.Core.Entities;
using VirtaMatePackageManager.Core.Interfaces.Repositories;
using VirtaMatePackageManager.Core.Services.FileSystem;
using VirtaMatePackageManager.Core.ValueObjects;

namespace VirtaMatePackageManager.Core.Services.Installation;

/// <summary>
/// Options for installing a VAR package.
/// </summary>
public class InstallationOptions
{
    /// <summary>
    /// Whether to automatically install dependencies.
    /// </summary>
    public bool InstallDependencies { get; set; } = false;
    
    /// <summary>
    /// Whether to install optional dependencies.
    /// </summary>
    public bool InstallOptionalDependencies { get; set; } = false;
    
    /// <summary>
    /// Whether to skip if already installed.
    /// </summary>
    public bool SkipIfAlreadyInstalled { get; set; } = true;
}

/// <summary>
/// Information about a completed installation.
/// </summary>
public class InstallationInfo
{
    public int InstallationId { get; set; }
    public int VarPackageId { get; set; }
    public string VarName { get; set; } = string.Empty;
    public int InstallationTargetId { get; set; }
    public string SymlinkPath { get; set; } = string.Empty;
    public DateTime InstalledAt { get; set; }
}

/// <summary>
/// Service for installing and uninstalling VAR packages.
/// </summary>
public class InstallationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly SymbolicLinkService _symbolicLinkService;

    public InstallationService(
        IUnitOfWork unitOfWork,
        SymbolicLinkService? symbolicLinkService = null)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _symbolicLinkService = symbolicLinkService ?? new SymbolicLinkService();
    }

    /// <summary>
    /// Installs a VAR package by creating symbolic link to installation target.
    /// </summary>
    /// <param name="varPackageId">Database ID of VAR package</param>
    /// <param name="installationTargetId">Database ID of installation target</param>
    /// <param name="options">Installation options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result with installation information</returns>
    public async Task<Result<InstallationInfo>> InstallVarPackageAsync(
        int varPackageId,
        int installationTargetId,
        InstallationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new InstallationOptions();

        // Get VAR package
        var varPackage = await _unitOfWork.VarPackages.GetByIdAsync(varPackageId, cancellationToken);
        if (varPackage == null)
        {
            return Result<InstallationInfo>.Failure(
                "VAR package not found",
                ErrorCode.VarPackageNotFound);
        }

        // Get installation target
        var target = await _unitOfWork.InstallationTargets.GetByIdAsync(installationTargetId, cancellationToken);
        if (target == null)
        {
            return Result<InstallationInfo>.Failure(
                "Installation target not found",
                ErrorCode.InstallationTargetNotFound);
        }

        // Check if already installed
        var existingInstallation = await _unitOfWork.Installations.GetInstallationAsync(
            varPackageId,
            installationTargetId,
            cancellationToken);

        if (existingInstallation != null && existingInstallation.IsEnabled)
        {
            if (options.SkipIfAlreadyInstalled)
            {
                return Result<InstallationInfo>.Success(new InstallationInfo
                {
                    InstallationId = existingInstallation.Id,
                    VarPackageId = varPackage.Id,
                    VarName = varPackage.VarName,
                    InstallationTargetId = target.Id,
                    SymlinkPath = existingInstallation.SymlinkPath,
                    InstalledAt = existingInstallation.InstalledAt
                });
            }
            
            return Result<InstallationInfo>.Failure(
                "VAR package already installed",
                ErrorCode.AlreadyInstalled);
        }

        // Verify VAR file exists
        if (!File.Exists(varPackage.FilePath))
        {
            return Result<InstallationInfo>.Failure(
                $"VAR file not found: {varPackage.FilePath}",
                ErrorCode.FileNotFound);
        }

        // Create symlink path
        var symlinkPath = Path.Combine(target.Path, varPackage.VarName);

        // Handle existing symlink (disabled or broken)
        if (File.Exists(symlinkPath))
        {
            if (_symbolicLinkService.IsSymbolicLink(symlinkPath))
            {
                // Remove existing symlink
                var deleteResult = _symbolicLinkService.DeleteSymbolicLink(symlinkPath);
                if (!deleteResult.IsSuccess)
                {
                    return Result<InstallationInfo>.Failure(
                        $"Failed to delete existing symlink: {deleteResult.Error}",
                        ErrorCode.SymlinkCreationFailed);
                }
            }
            else
            {
                // Regular file exists - error
                return Result<InstallationInfo>.Failure(
                    $"File already exists at symlink location: {symlinkPath}",
                    ErrorCode.PathConflict);
            }
        }

        // Check for .disabled file
        var disabledPath = symlinkPath + ".disabled";
        if (File.Exists(disabledPath))
        {
            try
            {
                File.Delete(disabledPath);
            }
            catch (Exception ex)
            {
                // Log warning but continue
                System.Diagnostics.Debug.WriteLine($"Failed to delete disabled file: {ex.Message}");
            }
        }

        // Create symbolic link
        var createLinkResult = _symbolicLinkService.CreateSymbolicLink(
            symlinkPath,
            varPackage.FilePath,
            isDirectory: false);

        if (!createLinkResult.IsSuccess)
        {
            return Result<InstallationInfo>.Failure(
                $"Failed to create symlink: {createLinkResult.Error}",
                ErrorCode.SymlinkCreationFailed);
        }

        // Install dependencies if requested
        if (options.InstallDependencies)
        {
            var dependencyResult = await InstallDependenciesAsync(
                varPackage,
                installationTargetId,
                options,
                cancellationToken);

            // Log warning but continue - dependencies are not blocking
            if (!dependencyResult.IsSuccess)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Warning: Some dependencies failed to install: {dependencyResult.Error}");
            }
        }

        // Create/update installation record
        Entities.Installation installation;
        if (existingInstallation != null)
        {
            // Update existing disabled installation
            existingInstallation.SymlinkPath = symlinkPath;
            existingInstallation.IsEnabled = true;
            existingInstallation.InstalledAt = DateTime.UtcNow;
            existingInstallation.UpdatedAt = DateTime.UtcNow;
            
            await _unitOfWork.Installations.UpdateAsync(existingInstallation, cancellationToken);
            installation = existingInstallation;
        }
        else
        {
            // Create new installation
            installation = new Entities.Installation
            {
                VarPackageId = varPackageId,
                InstallationTargetId = installationTargetId,
                SymlinkPath = symlinkPath,
                IsEnabled = true,
                InstalledAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            
            await _unitOfWork.Installations.AddAsync(installation, cancellationToken);
        }

        // Save changes
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<InstallationInfo>.Success(new InstallationInfo
        {
            InstallationId = installation.Id,
            VarPackageId = varPackage.Id,
            VarName = varPackage.VarName,
            InstallationTargetId = target.Id,
            SymlinkPath = symlinkPath,
            InstalledAt = installation.InstalledAt
        });
    }

    /// <summary>
    /// Installs dependencies for a VAR package recursively.
    /// </summary>
    private async Task<Result> InstallDependenciesAsync(
        Entities.VarPackage varPackage,
        int installationTargetId,
        InstallationOptions options,
        CancellationToken cancellationToken)
    {
        // Get dependencies
        var dependencies = await _unitOfWork.Dependencies.GetByVarPackageIdAsync(
            varPackage.Id,
            cancellationToken);

        var unresolvedDependencies = new List<string>();

        foreach (var dependency in dependencies)
        {
            // Skip optional dependencies if not requested
            if (dependency.IsOptional && !options.InstallOptionalDependencies)
            {
                continue;
            }

            // Resolve dependency to VAR package
            var resolvedVarPackage = await ResolveDependencyAsync(
                dependency.DependencyName,
                cancellationToken);

            if (resolvedVarPackage == null)
            {
                unresolvedDependencies.Add(dependency.DependencyName);
                continue;
            }

            // Recursively install dependency
            var installResult = await InstallVarPackageAsync(
                resolvedVarPackage.Id,
                installationTargetId,
                options,
                cancellationToken);

            if (!installResult.IsSuccess)
            {
                unresolvedDependencies.Add(dependency.DependencyName);
            }
        }

        if (unresolvedDependencies.Count > 0)
        {
            return Result.Failure(
                $"Failed to install {unresolvedDependencies.Count} dependencies: {string.Join(", ", unresolvedDependencies)}",
                ErrorCode.DependencyUnresolved);
        }

        return Result.Success();
    }

    /// <summary>
    /// Resolves a dependency name to a VAR package.
    /// Uses VAR name lookup since VAR name is unique.
    /// </summary>
    private async Task<Entities.VarPackage?> ResolveDependencyAsync(
        string dependencyName,
        CancellationToken cancellationToken)
    {
        // Dependency name format: "Creator.Package.Version" or "Creator.Package.latest"
        // VAR name format: "Creator.Package.Version.var"
        
        // Try exact match first (with .var extension)
        var varName = dependencyName.EndsWith(".var", StringComparison.OrdinalIgnoreCase)
            ? dependencyName
            : dependencyName + ".var";

        var varPackage = await _unitOfWork.VarPackages.GetByVarNameAsync(varName, cancellationToken);
        
        if (varPackage != null)
        {
            return varPackage;
        }

        // Try without extension (in case dependency name includes .var)
        if (dependencyName.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
        {
            varName = dependencyName.Substring(0, dependencyName.Length - 4);
            varPackage = await _unitOfWork.VarPackages.GetByVarNameAsync(varName + ".var", cancellationToken);
        }

        return varPackage;
    }

    /// <summary>
    /// Uninstalls a VAR package by removing the symbolic link.
    /// </summary>
    /// <param name="varPackageId">Database ID of VAR package</param>
    /// <param name="installationTargetId">Database ID of installation target</param>
    /// <param name="removeSymlink">Whether to remove the symlink file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure</returns>
    public async Task<Result> UninstallVarPackageAsync(
        int varPackageId,
        int installationTargetId,
        bool removeSymlink = true,
        CancellationToken cancellationToken = default)
    {
        // Get installation
        var installation = await _unitOfWork.Installations.GetInstallationAsync(
            varPackageId,
            installationTargetId,
            cancellationToken);

        if (installation == null)
        {
            return Result.Failure(
                "Installation not found",
                ErrorCode.InstallationNotFound);
        }

        // Remove symlink if requested
        if (removeSymlink && !string.IsNullOrEmpty(installation.SymlinkPath))
        {
            if (File.Exists(installation.SymlinkPath))
            {
                var deleteResult = _symbolicLinkService.DeleteSymbolicLink(installation.SymlinkPath);
                
                if (!deleteResult.IsSuccess)
                {
                    // Log warning but continue - mark as disabled anyway
                    System.Diagnostics.Debug.WriteLine(
                        $"Warning: Failed to delete symlink: {deleteResult.Error}");
                }
            }
        }

        // Delete installation record
        await _unitOfWork.Installations.DeleteAsync(installation, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

