# Testing Strategy

## Overview

This document outlines the comprehensive testing strategy for VirtaMatePackageManager, covering unit tests, integration tests, and end-to-end tests.

## Testing Principles

1. **Test Pyramid**: Many unit tests, fewer integration tests, minimal E2E tests
2. **AAA Pattern**: Arrange, Act, Assert
3. **Isolation**: Tests should be independent and isolated
4. **Fast**: Unit tests should run in milliseconds
5. **Reliable**: Tests should be deterministic
6. **Maintainable**: Tests should be easy to understand and update

## Testing Levels

### 1. Unit Tests (70%)

**Scope**: Individual classes and methods  
**Speed**: < 10ms per test  
**Dependencies**: Mocked  
**Coverage Target**: 80%+

### 2. Integration Tests (25%)

**Scope**: Component interactions  
**Speed**: < 1s per test  
**Dependencies**: Real database (Testcontainers)  
**Coverage Target**: Critical paths

### 3. End-to-End Tests (5%)

**Scope**: Full workflows  
**Speed**: < 30s per test  
**Dependencies**: Full system  
**Coverage Target**: Major user journeys

## Testing Technologies

- **Framework**: xUnit
- **Assertions**: FluentAssertions
- **Mocking**: Moq
- **Database Testing**: Testcontainers.PostgreSql
- **Coverage**: Coverlet

## Unit Testing

### Domain Layer Tests

**Test Entities**:
```csharp
public class VarPackageTests
{
    [Fact]
    public void Constructor_WithValidData_CreatesInstance()
    {
        // Arrange & Act
        var package = new VarPackage(
            repositoryId: 1,
            varName: "Creator.Package.1.var",
            filePath: "C:\\Vars\\Creator.Package.1.var"
        );
        
        // Assert
        package.VarName.Should().Be("Creator.Package.1.var");
        package.RepositoryId.Should().Be(1);
    }
    
    [Theory]
    [InlineData("")]
    [InlineData("Invalid")]
    [InlineData("Creator.Package")]
    public void Constructor_WithInvalidVarName_ThrowsException(string invalidName)
    {
        // Arrange, Act & Assert
        Action act = () => new VarPackage(1, invalidName, "path");
        act.Should().Throw<ArgumentException>();
    }
}
```

**Test Domain Services**:
```csharp
public class VarNameValidatorTests
{
    [Theory]
    [InlineData("Creator.Package.1.var", true)]
    [InlineData("Creator.Package.latest.var", true)]
    [InlineData("Invalid", false)]
    [InlineData("Creator.Package.var", false)]
    public void Validate_WithVariousInputs_ReturnsExpectedResult(string varName, bool expected)
    {
        // Arrange
        var validator = new VarNameValidator();
        
        // Act
        var result = validator.Validate(varName);
        
        // Assert
        result.Should().Be(expected);
    }
}
```

### Application Layer Tests

**Test Command Handlers**:
```csharp
public class InstallVarPackageCommandHandlerTests
{
    private readonly Mock<IVarPackageRepository> _mockRepository;
    private readonly Mock<IInstallationService> _mockInstallationService;
    private readonly InstallVarPackageCommandHandler _handler;
    
    public InstallVarPackageCommandHandlerTests()
    {
        _mockRepository = new Mock<IVarPackageRepository>();
        _mockInstallationService = new Mock<IInstallationService>();
        _handler = new InstallVarPackageCommandHandler(
            _mockRepository.Object,
            _mockInstallationService.Object
        );
    }
    
    [Fact]
    public async Task Handle_WhenVarPackageExists_ReturnsSuccess()
    {
        // Arrange
        var command = new InstallVarPackageCommand(varPackageId: 1, targetId: 1);
        var varPackage = new VarPackage { Id = 1, VarName = "Test.Var.1.var" };
        
        _mockRepository
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(varPackage);
        
        _mockInstallationService
            .Setup(s => s.CreateInstallationAsync(It.IsAny<Installation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(100));
        
        // Act
        var result = await _handler.Handle(command, CancellationToken.None);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(100);
        _mockInstallationService.Verify(
            s => s.CreateInstallationAsync(It.IsAny<Installation>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
    
    [Fact]
    public async Task Handle_WhenVarPackageNotFound_ReturnsFailure()
    {
        // Arrange
        var command = new InstallVarPackageCommand(varPackageId: 999, targetId: 1);
        
        _mockRepository
            .Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((VarPackage?)null);
        
        // Act
        var result = await _handler.Handle(command, CancellationToken.None);
        
        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
        result.ErrorCode.Should().Be(ErrorCode.VarPackageNotFound);
    }
}
```

### Infrastructure Layer Tests

