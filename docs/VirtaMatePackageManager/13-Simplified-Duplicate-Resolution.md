# Simplified Duplicate Resolution with VAR Name as Unique Key

## Key Design Decision

**VAR name is UNIQUE in database** - This simplifies everything!

## Database Constraint

```sql
CONSTRAINT uk_var_name UNIQUE (var_name)
```

This means:
- ✅ Only ONE record per VAR name in database
- ✅ Fast O(1) lookup by VAR name
- ✅ Database enforces uniqueness automatically
- ✅ No complex duplicate tracking needed

## How It Works

### Scenario: Same VAR in Multiple Repositories

**Example**:
```
Repository A (Priority 10): D:\Vars\Creator\Creator.Package.1.var
Repository B (Priority 5):  E:\Vars\Creator\Creator.Package.1.var
```

**Database Storage**:
- Only ONE record: `var_name = "Creator.Package.1"`
- `repository_id` = Repository A (higher priority)
- Repository B file exists on disk but NOT in database

**When Scanning Repository B**:
1. Check: Does VAR name exist in database?
2. Yes → Skip (already indexed from Repository A)
3. Log: "VAR Creator.Package.1 already exists, skipping"

**When Installing**:
1. Query database by VAR name
2. Get single record (from Repository A)
3. Install from Repository A's file path
4. Create symlink

## Simplified Detection Algorithm

```csharp
public async Task<Result<VarPackage>> ScanVarFileAsync(
    string filePath, 
    int repositoryId)
{
    var varName = ParseVarFileName(filePath);
    
    // Simple check: does VAR name exist?
    var existing = await _context.VarPackages
        .Where(v => v.VarName == varName)
        .FirstOrDefaultAsync();
    
    if (existing != null)
    {
        // VAR already exists
        if (existing.RepositoryId == repositoryId)
        {
            // Same repository - update if file changed
            return await UpdateExistingVarPackageAsync(existing, filePath);
        }
        else
        {
            // Different repository - skip (UNIQUE constraint)
            return Result.Failure(
                $"VAR {varName} already indexed from repository {existing.RepositoryId}",
                ErrorCode.DuplicateVarName
            );
        }
    }
    
    // New VAR - add to database
    return await AddNewVarPackageAsync(varName, filePath, repositoryId);
}
```

## Benefits

### 1. Simplicity
- ✅ No complex duplicate detection
- ✅ Simple database queries
- ✅ Clear logic flow

### 2. Performance
- ✅ Single indexed lookup
- ✅ No joins needed
- ✅ Fast insert/update

### 3. Correctness
- ✅ Database enforces uniqueness
- ✅ Cannot have duplicate records
- ✅ Predictable behavior

### 4. Matches Game Requirements
- ✅ Game only accepts unique VAR names
- ✅ System enforces same constraint
- ✅ Consistent behavior

## Handling Repository Priority

When same VAR exists in multiple repositories:

```csharp
public async Task<Result<VarPackage>> ResolveVarPackageAsync(
    string varName,
    SelectionStrategy strategy)
{
    // Query - will return at most 1 record (UNIQUE constraint)
    var varPackage = await _context.VarPackages
        .Where(v => v.VarName == varName)
        .FirstOrDefaultAsync();
    
    if (varPackage == null)
        return Result.Failure("VAR not found", ErrorCode.NotFound);
    
    return Result.Success(varPackage);
}
```

**Selection happens during scan**, not during resolution:
- First repository scanned wins (or based on priority)
- Subsequent repositories' copies are skipped
- Only primary repository's file is indexed

## Migration Strategy

### Option 1: Repository Priority
- Scan repositories by priority (high to low)
- First VAR encountered gets indexed
- Later copies are skipped

### Option 2: User Choice
- Detect conflict during scan
- Ask user which repository to use
- Index selected repository's file
- Skip others

### Option 3: Update Existing
- If VAR exists with different repository
- Check if new repository has higher priority
- Update record if yes
- Skip if no

## Example Implementation

```csharp
public async Task<Result> ScanRepositoryAsync(int repositoryId)
{
    var repository = await GetRepositoryAsync(repositoryId);
    var varFiles = ScanFileSystem(repository.Path);
    
    foreach (var varFile in varFiles)
    {
        var varName = ParseVarFileName(varFile);
        
        // Simple check
        var existing = await _context.VarPackages
            .FirstOrDefaultAsync(v => v.VarName == varName);
        
        if (existing != null)
        {
            // Already exists - handle based on repository
            if (existing.RepositoryId == repositoryId)
            {
                // Update from same repository
                await UpdateVarPackageAsync(existing, varFile);
            }
            else
            {
                // Different repository - check if should replace
                var shouldReplace = ShouldReplaceRepository(
                    existing, 
                    repository, 
                    SelectionStrategy
                );
                
                if (shouldReplace)
                {
                    // Update to new repository
                    existing.RepositoryId = repositoryId;
                    existing.FilePath = varFile;
                    await _context.SaveChangesAsync();
                }
                else
                {
                    // Skip - keep existing repository
                    LogInfo($"Skipping {varName} - already from repository {existing.RepositoryId}");
                }
            }
        }
        else
        {
            // New VAR - add it
            await AddVarPackageAsync(varName, varFile, repositoryId);
        }
    }
}
```

## Summary

**VAR name as UNIQUE key = Simple duplicate management**

- ✅ Database constraint enforces uniqueness
- ✅ Simple queries (no complex logic)
- ✅ Fast lookups (indexed)
- ✅ Matches game requirements
- ✅ Clear, predictable behavior

**Rule**: One VAR name = One database record. Multiple repositories can have same VAR file, but only primary repository's copy is indexed in database.

