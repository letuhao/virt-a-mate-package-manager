using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtaMatePackageManager.Application.Services;
using VirtaMatePackageManager.Core.Interfaces.Repositories;
using VirtaMatePackageManager.Core.Services.Content;
using VirtaMatePackageManager.Core.Services.FileSystem;
using VirtaMatePackageManager.Core.Services.Installation;
using VirtaMatePackageManager.Core.Services.Parsing;
using VirtaMatePackageManager.Core.Services.Preview;
using VirtaMatePackageManager.Core.Services.Repository;
using VirtaMatePackageManager.Core.Services.Validation;
using VirtaMatePackageManager.Infrastructure.Data;
using VirtaMatePackageManager.Infrastructure.Repositories;
using CoreInstallationService = VirtaMatePackageManager.Core.Services.Installation.InstallationService;
using AppInstallationService = VirtaMatePackageManager.Application.Services.InstallationService;

namespace VirtaMatePackageManager.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for configuring dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all services for VirtaMatePackageManager.
    /// </summary>
    public static IServiceCollection AddVirtaMatePackageManager(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Database Context
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Unit of Work
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Repositories
        services.AddScoped<IRepositoryRepository, RepositoryRepository>();
        services.AddScoped<IVarPackageRepository, VarPackageRepository>();
        services.AddScoped<IInstallationTargetRepository, InstallationTargetRepository>();
        services.AddScoped<IDependencyRepository, DependencyRepository>();
        services.AddScoped<IInstallationRepository, InstallationRepository>();

        // Core Services - Validation
        services.AddSingleton<VarFileValidationService>();

        // Core Services - Parsing
        services.AddSingleton<VarFileParsingService>();
        services.AddSingleton<DependencyExtractionService>();

        // Core Services - Content Analysis
        services.AddSingleton<ContentAnalysisService>();

        // Core Services - File System
        services.AddSingleton<FileHashService>();
        services.AddSingleton<SymbolicLinkService>();

        // Core Services - Preview
        services.AddScoped<PreviewImageExtractionService>();

        // Core Services - Repository
        services.AddScoped<RepositoryScanningService>();

        // Core Services - Duplicate
        services.AddScoped<Core.Services.Duplicate.DuplicateDetectionService>();
        services.AddScoped<Core.Services.Duplicate.DuplicateResolutionService>();

        // Core Services - Organization
        services.AddScoped<Core.Services.Organization.VarFileOrganizationService>();

        // Core Services - Installation
        services.AddScoped<Core.Services.Installation.InstallationService>();

        // Application Services
        services.AddScoped<IVarPackageService, VarPackageService>();
        services.AddScoped<IRepositoryService, RepositoryService>();
        services.AddScoped<IInstallationService, Application.Services.InstallationService>();
        services.AddScoped<IInstallationTargetService, InstallationTargetService>();
        services.AddScoped<IDependencyService, DependencyService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IBackendSettingsService, BackendSettingsService>();

        return services;
    }

    /// <summary>
    /// Registers configuration from appsettings.json.
    /// </summary>
    public static IServiceCollection AddVirtaMatePackageManagerConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Additional configuration bindings can be added here
        // For example: services.Configure<AppSettings>(configuration.GetSection("AppSettings"));
        
        return services;
    }
}