**Test Repositories** (with in-memory database):
```csharp
public class VarPackageRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly VarPackageRepository _repository;
    
    public VarPackageRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _context = new ApplicationDbContext(options);
        _repository = new VarPackageRepository(_context);
    }
    
    [Fact]
    public async Task GetByIdAsync_WhenExists_ReturnsVarPackage()
    {
        // Arrange
        var package = new VarPackage { VarName = "Test.Var.1.var", RepositoryId = 1 };
        _context.VarPackages.Add(package);
        await _context.SaveChangesAsync();
        
        // Act
        var result = await _repository.GetByIdAsync(package.Id, CancellationToken.None);
        
        // Assert
        result.Should().NotBeNull();
        result!.VarName.Should().Be("Test.Var.1.var");
    }
    
    [Fact]
    public async Task SearchAsync_WithCriteria_ReturnsMatchingPackages()
    {
        // Arrange
        var packages = new[]
        {
            new VarPackage { VarName = "Creator1.Package1.1.var", CreatorName = "Creator1", RepositoryId = 1 },
            new VarPackage { VarName = "Creator2.Package2.1.var", CreatorName = "Creator2", RepositoryId = 1 },
            new VarPackage { VarName = "Creator1.Package3.1.var", CreatorName = "Creator1", RepositoryId = 1 }
        };
        
        _context.VarPackages.AddRange(packages);
        await _context.SaveChangesAsync();
        
        var criteria = new VarSearchCriteria { CreatorName = "Creator1" };
        
        // Act
        var results = await _repository.SearchAsync(criteria, CancellationToken.None);
        
        // Assert
        results.Should().HaveCount(2);
        results.Should().OnlyContain(p => p.CreatorName == "Creator1");
    }
    
    public void Dispose()
    {
        _context?.Dispose();
    }
}
```

## Integration Testing

### Database Integration Tests

**Use Testcontainers for PostgreSQL**:
```csharp
public class RepositoryIntegrationTests : IClassFixture<PostgreSQLFixture>
{
    private readonly ApplicationDbContext _context;
    
    public RepositoryIntegrationTests(PostgreSQLFixture fixture)
    {
        _context = fixture.CreateDbContext();
    }
    
    [Fact]
    public async Task AddVarPackage_ShouldPersistToDatabase()
    {
        // Arrange
        var repository = new VarPackageRepository(_context);
        var package = new VarPackage
        {
            VarName = "Creator.Package.1.var",
            CreatorName = "Creator",
            PackageName = "Package",
            Version = "1",
            RepositoryId = 1,
            FilePath = "C:\\Test\\file.var",
            FileSize = 1000
        };
        
        // Act
        await repository.AddAsync(package, CancellationToken.None);
        await _context.SaveChangesAsync();
        
        // Assert
        var saved = await repository.GetByIdAsync(package.Id, CancellationToken.None);
        saved.Should().NotBeNull();
        saved!.VarName.Should().Be("Creator.Package.1.var");
    }
}

public class PostgreSQLFixture : IDisposable
{
    private readonly PostgreSqlContainer _container;
    
    public PostgreSQLFixture()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:14")
            .WithPassword("postgres")
            .Build();
        
        _container.StartAsync().Wait();
    }
    
    public ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        
        var context = new ApplicationDbContext(options);
        context.Database.Migrate();
        return context;
    }
    
    public void Dispose()
    {
        _container?.DisposeAsync().AsTask().Wait();
    }
}
```

### Service Integration Tests

**Test service interactions**:
```csharp
public class VarPackageServiceIntegrationTests : IClassFixture<ServiceFixture>
{
    private readonly IVarPackageService _service;
    private readonly ApplicationDbContext _context;
    
    public VarPackageServiceIntegrationTests(ServiceFixture fixture)
    {
        _service = fixture.VarPackageService;
        _context = fixture.DbContext;
    }
    
    [Fact]
    public async Task InstallVarPackage_ShouldCreateInstallation()
    {
        // Arrange
        var repository = await CreateRepositoryAsync();
        var varPackage = await CreateVarPackageAsync(repository.Id);
        var target = await CreateInstallationTargetAsync();
        
        // Act
        var result = await _service.InstallVarPackageAsync(
            new InstallVarPackageCommand(varPackage.Id, target.Id),
            CancellationToken.None);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        
        var installation = await _context.Installations
            .FirstOrDefaultAsync(i => i.VarPackageId == varPackage.Id);
        installation.Should().NotBeNull();
    }
}
```

## End-to-End Testing

### UI Tests (Future)

**Using Playwright or similar**:
```csharp
public class InstallVarPackageE2ETests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task InstallVarPackage_ThroughUI_ShouldSucceed()
    {
        // Navigate to application
        // Select VAR package
        // Click Install button
        // Verify installation succeeds
        // Verify UI updates
    }
}
```

