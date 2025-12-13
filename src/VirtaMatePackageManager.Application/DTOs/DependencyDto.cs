namespace VirtaMatePackageManager.Application.DTOs;

/// <summary>
/// Data transfer object for a Dependency.
/// </summary>
public record DependencyDto(
    int Id,
    string DependencyName,
    string? VersionConstraint,
    bool IsOptional,
    bool IsResolved,
    int? ResolvedVarPackageId,
    string? ResolvedVarName
);

/// <summary>
/// Dependency validation result.
/// </summary>
public record DependencyValidationResult(
    int VarPackageId,
    IEnumerable<MissingDependencyDto> MissingDependencies,
    IEnumerable<ResolvedDependencyDto> ResolvedDependencies,
    bool AllDependenciesResolved
);

/// <summary>
/// Missing dependency information.
/// </summary>
public record MissingDependencyDto(
    string DependencyName,
    string? VersionConstraint,
    bool IsOptional
);

/// <summary>
/// Resolved dependency information.
/// </summary>
public record ResolvedDependencyDto(
    string DependencyName,
    int VarPackageId,
    string VarName,
    string RepositoryName
);

