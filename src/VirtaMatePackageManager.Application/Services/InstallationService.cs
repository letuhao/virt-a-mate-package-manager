using VirtaMatePackageManager.Application.Commands;
using VirtaMatePackageManager.Application.DTOs;
using VirtaMatePackageManager.Core.Entities;
using VirtaMatePackageManager.Core.Interfaces.Repositories;
using VirtaMatePackageManager.Core.Services.FileSystem;
using VirtaMatePackageManager.Core.Services.Installation;
using VirtaMatePackageManager.Core.ValueObjects;

namespace VirtaMatePackageManager.Application.Services;

/// <summary>
/// Service implementation for VAR package installation operations.
/// Wraps Core InstallationService and provides Application-layer DTOs.
/// </summary>
public class InstallationService : IInstallationService
{
    private readonly Core.Services.Installation.InstallationService _coreInstallationService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly SymbolicLinkService _symbolicLinkService;

    public InstallationService(
        IUnitOfWork unitOfWork,
        Core.Services.Installation.InstallationService? coreInstallationService = null,
        SymbolicLinkService? symbolicLinkService = null)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _symbolicLinkService = symbolicLinkService ?? new SymbolicLinkService();
        _coreInstallationService = coreInstallationService ?? new Core.Services.Installation.InstallationService(unitOfWork, _symbolicLinkService);
    }

    public async Task<Result<int>> InstallVarPackageAsync(
        InstallVarPackageCommand command,
        CancellationToken ct)
    {
        var options = new InstallationOptions
        {
            InstallDependencies = command.InstallDependencies,
            SkipIfAlreadyInstalled = command.SkipIfAlreadyInstalled
        };

        var result = await _coreInstallationService.InstallVarPackageAsync(
            command.VarPackageId,
            command.InstallationTargetId,
            options,
            ct);

        if (!result.IsSuccess)
        {
            return Result<int>.Failure(result.Error ?? "Installation failed", result.ErrorCode ?? ErrorCode.InstallationFailed);
        }

        return Result<int>.Success(result.Value!.InstallationId);
    }

    public async Task<Result> UninstallVarPackageAsync(
        UninstallVarPackageCommand command,
        CancellationToken ct)
    {
        var result = await _coreInstallationService.UninstallVarPackageAsync(
            command.VarPackageId,
            command.InstallationTargetId,
            command.RemoveSymlink,
            ct);

        return result;
    }

    public async Task<Result> BatchInstallVarPackagesAsync(
        BatchInstallCommand command,
        CancellationToken ct)
    {
        var varPackageIds = command.VarPackageIds.ToList();
        var errors = new List<string>();

        foreach (var varPackageId in varPackageIds)
        {
            ct.ThrowIfCancellationRequested();

            var installCommand = new InstallVarPackageCommand(
                varPackageId,
                command.InstallationTargetId,
                command.InstallDependencies,
                SkipIfAlreadyInstalled: true);

            var result = await InstallVarPackageAsync(installCommand, ct);
            
            if (!result.IsSuccess)
            {
                errors.Add($"VAR package {varPackageId}: {result.Error}");
            }
        }

        if (errors.Count > 0)
        {
            return Result.Failure(
                $"Failed to install {errors.Count} of {varPackageIds.Count} packages: {string.Join("; ", errors)}",
                ErrorCode.InstallationFailed);
        }

        return Result.Success();
    }

    public async Task<Result> BatchUninstallVarPackagesAsync(
        BatchUninstallCommand command,
        CancellationToken ct)
    {
        var varPackageIds = command.VarPackageIds.ToList();
        var errors = new List<string>();

        foreach (var varPackageId in varPackageIds)
        {
            ct.ThrowIfCancellationRequested();

            var uninstallCommand = new UninstallVarPackageCommand(
                varPackageId,
                command.InstallationTargetId,
                RemoveSymlink: true);

            var result = await UninstallVarPackageAsync(uninstallCommand, ct);
            
            if (!result.IsSuccess)
            {
                errors.Add($"VAR package {varPackageId}: {result.Error}");
            }
        }

        if (errors.Count > 0)
        {
            return Result.Failure(
                $"Failed to uninstall {errors.Count} of {varPackageIds.Count} packages: {string.Join("; ", errors)}",
                ErrorCode.InstallationFailed);
        }

        return Result.Success();
    }

    public async Task<Result> EnableInstallationAsync(
        int installationId,
        bool enabled,
        CancellationToken ct)
    {
        var installation = await _unitOfWork.Installations.GetByIdAsync(installationId, ct);
        if (installation == null)
        {
            return Result.Failure(
                $"Installation {installationId} not found",
                ErrorCode.InstallationNotFound);
        }

        installation.IsEnabled = enabled;
        installation.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Installations.UpdateAsync(installation, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result<IEnumerable<InstallationDto>>> GetInstallationsByVarPackageAsync(
        int varPackageId,
        CancellationToken ct)
    {
        var installations = await _unitOfWork.Installations.GetByVarPackageIdAsync(varPackageId, ct);
        
        var dtos = await MapInstallationsToDtos(installations, ct);
        
        return Result<IEnumerable<InstallationDto>>.Success(dtos);
    }

    public async Task<Result<IEnumerable<InstallationDto>>> GetInstallationsByTargetAsync(
        int targetId,
        CancellationToken ct)
    {
        var installations = await _unitOfWork.Installations.GetByTargetIdAsync(targetId, ct);
        
        var dtos = await MapInstallationsToDtos(installations, ct);
        
        return Result<IEnumerable<InstallationDto>>.Success(dtos);
    }

    public async Task<Result<InstallationDto>> GetInstallationAsync(
        int installationId,
        CancellationToken ct)
    {
        var installation = await _unitOfWork.Installations.GetByIdAsync(installationId, ct);
        if (installation == null)
        {
            return Result<InstallationDto>.Failure(
                $"Installation {installationId} not found",
                ErrorCode.InstallationNotFound);
        }

        var dto = await MapInstallationToDto(installation, ct);
        
        return Result<InstallationDto>.Success(dto);
    }

    public async Task<Result> VerifyInstallationsAsync(
        int targetId,
        CancellationToken ct)
    {
        var installations = await _unitOfWork.Installations.GetByTargetIdAsync(targetId, ct);
        
        var brokenInstallations = new List<Installation>();
        
        foreach (var installation in installations)
        {
            ct.ThrowIfCancellationRequested();
            
            if (!File.Exists(installation.SymlinkPath))
            {
                brokenInstallations.Add(installation);
                continue;
            }

            if (!_symbolicLinkService.IsSymbolicLink(installation.SymlinkPath))
            {
                brokenInstallations.Add(installation);
                continue;
            }

            var resolvedTarget = _symbolicLinkService.ResolveSymbolicLink(installation.SymlinkPath);
            if (resolvedTarget == null || !File.Exists(resolvedTarget))
            {
                brokenInstallations.Add(installation);
            }
        }

        // Return success but broken installations could be returned in a more detailed result
        // For now, just return success
        return Result.Success();
    }

    public async Task<Result<int>> RepairBrokenSymlinksAsync(
        int targetId,
        CancellationToken ct)
    {
        var installations = await _unitOfWork.Installations.GetByTargetIdAsync(targetId, ct);
        
        var repairedCount = 0;
        var brokenInstallations = new List<Installation>();
        
        foreach (var installation in installations)
        {
            ct.ThrowIfCancellationRequested();
            
            if (!File.Exists(installation.SymlinkPath))
            {
                brokenInstallations.Add(installation);
                continue;
            }

            if (!_symbolicLinkService.IsSymbolicLink(installation.SymlinkPath))
            {
                brokenInstallations.Add(installation);
                continue;
            }

            var resolvedTarget = _symbolicLinkService.ResolveSymbolicLink(installation.SymlinkPath);
            if (resolvedTarget == null || !File.Exists(resolvedTarget))
            {
                brokenInstallations.Add(installation);
            }
        }

        // Try to repair broken installations
        foreach (var installation in brokenInstallations)
        {
            ct.ThrowIfCancellationRequested();
            
            // Get VAR package
            var varPackage = await _unitOfWork.VarPackages.GetByIdAsync(installation.VarPackageId, ct);
            if (varPackage == null || !File.Exists(varPackage.FilePath))
            {
                // VAR package doesn't exist - remove installation
                await _unitOfWork.Installations.DeleteAsync(installation, ct);
                continue;
            }

            // Try to recreate symlink
            var createResult = _symbolicLinkService.CreateSymbolicLink(
                installation.SymlinkPath,
                varPackage.FilePath,
                isDirectory: false);

            if (createResult.IsSuccess)
            {
                installation.IsEnabled = true;
                installation.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.Installations.UpdateAsync(installation, ct);
                repairedCount++;
            }
            else
            {
                // Can't repair - remove installation
                await _unitOfWork.Installations.DeleteAsync(installation, ct);
            }
        }

        if (brokenInstallations.Count > 0)
        {
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return Result<int>.Success(repairedCount);
    }

    private async Task<List<InstallationDto>> MapInstallationsToDtos(
        IEnumerable<Installation> installations,
        CancellationToken ct)
    {
        var dtos = new List<InstallationDto>();
        
        foreach (var installation in installations)
        {
            var dto = await MapInstallationToDto(installation, ct);
            dtos.Add(dto);
        }
        
        return dtos;
    }

    private async Task<InstallationDto> MapInstallationToDto(
        Installation installation,
        CancellationToken ct)
    {
        // Load navigation properties if not already loaded
        if (installation.VarPackage == null)
        {
            installation = await _unitOfWork.Installations.GetByIdAsync(installation.Id, ct) 
                ?? installation;
        }

        var varPackage = installation.VarPackage ?? 
            await _unitOfWork.VarPackages.GetByIdAsync(installation.VarPackageId, ct);
        
        var target = installation.InstallationTarget ?? 
            await _unitOfWork.InstallationTargets.GetByIdAsync(installation.InstallationTargetId, ct);

        return new InstallationDto(
            installation.Id,
            installation.VarPackageId,
            varPackage?.VarName ?? string.Empty,
            installation.InstallationTargetId,
            target?.Name ?? string.Empty,
            installation.SymlinkPath,
            installation.IsEnabled,
            installation.InstalledAt,
            installation.InstalledBy
        );
    }
}

