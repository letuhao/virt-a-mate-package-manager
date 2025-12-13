# VAR Name as Unique Key - Design Rationale

## Overview

In VirtaMatePackageManager, **VAR name** is used as the unique identifier. This design decision simplifies duplicate detection and matches the game's requirements.

## Why VAR Name as Unique Key?

### 1. Game Requirement
- **Virt-a-Mate only accepts unique VAR filenames**
- If two VAR files with the same name exist, only one can be used
- The game identifies VAR packages by filename, not by content

### 2. Simplified Database Design
- **Single UNIQUE constraint** on `var_name` column
- No need for complex composite keys
- Fast lookups using indexed column

### 3. Simplified Duplicate Detection
- **Simple Query**: `SELECT * FROM var_packages WHERE var_name = @varName`
- If multiple results exist, they're from different repositories
- Apply selection strategy to choose primary

### 4. Matches Legacy System
- Legacy system also uses `varName` as unique identifier
- Database schema uses `var_name` as unique constraint
- Consistent with existing data structure

## Database Schema

### var_packages Table

```sql
CREATE TABLE var_packages (
    id SERIAL PRIMARY KEY,
    repository_id INTEGER NOT NULL REFERENCES repositories(id) ON DELETE CASCADE,
    var_name VARCHAR(500) NOT NULL UNIQUE,  -- UNIQUE globally
    creator_name VARCHAR(255) NOT NULL,
    package_name VARCHAR(255) NOT NULL,
    version VARCHAR(50) NOT NULL,
    file_path TEXT NOT NULL,
    -- ... other columns
    CONSTRAINT uk_var_name UNIQUE (var_name)
);

CREATE INDEX idx_var_packages_var_name ON var_packages(var_name);
```

**Key Points**:
- `var_name` has UNIQUE constraint
- Indexed for fast lookups
- One VAR package per VAR name globally

## How Duplicates are Handled

### Scenario: Same VAR Name in Multiple Repositories

**Example**:
```
Repository A: D:\Vars\Creator\Creator.Package.1.var
Repository B: E:\Vars\Creator\Creator.Package.1.var
```

**Database Behavior**:
- Both files are scanned and stored in database
- Both have same `var_name`: `Creator.Package.1`
- **BUT**: Only one record in `var_packages` table (UNIQUE constraint)
- When second file is scanned: Update existing record OR mark as duplicate

### Detection Algorithm

```sql
-- Find VAR names with multiple repository copies
SELECT var_name, COUNT(*) as repository_count
FROM var_packages
GROUP BY var_name
HAVING COUNT(*) > 1;
```

**Simplified Logic**:
1. Query database by VAR name
2. If multiple repositories have same VAR:
   - Apply selection strategy
   - Choose primary repository
   - Mark others as duplicates

## Installation Resolution

When installing a VAR package:

```csharp
// Simple lookup
var varPackage = await _repository.GetByVarNameAsync(varName);

// If multiple repositories have same VAR name:
// 1. Query all copies
var allCopies = await _repository.GetAllByVarNameAsync(varName);

// 2. Apply selection strategy
var primary = DeterminePrimary(allCopies, strategy);

// 3. Install from primary
await InstallFromVarPackage(primary);
```

## Advantages

### 1. Performance
- ✅ Fast lookups (indexed unique key)
- ✅ Simple queries (no complex joins)
- ✅ O(1) lookup by VAR name

### 2. Simplicity
- ✅ One constraint, one index
- ✅ No complex duplicate tracking tables
- ✅ Straightforward resolution logic

### 3. Consistency
- ✅ Matches game's behavior
- ✅ Matches legacy system
- ✅ Predictable behavior

### 4. Correctness
- ✅ Enforces uniqueness at database level
- ✅ Prevents data inconsistencies
- ✅ Clear semantics

## Handling Edge Cases

### Case 1: Same VAR Name, Different Content

**Rare but possible**:
- Repository A: `Creator.Package.1.var` (hash: `abc123`)
- Repository B: `Creator.Package.1.var` (hash: `def456`)

**Solution**:
- Database constraint prevents storing both
- During scan: Compare hashes
- If different: Log warning, keep one based on strategy
- User can manually review

### Case 2: Repository Priority

**When multiple repositories have same VAR**:
- Use repository priority to choose primary
- Higher priority repository wins
- User can override preference

### Case 3: File Updates

**When VAR file is updated in one repository**:
- Update existing record in database
- Keep VAR name same
- Update metadata, hash, timestamps
- No duplicate created

## Implementation Example

### Resolve VAR Package

```csharp
public async Task<Result<VarPackage>> GetVarPackageAsync(string varName)
{
    // Simple query by unique key
    var varPackage = await _context.VarPackages
        .Where(v => v.VarName == varName)
        .FirstOrDefaultAsync();
    
    if (varPackage == null)
        return Result<VarPackage>.Failure("VAR not found", ErrorCode.NotFound);
    
    return Result<VarPackage>.Success(varPackage);
}
```

### Handle Multiple Repositories

```csharp
public async Task<Result<VarPackage>> ResolveVarPackageAsync(
    string varName, 
    SelectionStrategy strategy)
{
    // Get all copies from all repositories
    var allCopies = await _context.VarPackages
        .Include(v => v.Repository)
        .Where(v => v.VarName == varName)
        .ToListAsync();
    
    if (allCopies.Count == 0)
        return Result<VarPackage>.Failure("VAR not found", ErrorCode.NotFound);
    
    if (allCopies.Count == 1)
        return Result<VarPackage>.Success(allCopies[0]);
    
    // Multiple copies - apply strategy
    var primary = DeterminePrimary(allCopies, strategy);
    return Result<VarPackage>.Success(primary);
}
```

## Summary

Using VAR name as unique key:
- ✅ Simplifies database design
- ✅ Matches game requirements
- ✅ Provides fast lookups
- ✅ Makes duplicate detection straightforward
- ✅ Consistent with legacy system

**Rule**: One VAR name = One VAR package record globally. If same VAR name exists in multiple repositories, application logic resolves which one to use based on selection strategy.

