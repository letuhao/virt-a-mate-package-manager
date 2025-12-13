# Duplicate Management Strategy

## Overview

In the legacy system, duplicate file management was simple: only one repository existed, so duplicates were just moved to a "redundant" folder. In the new multi-repository system, duplicate management is more complex and requires sophisticated algorithms.

## Types of Duplicates

### 1. Filename Duplicates (Cross-Repository)
- **Definition**: Multiple files with the same VAR name (e.g., `Creator.Package.1.var`) in different repositories
- **Key Point**: VAR name is globally unique in the game, so only ONE copy can be used at a time
- **Example**: 
  - Repository A: `D:\Vars\Creator\Creator.Package.1.var`
  - Repository B: `E:\Vars\Creator\Creator.Package.1.var`
- **Database**: `var_name` has UNIQUE constraint - only one record per VAR name globally

### 2. Content Duplicates (Hash-based)
- **Definition**: Files with different names but identical content (same file hash)
- **Example**:
  - `Creator.Package.1.var` (hash: `abc123...`)
  - `Creator.Package.2.var` (hash: `abc123...`) - same content, different version number

### 3. Exact Duplicates
- **Definition**: Files with same name AND same content
- **Example**:
  - Repository A: `Creator.Package.1.var` (hash: `abc123...`)
  - Repository B: `Creator.Package.1.var` (hash: `abc123...`)

## Duplicate Detection Strategy

### Key Principle: VAR Name is Unique Identifier

- **Database**: `var_name` column has UNIQUE constraint
- **Game Requirement**: Game only accepts unique VAR filenames
- **Simplified Detection**: Query by VAR name - if multiple repositories have same VAR name, we need to resolve which one to use

### Phase 1: During Repository Scan

**Algorithm**: `DetectDuplicateVarFiles`
- Scans all enabled repositories
- Groups VAR packages by `var_name` (unique identifier)
- Identifies VAR names that exist in multiple repositories
- Determines primary file based on selection strategy

**When**: Runs automatically after scanning repositories

**Simplified Logic**:
```sql
-- Simple query to find duplicates
SELECT var_name, COUNT(*) as count
FROM var_packages
GROUP BY var_name
HAVING COUNT(*) > 1;
```

### Phase 2: During Organization

**Algorithm**: `OrganizeVarFiles`
- Handles local duplicates within single repository
- Detects cross-repository duplicates
- Logs warnings but doesn't auto-resolve
- Moves only local duplicates to redundant folder

**When**: When organizing files in a repository

### Phase 3: During Installation

**Algorithm**: `ResolveDuplicateVarFile` + `HandleDuplicateDuringInstallation`
- **Simple Query**: Query database by VAR name (unique key)
  ```sql
  SELECT * FROM var_packages WHERE var_name = @varName
  ```
- If multiple results: Apply selection strategy to choose primary
- Checks user preferences (which repository to prefer)
- Creates symlink from primary file

**When**: When user installs a VAR package

**Advantages of VAR Name as Unique Key**:
- ✅ Fast lookup (indexed)
- ✅ Simple query (no complex joins)
- ✅ Matches game's requirement (unique filenames)
- ✅ Easy to resolve duplicates

## Primary File Selection Strategies

When multiple duplicates exist, the system must decide which file is "primary" (used by default).

### Available Strategies

1. **HighestPriorityRepository** (Recommended Default)
   - Uses file from repository with highest priority
   - If priority same, uses newest file
   - Ensures consistent behavior based on repository importance

2. **NewestFile**
   - Uses most recently modified file
   - Assumes newer = better/correct version

3. **OldestFile**
   - Uses oldest file
   - Assumes original is better

4. **LargestFile**
   - Uses largest file size
   - Assumes larger = more complete

5. **SmallestFile**
   - Uses smallest file size
   - Assumes smaller = more optimized

6. **FirstFound**
   - Uses first file encountered during scan
   - Deterministic but may not be optimal

### Configuration

```json
{
  "DuplicateManagement": {
    "DefaultSelectionStrategy": "HighestPriorityRepository",
    "DetectionOptions": {
      "DetectContentDuplicates": true,
      "DetectFilenameDuplicates": true,
      "MinWasteSizeToReport": 1048576  // 1MB
    },
    "MarkingOptions": {
      "MarkPrimary": true,
      "MarkDuplicates": true,
      "AutoResolve": false
    }
  }
}
```

## Database Schema for Duplicates

### Key Constraint

**var_packages** table:
```sql
-- VAR name is globally unique
CONSTRAINT uk_var_name UNIQUE (var_name)
```

This ensures:
- Only one VAR package record per VAR name in the database
- If same VAR name exists in multiple repositories, we need to handle it at application level
- Fast lookup by VAR name (indexed)

### Optional: Duplicate Tracking Tables

If you want to track historical duplicates across repositories:

**duplicate_groups** (optional)
```sql
CREATE TABLE duplicate_groups (
    id SERIAL PRIMARY KEY,
    var_name VARCHAR(500) NOT NULL,
    primary_var_package_id INTEGER REFERENCES var_packages(id),
    total_wasted_space BIGINT,
    detected_at TIMESTAMP NOT NULL,
    resolved_at TIMESTAMP,
    resolution_strategy VARCHAR(50),
    UNIQUE (var_name)
);
```

