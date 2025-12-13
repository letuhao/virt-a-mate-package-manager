# Architecture Overview

## System Architecture

VirtaMatePackageManager follows **Clean Architecture** principles with clear separation of concerns across multiple layers.

## Architectural Layers

```
┌─────────────────────────────────────────────────────────────┐
│                   Presentation Layer                         │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐      │
│  │     WPF      │  │    API       │  │  CLI/Tools   │      │
│  │   Desktop    │  │   (Future)   │  │  (Future)    │      │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘      │
└─────────┼──────────────────┼──────────────────┼─────────────┘
          │                  │                  │
┌─────────┼──────────────────┼──────────────────┼─────────────┐
│         │   Application Layer (Use Cases)      │             │
│  ┌──────▼──────────────────────────────────────▼───────┐    │
│  │  • InstallVarUseCase                              │    │
│  │  • UninstallVarUseCase                            │    │
│  │  • ScanRepositoryUseCase                          │    │
│  │  • SearchVarUseCase                               │    │
│  │  • ManageDependenciesUseCase                      │    │
│  └────────────────────────────────────────────────────┘    │
└─────────┼───────────────────────────────────────────────────┘
          │
┌─────────┼───────────────────────────────────────────────────┐
│         │   Domain Layer (Core Business Logic)              │
│  ┌──────▼───────────────────────────────────────┐          │
│  │  • VarPackage (Entity)                       │          │
│  │  • Repository (Entity)                       │          │
│  │  • InstallationTarget (Entity)               │          │
│  │  • DependencyGraph (Domain Service)          │          │
│  │  • VarValidator (Domain Service)             │          │
│  └──────────────────────────────────────────────┘          │
└─────────┼───────────────────────────────────────────────────┘
          │
┌─────────┼───────────────────────────────────────────────────┐
│         │   Infrastructure Layer                             │
│  ┌──────▼───────────────────────────────────────┐          │
│  │  • PostgreSQL Repository                     │          │
│  │  • FileSystemService                         │          │
│  │  • SymlinkService                            │          │
│  │  • ZipService                                │          │
│  │  • JsonParser                                │          │
│  └──────────────────────────────────────────────┘          │
└─────────────────────────────────────────────────────────────┘
```

## Layer Responsibilities

### Presentation Layer

**Responsibilities**:
- User interface (WPF/Avalonia)
- User input validation
- View models (MVVM pattern)
- UI event handling
- Presentation-specific logic

**Dependencies**: Application Layer only

**Technologies**:
- WPF or Avalonia UI
- MVVM Toolkit
- CommunityToolkit.Mvvm

### Application Layer

**Responsibilities**:
- Use case orchestration
- Business workflow coordination
- Transaction management
- Input/output DTOs
- Application-specific business rules

**Dependencies**: Domain Layer, Infrastructure Layer (via interfaces)

**Patterns**:
- Command/Query Separation (CQRS)
- Mediator pattern (MediatR)
- Result pattern for error handling

### Domain Layer

**Responsibilities**:
- Core business entities
- Domain logic and rules
- Value objects
- Domain events
- Business invariants

**Dependencies**: None (pure business logic)

**Key Entities**:
- `VarPackage`: Represents a VAR file with metadata
- `Repository`: Represents a storage location for VAR files
- `InstallationTarget`: Represents a target directory for installations
- `Dependency`: Represents dependency relationships

### Infrastructure Layer

**Responsibilities**:
- Database access (EF Core)
- File system operations
- Symbolic link creation
- ZIP file handling
- JSON parsing
- External service integration

**Dependencies**: Domain Layer (implements interfaces)

**Technologies**:
- Entity Framework Core 9.0
- Npgsql (PostgreSQL provider)
- System.IO.Abstractions (for testability)

## Design Patterns

### 1. Repository Pattern

**Purpose**: Abstract data access

```csharp
public interface IVarPackageRepository
{
    Task<VarPackage?> GetByIdAsync(int id, CancellationToken ct);
    Task<IEnumerable<VarPackage>> SearchAsync(VarSearchCriteria criteria, CancellationToken ct);
    Task AddAsync(VarPackage package, CancellationToken ct);
    Task UpdateAsync(VarPackage package, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
```

### 2. Unit of Work Pattern

**Purpose**: Manage transactions and coordinate repositories

```csharp
public interface IUnitOfWork
{
    IVarPackageRepository VarPackages { get; }
    IRepositoryRepository Repositories { get; }
    IInstallationTargetRepository InstallationTargets { get; }
    
    Task<int> SaveChangesAsync(CancellationToken ct);
    Task BeginTransactionAsync(CancellationToken ct);
    Task CommitTransactionAsync(CancellationToken ct);
    Task RollbackTransactionAsync(CancellationToken ct);
}
```

### 3. Mediator Pattern (CQRS)

**Purpose**: Decouple requests from handlers

```csharp
// Commands
public record InstallVarCommand(int VarPackageId, int TargetId) : IRequest<Result<int>>;
public record UninstallVarCommand(int VarPackageId, int TargetId) : IRequest<Result>;

// Queries
public record SearchVarQuery(VarSearchCriteria Criteria) : IRequest<Result<IEnumerable<VarPackageDto>>>;

// Handlers
public class InstallVarCommandHandler : IRequestHandler<InstallVarCommand, Result<int>>
{
    // Implementation
}
```

### 4. Result Pattern

**Purpose**: Explicit error handling without exceptions

```csharp
public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    public ErrorCode? ErrorCode { get; }
    
    public static Result<T> Success(T value);
    public static Result<T> Failure(string error, ErrorCode code);
}
```

