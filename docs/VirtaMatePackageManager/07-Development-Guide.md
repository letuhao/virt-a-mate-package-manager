# Development Guide

## Overview

This guide provides setup instructions, coding standards, project structure, and development workflow for VirtaMatePackageManager.

## Environment Setup

### Prerequisites

1. **.NET 9.0 SDK**
   - Download from: https://dotnet.microsoft.com/download
   - Verify: `dotnet --version` (should show 9.0.x)

2. **PostgreSQL 14+**
   - Download from: https://www.postgresql.org/download/
   - Or use Docker: `docker run -p 5432:5432 -e POSTGRES_PASSWORD=postgres postgres:14`

3. **IDE**
   - Visual Studio 2022 (recommended)
   - JetBrains Rider
   - VS Code with C# extension

4. **Tools**
   - Git
   - Entity Framework Core tools: `dotnet tool install --global dotnet-ef`

### Initial Setup

1. **Clone Repository**
```bash
git clone <repository-url>
cd VirtaMatePackageManager
```

2. **Restore Dependencies**
```bash
dotnet restore
```

3. **Setup Database**
```bash
# Update connection string in appsettings.json
# Run migrations
cd src/VirtaMatePackageManager.Infrastructure
dotnet ef database update
```

4. **Build Solution**
```bash
dotnet build
```

5. **Run Tests**
```bash
dotnet test
```

## Project Structure

```
VirtaMatePackageManager/
├── src/
│   ├── VirtaMatePackageManager.Core/
│   │   ├── Entities/           # Domain entities
│   │   ├── ValueObjects/       # Value objects
│   │   ├── Interfaces/         # Domain interfaces
│   │   └── Services/           # Domain services
│   │
│   ├── VirtaMatePackageManager.Application/
│   │   ├── Commands/           # Command handlers
│   │   ├── Queries/            # Query handlers
│   │   ├── DTOs/               # Data transfer objects
│   │   ├── Services/           # Application services
│   │   └── Validators/         # FluentValidation validators
│   │
│   ├── VirtaMatePackageManager.Infrastructure/
│   │   ├── Data/               # EF Core DbContext
│   │   ├── Repositories/       # Repository implementations
│   │   ├── Services/           # Infrastructure services
│   │   └── Migrations/         # EF Core migrations
│   │
│   └── VirtaMatePackageManager.Presentation/
│       ├── Views/              # WPF views (XAML)
│       ├── ViewModels/         # MVVM view models
│       ├── Controls/           # Custom controls
│       └── Converters/         # Value converters
│
├── tests/
│   ├── VirtaMatePackageManager.Core.Tests/
│   ├── VirtaMatePackageManager.Application.Tests/
│   └── VirtaMatePackageManager.Integration.Tests/
│
├── docs/
│   └── VirtaMatePackageManager/
│
├── scripts/
│   ├── database/               # Database scripts
│   └── deployment/             # Deployment scripts
│
├── .editorconfig              # Code style configuration
├── .gitignore
├── Directory.Build.props       # Common MSBuild properties
└── VirtaMatePackageManager.sln
```

## Coding Standards

### Naming Conventions

**Classes**: PascalCase
```csharp
public class VarPackageService { }
```

**Methods**: PascalCase
```csharp
public async Task<Result<int>> InstallVarPackageAsync() { }
```

**Properties**: PascalCase
```csharp
public string VarName { get; set; }
```

**Private Fields**: _camelCase
```csharp
private readonly IVarPackageRepository _repository;
```

**Local Variables**: camelCase
```csharp
var varPackage = await _repository.GetByIdAsync(id);
```

**Constants**: PascalCase
```csharp
public const int MaxBatchSize = 100;
```

**Interfaces**: I + PascalCase
```csharp
public interface IVarPackageService { }
```

**Async Methods**: Suffix with "Async"
```csharp
public async Task DoSomethingAsync() { }
```

### Code Style

**Use var for implicit types**:
```csharp
var service = new VarPackageService(); // Good
VarPackageService service = new VarPackageService(); // Avoid
```

**Use expression-bodied members where appropriate**:
```csharp
public string FullName => $"{FirstName} {LastName}";
```

**Use null-conditional operators**:
```csharp
var name = varPackage?.VarName ?? "Unknown";
```

**Use pattern matching**:
```csharp
if (result is Result<int> success && success.IsSuccess)
{
    // Handle success
}
```

### Async/Await Guidelines

**Always use async/await for I/O operations**:
```csharp
// ✅ Good
public async Task<Result<VarPackage>> GetByIdAsync(int id)
{
    return await _repository.GetByIdAsync(id);
}

// ❌ Bad
public Task<Result<VarPackage>> GetById(int id)
{
    return _repository.GetById(id);
}
```

