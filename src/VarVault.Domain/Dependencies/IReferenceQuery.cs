namespace VarVault.Domain.Dependencies;

/// <summary>
/// ⚠ Answers "is this package referenced by anything?" across every reference source — Dependency,
/// SaveDependency, PresetMember, VarAlias, and ActivationLink — so safe-delete never treats a var
/// needed only by a user's own save (or a preset/alias/link) as an orphan. (Checklist 2.12.)
/// </summary>
public interface IReferenceQuery
{
    Task<bool> IsReferencedAsync(long packageId, CancellationToken cancellationToken = default);
}
