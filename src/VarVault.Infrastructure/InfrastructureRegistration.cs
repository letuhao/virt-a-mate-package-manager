using Microsoft.Extensions.DependencyInjection;
using VarVault.Common;

namespace VarVault.Infrastructure;

/// <summary>
/// Registers cross-cutting infrastructure (clock now; DbContext, filesystem/symlink,
/// zip, job queue arrive in Slice 1). The Host calls this once; modules consume the
/// results through SDK interfaces and never bind to infrastructure directly.
/// </summary>
public static class InfrastructureRegistration
{
    public static IServiceCollection AddVarVaultInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IClock>(SystemClock.Instance);
        return services;
    }
}
