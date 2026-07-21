using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Services;
using VarVault.App.ViewModels;
using VarVault.Sdk.Import;
using VarVault.Sdk.Repositories;
using VarVault.Sdk.Threading;

namespace VarVault.App.Tests;

internal static class ImportTestHelpers
{
    public static ImportViewModel CreateImportViewModel(IServiceProvider services) =>
        new(
            services.GetRequiredService<IImportService>(),
            services.GetRequiredService<IRepositoryService>(),
            services.GetRequiredService<ImportJobRunner>(),
            services.GetRequiredService<IUiDispatcher>());

    public static void RegisterImportJobs(IServiceCollection services) =>
        services.AddSingleton<ImportJobRunner>();
}
