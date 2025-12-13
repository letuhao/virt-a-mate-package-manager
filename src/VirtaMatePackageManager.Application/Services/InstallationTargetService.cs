using VirtaMatePackageManager.Application.Commands;
using VirtaMatePackageManager.Application.DTOs;
using VirtaMatePackageManager.Core.Entities;
using VirtaMatePackageManager.Core.Interfaces.Repositories;
using VirtaMatePackageManager.Core.ValueObjects;

namespace VirtaMatePackageManager.Application.Services;

/// <summary>
/// Service implementation for installation target management operations.
/// </summary>
public class InstallationTargetService : IInstallationTargetService
{
    private readonly IUnitOfWork _unitOfWork;

    public InstallationTargetService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<Result<int>> AddInstallationTargetAsync(
        AddInstallationTargetCommand command,
        CancellationToken ct)
    {
        // Validate path
        if (string.IsNullOrWhiteSpace(command.Path))
        {
            return Result<int>.Failure(
                "Installation target path cannot be empty",
                ErrorCode.InvalidInput);
        }

        if (!Directory.Exists(command.Path))
        {
            return Result<int>.Failure(
                $"Installation target path does not exist: {command.Path}",
                ErrorCode.DirectoryNotFound);
        }

        // Check if target with same path already exists
        var existingTarget = await _unitOfWork.InstallationTargets.GetByPathAsync(command.Path, ct);
        if (existingTarget != null)
        {
            return Result<int>.Failure(
                $"Installation target with path '{command.Path}' already exists",
                ErrorCode.PathConflict);
        }

        // Create new installation target
        var target = new InstallationTarget
        {
            Name = command.Name,
            Path = Path.GetFullPath(command.Path),
            ProfileName = command.ProfileName,
            Description = command.Description,
            IsActive = false, // New targets are not active by default
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.InstallationTargets.AddAsync(target, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<int>.Success(target.Id);
    }

    public async Task<Result> UpdateInstallationTargetAsync(
        UpdateInstallationTargetCommand command,
        CancellationToken ct)
    {
        var target = await _unitOfWork.InstallationTargets.GetByIdAsync(command.Id, ct);
        if (target == null)
        {
            return Result.Failure(
                $"Installation target {command.Id} not found",
                ErrorCode.InstallationTargetNotFound);
        }

        // Update fields
        if (command.Name != null)
        {
            target.Name = command.Name;
        }

        if (command.Path != null)
        {
            if (!Directory.Exists(command.Path))
            {
                return Result.Failure(
                    $"Installation target path does not exist: {command.Path}",
                    ErrorCode.DirectoryNotFound);
            }

            // Check if another target with same path exists
            var existingTarget = await _unitOfWork.InstallationTargets.GetByPathAsync(command.Path, ct);
            if (existingTarget != null && existingTarget.Id != command.Id)
            {
                return Result.Failure(
                    $"Installation target with path '{command.Path}' already exists",
                    ErrorCode.PathConflict);
            }

            target.Path = Path.GetFullPath(command.Path);
        }

        if (command.ProfileName != null)
        {
            target.ProfileName = command.ProfileName;
        }

        if (command.Description != null)
        {
            target.Description = command.Description;
        }

        target.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.InstallationTargets.UpdateAsync(target, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> DeleteInstallationTargetAsync(
        int targetId,
        CancellationToken ct)
    {
        var target = await _unitOfWork.InstallationTargets.GetByIdAsync(targetId, ct);
        if (target == null)
        {
            return Result.Failure(
                $"Installation target {targetId} not found",
                ErrorCode.InstallationTargetNotFound);
        }

        // Check if target has installations
        var installations = await _unitOfWork.Installations.GetByTargetIdAsync(targetId, ct);
        if (installations.Any())
        {
            return Result.Failure(
                $"Cannot delete installation target {targetId}: it has {installations.Count()} installations. Remove installations first.",
                ErrorCode.InstallationTargetNotFound);
        }

        await _unitOfWork.InstallationTargets.DeleteAsync(targetId, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> SetActiveTargetAsync(
        int targetId,
        CancellationToken ct)
    {
        var target = await _unitOfWork.InstallationTargets.GetByIdAsync(targetId, ct);
        if (target == null)
        {
            return Result.Failure(
                $"Installation target {targetId} not found",
                ErrorCode.InstallationTargetNotFound);
        }

        // Deactivate all other targets
        var allTargets = await _unitOfWork.InstallationTargets.GetAllAsync(ct);
        foreach (var t in allTargets)
        {
            if (t.Id != targetId)
            {
                t.IsActive = false;
                t.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.InstallationTargets.UpdateAsync(t, ct);
            }
        }

        // Activate the target
        target.IsActive = true;
        target.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.InstallationTargets.UpdateAsync(target, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result<IEnumerable<InstallationTargetDto>>> GetAllTargetsAsync(
        CancellationToken ct)
    {
        var targets = await _unitOfWork.InstallationTargets.GetAllAsync(ct);
        
        var dtos = await MapTargetsToDtos(targets, ct);

        return Result<IEnumerable<InstallationTargetDto>>.Success(dtos);
    }

    public async Task<Result<InstallationTargetDto>> GetTargetByIdAsync(
        int id,
        CancellationToken ct)
    {
        var target = await _unitOfWork.InstallationTargets.GetByIdAsync(id, ct);
        if (target == null)
        {
            return Result<InstallationTargetDto>.Failure(
                $"Installation target {id} not found",
                ErrorCode.InstallationTargetNotFound);
        }

        var dto = await MapTargetToDto(target, ct);

        return Result<InstallationTargetDto>.Success(dto);
    }

    public async Task<Result<InstallationTargetDto?>> GetActiveTargetAsync(
        CancellationToken ct)
    {
        var activeTargets = await _unitOfWork.InstallationTargets.GetActiveTargetsAsync(ct);
        var activeTarget = activeTargets.FirstOrDefault();

        if (activeTarget == null)
        {
            return Result<InstallationTargetDto?>.Success(null);
        }

        var dto = await MapTargetToDto(activeTarget, ct);
        return Result<InstallationTargetDto?>.Success(dto);
    }

    public async Task<Result<IEnumerable<InstallationTargetDto>>> GetTargetsByProfileAsync(
        string profileName,
        CancellationToken ct)
    {
        // Note: This requires a method in IInstallationTargetRepository that filters by profile
        // For now, get all and filter in-memory
        var allTargets = await _unitOfWork.InstallationTargets.GetAllAsync(ct);
        var targets = allTargets.Where(t => t.ProfileName == profileName);
        
        var dtos = await MapTargetsToDtos(targets, ct);

        return Result<IEnumerable<InstallationTargetDto>>.Success(dtos);
    }

    private async Task<List<InstallationTargetDto>> MapTargetsToDtos(
        IEnumerable<InstallationTarget> targets,
        CancellationToken ct)
    {
        var dtos = new List<InstallationTargetDto>();

        foreach (var target in targets)
        {
            var dto = await MapTargetToDto(target, ct);
            dtos.Add(dto);
        }

        return dtos;
    }

    private async Task<InstallationTargetDto> MapTargetToDto(
        InstallationTarget target,
        CancellationToken ct)
    {
        // Get installation count
        var installations = await _unitOfWork.Installations.GetByTargetIdAsync(target.Id, ct);
        var installationCount = installations.Count();

        return new InstallationTargetDto(
            target.Id,
            target.Name,
            target.Path,
            target.ProfileName,
            target.Description,
            target.IsActive,
            installationCount,
            target.CreatedAt,
            target.UpdatedAt
        );
    }
}