**Configure await for better performance**:
```csharp
// Use ConfigureAwait(false) in library code
public async Task DoWorkAsync()
{
    await SomeOperationAsync().ConfigureAwait(false);
}
```

**Avoid async void** (except event handlers):
```csharp
// ❌ Bad
public async void DoWork() { }

// ✅ Good
public async Task DoWorkAsync() { }
```

### Error Handling

**Use Result pattern for business errors**:
```csharp
public async Task<Result<int>> InstallAsync(int id)
{
    var package = await _repository.GetByIdAsync(id);
    if (package == null)
        return Result<int>.Failure("Package not found", ErrorCode.NotFound);
    
    // Success
    return Result<int>.Success(installationId);
}
```

**Use exceptions only for unexpected errors**:
```csharp
try
{
    await _fileService.ReadFileAsync(path);
}
catch (FileNotFoundException ex)
{
    // Log and convert to Result
    _logger.LogError(ex, "File not found: {Path}", path);
    return Result.Failure("File not found", ErrorCode.FileNotFound);
}
```

### Documentation

**XML documentation for public APIs**:
```csharp
/// <summary>
/// Installs a VAR package to the specified installation target.
/// </summary>
/// <param name="command">Installation command containing VAR package ID and target ID.</param>
/// <param name="ct">Cancellation token.</param>
/// <returns>Result containing installation ID on success, or error information.</returns>
public async Task<Result<int>> InstallVarPackageAsync(
    InstallVarPackageCommand command,
    CancellationToken ct)
{
    // Implementation
}
```

## Dependency Injection

### Service Registration

**Application Layer** (Application.cs):
```csharp
services.AddScoped<IVarPackageService, VarPackageService>();
services.AddScoped<IInstallationService, InstallationService>();
services.AddScoped<IRepositoryService, RepositoryService>();
```

**Infrastructure Layer**:
```csharp
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

services.AddScoped<IUnitOfWork, UnitOfWork>();
services.AddScoped<IVarPackageRepository, VarPackageRepository>();
```

**Use Scrutor for convention-based registration**:
```csharp
services.Scan(scan => scan
    .FromAssemblyOf<IVarPackageService>()
    .AddClasses(classes => classes.AssignableTo<IVarPackageService>())
    .AsImplementedInterfaces()
    .WithScopedLifetime());
```

## Testing Guidelines

### Unit Tests

**Structure**:
```csharp
public class VarPackageServiceTests
{
    private readonly Mock<IVarPackageRepository> _mockRepository;
    private readonly VarPackageService _service;
    
    public VarPackageServiceTests()
    {
        _mockRepository = new Mock<IVarPackageRepository>();
        _service = new VarPackageService(_mockRepository.Object);
    }
    
    [Fact]
    public async Task InstallVarPackageAsync_WhenPackageExists_ReturnsSuccess()
    {
        // Arrange
        var packageId = 1;
        var command = new InstallVarPackageCommand(packageId, targetId: 1);
        _mockRepository.Setup(r => r.GetByIdAsync(packageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VarPackage { Id = packageId });
        
        // Act
        var result = await _service.InstallVarPackageAsync(command, CancellationToken.None);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeGreaterThan(0);
    }
}
```

### Integration Tests

**Use Testcontainers for database**:
```csharp
public class RepositoryIntegrationTests : IClassFixture<PostgreSQLContainer>
{
    [Fact]
    public async Task AddVarPackage_ShouldPersistToDatabase()
    {
        // Arrange
        using var context = CreateDbContext();
        var repository = new VarPackageRepository(context);
        var package = new VarPackage { VarName = "Test.Var.1.var" };
        
        // Act
        await repository.AddAsync(package, CancellationToken.None);
        await context.SaveChangesAsync();
        
        // Assert
        var saved = await repository.GetByIdAsync(package.Id, CancellationToken.None);
        saved.Should().NotBeNull();
        saved.VarName.Should().Be("Test.Var.1.var");
    }
}
```

## Git Workflow

### Branch Strategy