### 5. Strategy Pattern

**Purpose**: Different algorithms for VAR scanning

```csharp
public interface IVarScannerStrategy
{
    Task<VarPackageMetadata> ScanAsync(string varFilePath, CancellationToken ct);
}

public class ParallelVarScannerStrategy : IVarScannerStrategy { }
public class SequentialVarScannerStrategy : IVarScannerStrategy { }
```

### 6. Factory Pattern

**Purpose**: Create VAR packages from different sources

```csharp
public interface IVarPackageFactory
{
    Task<VarPackage> CreateFromFileAsync(string filePath, int repositoryId, CancellationToken ct);
    Task<VarPackage> CreateFromMetadataAsync(VarPackageMetadata metadata, int repositoryId, CancellationToken ct);
}
```

## Technology Stack

### Core Framework

- **.NET 9.0**: Latest framework with performance improvements
- **C# 13**: Modern language features

### Data Access

- **Entity Framework Core 9.0**: ORM for PostgreSQL
- **Npgsql.EntityFrameworkCore.PostgreSQL**: PostgreSQL provider
- **PostgreSQL 14+**: Database server

### Dependency Injection

- **Microsoft.Extensions.DependencyInjection**: Built-in DI container
- **Scrutor**: Advanced DI features (decorator pattern support)

### Logging

- **Serilog**: Structured logging
- **Serilog.Sinks.File**: File logging
- **Serilog.Sinks.Console**: Console logging

### Configuration

- **Microsoft.Extensions.Configuration**: Configuration management
- **appsettings.json**: JSON-based configuration

### Validation

- **FluentValidation**: Validation library
- **Data Annotations**: Attribute-based validation

### Mapping

- **Mapster** or **AutoMapper**: Object mapping

### Testing

- **xUnit**: Unit testing framework
- **Moq**: Mocking framework
- **FluentAssertions**: Assertion library
- **Testcontainers.PostgreSql**: Integration testing

## Data Flow

### Install VAR Flow

```
User Action (UI)
    ↓
InstallVarCommand
    ↓
InstallVarCommandHandler
    ↓
[Check Dependencies] → DependencyResolver
    ↓
[Validate Target] → TargetValidator
    ↓
[Create Symlink] → SymlinkService
    ↓
[Update Database] → UnitOfWork
    ↓
[Save Changes] → PostgreSQL
    ↓
Result Returned
    ↓
UI Update (via event/messaging)
```

### Scan Repository Flow

```
User Action / Scheduled Task
    ↓
ScanRepositoryCommand
    ↓
ScanRepositoryCommandHandler
    ↓
[Get Files] → FileSystemService (async, parallel)
    ↓
[Parse Metadata] → VarMetadataParser (parallel)
    ↓
[Compare with DB] → VarPackageRepository
    ↓
[Update/Insert] → UnitOfWork (batch)
    ↓
[Commit Transaction] → PostgreSQL
    ↓
[Notify UI] → Event Publisher
```

## Async/Await Strategy

### All I/O Operations Are Async

- Database queries: `Task<T>`
- File operations: `IAsyncEnumerable<T>`
- ZIP extraction: `Task`
- Network operations: `Task<T>`

### Parallel Processing

```csharp
// Parallel VAR scanning
await Parallel.ForEachAsync(
    varFiles,
    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
    async (varFile, ct) =>
    {
        var metadata = await scanner.ScanAsync(varFile, ct);
        await repository.AddAsync(metadata, ct);
    });
```

### Cancellation Support

All long-running operations support `CancellationToken`:

```csharp
public async Task<Result<IEnumerable<VarPackage>>> ScanRepositoryAsync(
    int repositoryId,
    CancellationToken cancellationToken = default)
{
    // Check cancellation periodically
    cancellationToken.ThrowIfCancellationRequested();
    // ...
}
```

## Error Handling Strategy

### Result Pattern for Business Logic

```csharp
public async Task<Result<int>> InstallVarAsync(
    int varPackageId,
    int targetId,
    CancellationToken ct)
{
    var varPackage = await _repository.GetByIdAsync(varPackageId, ct);
    if (varPackage == null)
        return Result<int>.Failure("VAR package not found", ErrorCode.VarNotFound);
    
    // Continue with success path
    return Result<int>.Success(installationId);
}
```

### Exception Handling

- **Exceptions**: Only for unexpected errors (system exceptions)
- **Results**: For expected business errors (validation, not found, etc.)
- **Logging**: All errors logged with context

## Security Considerations

### Input Validation

- All user input validated
- Path traversal prevention
- SQL injection prevention (EF Core parameterized queries)
- File path sanitization

### File System Operations

- Symbolic link target validation
- Path normalization
- Permission checks before operations

### Database Security

- Connection string encryption
- Parameterized queries only
- Principle of least privilege for DB user

## Performance Optimizations

### Database

- Proper indexing on search columns
- Connection pooling
- Batch operations for bulk inserts
- Query optimization with EF Core

### Caching

- In-memory caching for frequently accessed data
- Repository metadata caching
- Dependency graph caching

### UI Responsiveness

- Virtual scrolling for large lists
- Lazy loading of data
- Progress reporting with cancellation
- Background processing for long operations

## Scalability Considerations

### Horizontal Scaling

- Database can be on separate server
- File repositories can be network locations
- API layer (future) can scale independently

### Vertical Scaling

- Parallel processing uses all CPU cores
- Efficient memory usage with streaming
- Connection pooling for database

## Next Steps

Continue to:
- [Database Schema](./02-Database-Schema.md) - Detailed database design
- [Feature Specifications](./03-Feature-Specifications.md) - Feature requirements