**var_packages** - No additional columns needed if using simple approach:
- VAR name is unique (enforced by constraint)
- Query by VAR name to find all copies across repositories
- Apply selection strategy to choose primary

## User Workflow

### 1. Automatic Detection

```
User scans repositories
    ↓
System detects duplicates automatically
    ↓
Duplicates marked in database
    ↓
User sees duplicate count in UI
```

### 2. Review Duplicates

```
User opens "Duplicate Management" view
    ↓
System shows all duplicate groups
    ↓
User can:
    - See all copies of each duplicate
    - Compare file details (size, date, repository)
    - Choose which is primary
    - Delete redundant copies
```

### 3. Installation Resolution

```
User installs VAR package
    ↓
System detects duplicate exists
    ↓
System checks user preference (if exists)
    ↓
If no preference: Use selection strategy
    ↓
Create symlink from primary file
```

## Implementation Functions

### Core Functions

1. **DetectDuplicateVarFiles** - Detect all duplicates across repositories
2. **ResolveDuplicateVarFile** - Determine which duplicate to use
3. **MarkDuplicateVarFiles** - Mark duplicates in database
4. **OrganizeVarFiles** - Handle duplicates during organization
5. **HandleDuplicateDuringInstallation** - Resolve during install

### Helper Functions

1. **DeterminePrimaryFile** - Apply selection strategy
2. **BuildExistingFilesMap** - Build cross-repo file map
3. **CheckCrossRepositoryDuplicate** - Check if file exists elsewhere
4. **GetUserPreferenceForDuplicate** - Get user's choice

## Example Scenarios

### Scenario 1: Exact Duplicate

**Situation**:
- Repository A (Priority 10): `Creator.Package.1.var` (hash: `abc123`)
- Repository B (Priority 5): `Creator.Package.1.var` (hash: `abc123`)

**Detection**:
- Type: ExactDuplicate
- Both files identical

**Resolution**:
- Strategy: HighestPriorityRepository
- Primary: Repository A (higher priority)
- Action: Mark Repository B copy as duplicate
- User can delete Repository B copy to save space

### Scenario 2: Filename Duplicate (Different Content)

**Situation**:
- Repository A: `Creator.Package.1.var` (hash: `abc123`, modified: 2024-01-01)
- Repository B: `Creator.Package.1.var` (hash: `def456`, modified: 2024-01-15)

**Detection**:
- Type: FilenameDuplicate
- Warning: Same name but different content!

**Resolution**:
- User must manually review
- System warns about potential issue
- User decides which is correct version
- Option: Rename one file to avoid conflict

### Scenario 3: Content Duplicate (Different Names)

**Situation**:
- Repository A: `Creator.Package.1.var` (hash: `abc123`)
- Repository B: `Creator.Package.2.var` (hash: `abc123`)

**Detection**:
- Type: ContentDuplicate
- Same content, different version numbers

**Resolution**:
- User can choose which name/version to keep
- System suggests keeping newer version number
- Mark older version as duplicate

## Performance Considerations

### Hash Computation

- **Challenge**: Computing file hash is expensive for large files
- **Solution**: 
  - Cache hashes in database
  - Only recompute if file modified
  - Use async hash computation
  - Parallel processing for multiple files

### Cross-Repository Scanning

- **Challenge**: Scanning all repositories for duplicates is slow
- **Solution**:
  - Incremental detection (only new/changed files)
  - Background processing
  - Database-based lookup (don't rescan file system)
  - Batch processing

## User Interface Features

### Duplicate Management View

1. **List View**
   - Shows all duplicate groups
   - Summary: count, wasted space
   - Filter by type, repository

2. **Detail View**
   - Shows all copies of duplicate
   - Compare side-by-side:
     - Repository
     - File size
     - Modified date
     - Hash
   - Actions:
     - Set as primary
     - Delete duplicate
     - Open file location

3. **Statistics**
   - Total duplicates
   - Total wasted space
   - Duplicates by repository
   - Potential space savings

## Best Practices

1. **Regular Detection**: Run duplicate detection after repository scans
2. **User Review**: Always let user review before auto-deleting
3. **Backup**: Don't delete duplicates without backup option
4. **Priority Setting**: Configure repository priorities appropriately
5. **Hash Caching**: Cache file hashes to improve performance

## Migration from Legacy System

**Legacy Behavior**:
- Single repository
- Duplicates moved to "redundant" folder immediately
- No cross-repository awareness

**New System Behavior**:
- Multiple repositories supported
- Duplicates detected but not auto-moved
- User can choose resolution strategy
- Database tracks all duplicates

**Migration Notes**:
- Existing redundant folder files can be scanned
- Files marked as duplicates automatically
- User can review and decide on cleanup

---

## Summary

The new duplicate management system provides:
- ✅ Cross-repository duplicate detection
- ✅ Multiple selection strategies
- ✅ User control over resolution
- ✅ Database tracking
- ✅ Performance optimizations
- ✅ Flexible configuration

All functions are documented in `09-Function-Specifications.md` section 6.