- **main**: Production-ready code
- **develop**: Integration branch
- **feature/**: Feature branches
- **bugfix/**: Bug fix branches
- **release/**: Release preparation

### Commit Messages

**Format**:
```
<type>(<scope>): <subject>

<body>

<footer>
```

**Types**:
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation
- `style`: Code style
- `refactor`: Refactoring
- `test`: Tests
- `chore`: Build/maintenance

**Example**:
```
feat(installation): Add batch installation support

Implements batch installation of multiple VAR packages
with progress reporting and cancellation support.

Closes #123
```

## Build Configuration

### Directory.Build.props

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>13</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <WarningsAsErrors />
    <WarningsNotAsErrors />
  </PropertyGroup>
</Project>
```

### EditorConfig

```ini
root = true

[*.cs]
indent_style = space
indent_size = 4
end_of_line = crlf
insert_final_newline = true
trim_trailing_whitespace = true

[*.{cs,csx,vb,vbx}]
dotnet_sort_system_directives_first = true
dotnet_separate_import_directive_groups = false
```

## Entity Framework Core

### DbContext Configuration

```csharp
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }
    
    public DbSet<VarPackage> VarPackages => Set<VarPackage>();
    public DbSet<Repository> Repositories => Set<Repository>();
    // ... other DbSets
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }
}
```

### Entity Configuration

```csharp
public class VarPackageConfiguration : IEntityTypeConfiguration<VarPackage>
{
    public void Configure(EntityTypeBuilder<VarPackage> builder)
    {
        builder.ToTable("var_packages");
        
        builder.HasKey(v => v.Id);
        
        builder.Property(v => v.VarName)
            .HasMaxLength(500)
            .IsRequired();
        
        builder.HasIndex(v => v.VarName);
        builder.HasIndex(v => new { v.CreatorName, v.PackageName, v.Version });
        
        builder.HasOne(v => v.Repository)
            .WithMany(r => r.VarPackages)
            .HasForeignKey(v => v.RepositoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

## Logging

### Configuration

```csharp
services.AddSerilog((services, lc) => lc
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/varm-.log", rollingInterval: RollingInterval.Day));
```

### Usage

```csharp
public class VarPackageService
{
    private readonly ILogger<VarPackageService> _logger;
    
    public VarPackageService(ILogger<VarPackageService> logger)
    {
        _logger = logger;
    }
    
    public async Task<Result<int>> InstallAsync(int id)
    {
        _logger.LogInformation("Installing VAR package {VarPackageId}", id);
        
        try
        {
            // Implementation
            _logger.LogInformation("Successfully installed VAR package {VarPackageId}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install VAR package {VarPackageId}", id);
            throw;
        }
    }
}
```

## Performance Considerations

### Database Queries

**Use projections**:
```csharp
// ❌ Bad - loads entire entity
var packages = await _context.VarPackages
    .Where(v => v.CreatorName == "Creator")
    .ToListAsync();

// ✅ Good - only loads needed data
var packages = await _context.VarPackages
    .Where(v => v.CreatorName == "Creator")
    .Select(v => new VarPackageDto { VarName = v.VarName, ... })
    .ToListAsync();
```

**Use pagination**:
```csharp
var packages = await _context.VarPackages
    .OrderBy(v => v.VarName)
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync();
```

**Batch operations**:
```csharp
_context.VarPackages.AddRange(packages);
await _context.SaveChangesAsync(); // Single round-trip
```

### Parallel Processing

```csharp
await Parallel.ForEachAsync(
    varFiles,
    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
    async (file, ct) =>
    {
        await ProcessFileAsync(file, ct);
    });
```

## Debugging

### Local Development

1. **Set up local PostgreSQL**
2. **Configure appsettings.Development.json**:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=varm_dev;Username=postgres;Password=postgres"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  }
}
```

3. **Run with debugger**
4. **Use EF Core logging**:
```csharp
optionsBuilder.LogTo(Console.WriteLine, LogLevel.Information);
```

## Common Tasks

### Add New Entity

1. Create entity in Core layer
2. Create entity configuration in Infrastructure
3. Add DbSet to DbContext
4. Create migration: `dotnet ef migrations add AddNewEntity`
5. Update database: `dotnet ef database update`

### Add New Feature

1. Create feature branch
2. Add domain entities/events
3. Add application services/commands/queries
4. Add repository methods
5. Add UI (if needed)
6. Write tests
7. Create PR

### Database Migration

```bash
# Create migration
dotnet ef migrations add MigrationName --project src/VirtaMatePackageManager.Infrastructure

# Update database
dotnet ef database update --project src/VirtaMatePackageManager.Infrastructure

# Generate SQL script
dotnet ef migrations script --project src/VirtaMatePackageManager.Infrastructure
```

## Troubleshooting

### Common Issues

**Database connection fails**:
- Check PostgreSQL is running
- Verify connection string
- Check firewall settings

**Migrations fail**:
- Check database exists
- Verify user permissions
- Review migration SQL

**Tests fail**:
- Check test database setup
- Verify test data
- Check async/await usage

## Next Steps

Continue to:
- [Testing Strategy](./08-Testing-Strategy.md) - Testing approach
- [Architecture Overview](./01-Architecture-Overview.md) - Review architecture