## Test Organization

### Project Structure

```
tests/
├── VirtaMatePackageManager.Core.Tests/
│   ├── Entities/
│   ├── ValueObjects/
│   └── Services/
│
├── VirtaMatePackageManager.Application.Tests/
│   ├── Commands/
│   ├── Queries/
│   └── Services/
│
├── VirtaMatePackageManager.Infrastructure.Tests/
│   ├── Repositories/
│   └── Services/
│
└── VirtaMatePackageManager.Integration.Tests/
    ├── Database/
    ├── Services/
    └── EndToEnd/
```

### Test Categories

**Use traits for test organization**:
```csharp
[Trait("Category", "Unit")]
public class VarPackageTests { }

[Trait("Category", "Integration")]
public class RepositoryIntegrationTests { }

[Trait("Category", "Slow")]
public class DatabaseMigrationTests { }
```

## Test Data Management

### Test Fixtures

**Reusable test data**:
```csharp
public static class TestDataFactory
{
    public static VarPackage CreateVarPackage(
        int repositoryId = 1,
        string varName = "Test.Creator.Package.1.var")
    {
        return new VarPackage
        {
            RepositoryId = repositoryId,
            VarName = varName,
            CreatorName = "Test.Creator",
            PackageName = "Package",
            Version = "1",
            FilePath = $"C:\\Test\\{varName}",
            FileSize = 1000
        };
    }
    
    public static Repository CreateRepository(string path = "C:\\Test\\Repo")
    {
        return new Repository
        {
            Name = "Test Repository",
            Path = path,
            Priority = 0,
            Enabled = true
        };
    }
}
```

## Code Coverage

### Coverage Goals

- **Overall**: 80%+
- **Domain Layer**: 90%+
- **Application Layer**: 80%+
- **Infrastructure Layer**: 70%+

### Coverage Reports

**Generate coverage report**:
```bash
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:coverage.cobertura.xml -targetdir:coverage
```

## Performance Testing

### Load Testing

**Test with large datasets**:
```csharp
[Fact]
public async Task SearchVarPackages_With10000Packages_CompletesQuickly()
{
    // Arrange
    var packages = Enumerable.Range(1, 10000)
        .Select(i => TestDataFactory.CreateVarPackage(varName: $"Creator.Package.{i}.var"))
        .ToList();
    
    await _context.VarPackages.AddRangeAsync(packages);
    await _context.SaveChangesAsync();
    
    // Act
    var stopwatch = Stopwatch.StartNew();
    var results = await _service.SearchVarPackagesAsync(
        new VarSearchQuery { CreatorName = "Creator" },
        CancellationToken.None);
    stopwatch.Stop();
    
    // Assert
    stopwatch.ElapsedMilliseconds.Should().BeLessThan(500);
}
```

## Continuous Integration

### CI Pipeline

**GitHub Actions example**:
```yaml
name: Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    services:
      postgres:
        image: postgres:14
        env:
          POSTGRES_PASSWORD: postgres
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '9.0.x'
      - run: dotnet restore
      - run: dotnet build
      - run: dotnet test --collect:"XPlat Code Coverage"
      - name: Upload coverage
        uses: codecov/codecov-action@v3
```

## Test Checklist

### Before Committing

- [ ] All tests pass
- [ ] Code coverage meets targets
- [ ] No skipped tests
- [ ] Tests are fast (< 5 minutes total)
- [ ] Tests are independent
- [ ] Tests have clear names

### Code Review

- [ ] Tests cover happy path
- [ ] Tests cover error cases
- [ ] Tests cover edge cases
- [ ] Tests are maintainable
- [ ] Test data is realistic

## Best Practices

1. **Test Behavior, Not Implementation**
   - Test what the code does, not how it does it
   - Avoid testing private methods directly

2. **Use Descriptive Test Names**
   ```csharp
   // ✅ Good
   [Fact]
   public void InstallVarPackage_WhenDependenciesMissing_ReturnsFailure()
   
   // ❌ Bad
   [Fact]
   public void Test1()
   ```

3. **Arrange-Act-Assert Pattern**
   - Clear separation of setup, execution, and verification

4. **One Assertion Per Test** (when possible)
   - Easier to understand what failed

5. **Test Isolation**
   - Each test should be independent
   - No shared state between tests

6. **Mock External Dependencies**
   - Database, file system, network calls

7. **Use Test Fixtures for Common Setup**
   - Avoid duplication

---

## Next Steps

Review documentation:
- [Development Guide](./07-Development-Guide.md) - Implementation details
- [Feature Specifications](./03-Feature-Specifications.md) - Requirements

