# Migration Plan

## Overview

This document outlines the migration strategy from the legacy varManager system (Microsoft Access) to VirtaMatePackageManager (PostgreSQL). The migration must preserve all user data and configurations while improving performance and functionality.

## Migration Goals

1. **Zero Data Loss**: All VAR packages, installations, and dependencies migrated
2. **Backward Compatibility**: Import existing varManager database
3. **Seamless Transition**: Users can continue using existing repositories
4. **Performance Improvement**: Faster operations post-migration
5. **Rollback Capability**: Ability to revert if needed

## Migration Strategy

### Phase 1: Preparation

#### 1.1 Database Analysis

**Tasks**:
- Analyze existing Microsoft Access database structure
- Document all tables, relationships, and data
- Identify data quality issues
- Estimate data volumes

**Deliverables**:
- Database schema documentation
- Data volume report
- Data quality assessment

#### 1.2 Migration Tool Development

**Components**:
- Access database reader (OLEDB connection)
- Data mapper (Access → PostgreSQL)
- Validation tool
- Rollback tool

**Tools**:
```csharp
public interface ILegacyDatabaseReader
{
    Task<LegacyDatabaseSnapshot> ReadAsync(string accessDbPath);
}

public interface IMigrationMapper
{
    Task<MigrationResult> MigrateAsync(LegacyDatabaseSnapshot snapshot);
}
```

#### 1.3 Backup Strategy

**Before Migration**:
1. Backup existing Access database (.mdb file)
2. Backup repository directories (optional, for safety)
3. Document current installation state
4. Create rollback plan

**Backup Locations**:
- User backup directory
- Automatic backup with timestamp
- Cloud backup option (future)

### Phase 2: Data Migration

#### 2.1 Migration Steps

**Step 1: Setup New Database**
```
1. Create PostgreSQL database
2. Run EF Core migrations
3. Verify schema created correctly
```

**Step 2: Connect to Legacy Database**
```
1. Verify Access database accessible
2. Test connection
3. Read database structure
```

**Step 3: Migrate Core Data**
```
1. Migrate repositories → repositories table
2. Migrate VAR packages → var_packages table
3. Migrate dependencies → dependencies table
4. Migrate installations → installations table
```

**Step 4: Validate Migration**
```
1. Count records (source vs destination)
2. Verify relationships
3. Spot-check data integrity
4. Run validation queries
```

**Step 5: Post-Migration Tasks**
```
1. Resolve dependencies
2. Verify symlinks still valid
3. Rebuild indexes
4. Analyze statistics
```

#### 2.2 Data Mapping

**Repository Mapping**:
```
Legacy: Settings.Default.varspath (single repository)
New: Multiple repositories table

Mapping:
- Create single repository entry
- Path: Legacy varspath
- Name: "Legacy Repository"
- Priority: 0 (default)
- Enabled: true
```

**VAR Package Mapping**:
```
Legacy: varManagerDataSet.vars
New: var_packages

Mapping:
- varName → var_name, creator_name, package_name, version (parsed)
- filePath → file_path
- All metadata fields mapped directly
- repository_id → newly created repository
```

**Dependency Mapping**:
```
Legacy: varManagerDataSet.dependencies
New: dependencies

Mapping:
- varName → var_package_id (lookup)
- dependency → dependency_name
- Resolve resolved_var_package_id after migration
```

**Installation Mapping**:
```
Legacy: Symbolic links in AddonPackages
New: installations table

Mapping:
- Scan AddonPackages directory for symlinks
- Match symlinks to var_packages by path
- Create installation records
- Verify symlinks valid
```

#### 2.3 Migration Tool Implementation

**Console Application**:

```csharp
public class MigrationTool
{
    public async Task<MigrationResult> MigrateAsync(MigrationOptions options)
    {
        // 1. Read legacy database
        var snapshot = await _legacyReader.ReadAsync(options.AccessDbPath);
        
        // 2. Create new database
        await _databaseCreator.CreateDatabaseAsync(options.PostgresConnectionString);
        
        // 3. Migrate data
        var result = await _mapper.MigrateAsync(snapshot);
        
        // 4. Validate
        var validation = await _validator.ValidateAsync(result);
        
        return new MigrationResult(result, validation);
    }
}
```

**Command Line Interface**:
```bash
VirtaMatePackageManager.Migrate.exe --source "C:\varManager\varManager.mdb" --target "postgresql://..."
```

### Phase 3: Configuration Migration

#### 3.1 Settings Migration

**Legacy Settings** (app.config):
- `vampath`: VaM installation path
- `varspath`: VAR repository path

**New Configuration** (appsettings.json):
```json
{
  "VaM": {
    "InstallationPath": "C:\\VaM"
  },
  "Repositories": [
    {
      "Path": "D:\\Vars",
      "Name": "Main Repository",
      "Priority": 0,
      "Enabled": true
    }
  ],
  "InstallationTargets": [
    {
      "Path": "C:\\VaM\\AddonPackages",
      "Name": "Default",
      "IsActive": true
    }
  ]
}
```

**Migration Process**:
1. Read legacy settings
2. Create configuration file
3. Create repositories from legacy paths
4. Create installation target from legacy path

### Phase 4: Verification

#### 4.1 Data Verification

**Validation Queries**:
```sql
-- Count check
SELECT COUNT(*) FROM var_packages;
-- Should match legacy count

-- Dependency check
SELECT COUNT(*) FROM dependencies WHERE is_resolved = FALSE;
-- Should be minimal

-- Installation check
SELECT COUNT(*) FROM installations;
-- Should match symlink count in AddonPackages
```

#### 4.2 Functional Verification

**Test Cases**:
1. ✅ All VAR packages visible in UI
2. ✅ Search/filter works correctly
3. ✅ Installation status correct
4. ✅ Dependencies resolved
5. ✅ Can install new VAR
6. ✅ Can uninstall VAR
7. ✅ Repository scanning works

### Phase 5: Rollback Plan

#### 5.1 Rollback Scenarios

**If Migration Fails**:
1. Keep original Access database (backup)
2. New PostgreSQL database can be deleted
3. User continues with legacy system
4. Fix migration issues and retry

**If Post-Migration Issues**:
1. Both databases exist (Access backup + PostgreSQL)
2. Export data back to Access format (if needed)
3. Use legacy system temporarily
4. Fix issues and re-migrate

#### 5.2 Rollback Tools

**Export to Legacy Format**:
```csharp
public interface ILegacyExporter
{
    Task ExportToAccessAsync(string accessDbPath);
}
```

## Migration Workflow

### User-Facing Workflow

```
1. User opens VirtaMatePackageManager for first time
2. System detects no PostgreSQL database exists
3. Migration wizard appears:
   
   ┌─────────────────────────────────────┐
   │ Welcome to VirtaMatePackageManager │
   ├─────────────────────────────────────┤
   │                                     │
   │ We detected an existing varManager  │
   │ installation.                       │
   │                                     │
   │ Would you like to migrate your data?│
   │                                     │
   │ [Migrate] [Skip] [Help]            │
   └─────────────────────────────────────┘

4. User clicks "Migrate"
5. System scans for legacy database
6. Shows migration options:
   
   ┌─────────────────────────────────────┐
   │ Migration Options                   │
   ├─────────────────────────────────────┤
   │ Legacy Database:                    │
   │ C:\varManager\varManager.mdb        │
   │                                     │
   │ [Browse...]                         │
   │                                     │
   │ Options:                            │
   │ ☑ Migrate VAR packages              │
   │ ☑ Migrate installations             │
   │ ☑ Migrate dependencies              │
   │ ☐ Create backup                     │
   │                                     │
   │ [Start Migration] [Cancel]          │
   └─────────────────────────────────────┘

7. Migration progress shown
8. Migration completes
9. User can start using new system
```

### Programmatic Workflow

```csharp
public class MigrationService
{
    public async Task<MigrationResult> MigrateFromLegacyAsync(
        string accessDbPath,
        MigrationOptions options,
        IProgress<MigrationProgress> progress,
        CancellationToken ct)
    {
        // 1. Validate access database
        progress.Report(new("Validating legacy database...", 0));
        var validation = await ValidateLegacyDatabaseAsync(accessDbPath, ct);
        if (!validation.IsValid)
            return MigrationResult.Failure(validation.Error);
        
        // 2. Create backup
        if (options.CreateBackup)
        {
            progress.Report(new("Creating backup...", 10));
            await CreateBackupAsync(accessDbPath, ct);
        }
        
        // 3. Read legacy data
        progress.Report(new("Reading legacy data...", 20));
        var snapshot = await ReadLegacyDatabaseAsync(accessDbPath, ct);
        
        // 4. Migrate repositories
        progress.Report(new("Migrating repositories...", 30));
        var repositories = await MigrateRepositoriesAsync(snapshot, ct);
        
        // 5. Migrate VAR packages
        progress.Report(new("Migrating VAR packages...", 40));
        var varPackages = await MigrateVarPackagesAsync(snapshot, repositories, ct);
        
        // 6. Migrate dependencies
        progress.Report(new("Migrating dependencies...", 60));
        await MigrateDependenciesAsync(snapshot, varPackages, ct);
        
        // 7. Migrate installations
        progress.Report(new("Migrating installations...", 80));
        await MigrateInstallationsAsync(snapshot, varPackages, ct);
        
        // 8. Resolve dependencies
        progress.Report(new("Resolving dependencies...", 90));
        await ResolveDependenciesAsync(ct);
        
        // 9. Validate
        progress.Report(new("Validating migration...", 95));
        var validationResult = await ValidateMigrationAsync(snapshot, ct);
        
        progress.Report(new("Migration complete!", 100));
        return MigrationResult.Success(validationResult);
    }
}
```

## Data Quality Issues

### Common Issues

**1. Duplicate VAR Packages**
- Same VAR in multiple locations
- **Solution**: Use file hash to detect, keep one copy

**2. Invalid VAR Names**
- Names not following Creator.Package.Version.var format
- **Solution**: Mark as invalid, allow manual review

**3. Broken Symlinks**
- Symlinks pointing to non-existent files
- **Solution**: Detect and mark as broken, allow repair

**4. Missing Metadata**
- VAR files without meta.json
- **Solution**: Extract basic info from filename, flag for review

**5. Unresolved Dependencies**
- Dependencies not found in database
- **Solution**: Mark as unresolved, allow manual resolution

## Migration Testing

### Test Scenarios

**1. Small Database**
- < 100 VAR packages
- Single repository
- Few dependencies
- **Expected**: Migration completes in < 1 minute

**2. Medium Database**
- 1,000 VAR packages
- Single repository
- Multiple dependencies
- **Expected**: Migration completes in < 5 minutes

**3. Large Database**
- 10,000+ VAR packages
- Multiple repositories (simulated)
- Complex dependencies
- **Expected**: Migration completes in < 30 minutes

**4. Edge Cases**
- Empty database
- Corrupted Access database
- Missing files
- Invalid paths
- Circular dependencies

## Post-Migration Tasks

### Automatic Tasks

1. ✅ Resolve dependencies
2. ✅ Verify symlinks
3. ✅ Rebuild database indexes
4. ✅ Update statistics

### Manual Tasks (User)

1. ✅ Review migrated repositories
2. ✅ Verify VAR packages visible
3. ✅ Check installation status
4. ✅ Test install/uninstall operations
5. ✅ Review unresolved dependencies

## Migration Checklist

### Pre-Migration
- [ ] Backup legacy database
- [ ] Document current state
- [ ] Verify PostgreSQL installed
- [ ] Test migration tool on sample data

### During Migration
- [ ] Monitor progress
- [ ] Check for errors
- [ ] Verify data counts
- [ ] Test critical paths

### Post-Migration
- [ ] Verify all data migrated
- [ ] Test search/filter
- [ ] Test install/uninstall
- [ ] Verify dependencies
- [ ] Check symlinks valid
- [ ] Performance testing

## Rollback Procedure

### If Migration Fails

1. **Stop migration immediately**
2. **Delete PostgreSQL database** (if created)
3. **Restore from backup** (if needed)
4. **Analyze error logs**
5. **Fix issues**
6. **Retry migration**

### If Post-Migration Issues

1. **Both systems available** (Access backup + PostgreSQL)
2. **Document issues**
3. **Use legacy system** for critical operations
4. **Fix issues** in new system
5. **Re-run migration** or manual fix

## Migration Tool Features

### Command Line Tool

```bash
# Interactive migration
VirtaMatePackageManager.Migrate.exe

# Automated migration
VirtaMatePackageManager.Migrate.exe --source "path/to/varManager.mdb" --target "connection-string" --auto

# Validation only
VirtaMatePackageManager.Migrate.exe --source "path/to/varManager.mdb" --validate-only

# Export report
VirtaMatePackageManager.Migrate.exe --source "path/to/varManager.mdb" --export-report "report.json"
```

### GUI Migration Wizard

- Step-by-step wizard
- Progress indication
- Error reporting
- Validation results
- Rollback option

## Timeline Estimate

### Development Phase
- Migration tool development: 1-2 weeks
- Testing: 1 week
- Documentation: 3 days

### Migration Phase (Per User)
- Small database: 5-10 minutes
- Medium database: 10-30 minutes
- Large database: 30-60 minutes

## Success Criteria

✅ All VAR packages migrated  
✅ All installations tracked  
✅ Dependencies resolved (or marked)  
✅ No data loss  
✅ Performance improved  
✅ User can continue workflow  

## Next Steps

Continue to:
- [Development Guide](./07-Development-Guide.md) - Implementation guide
- [Testing Strategy](./08-Testing-Strategy.md) - Testing approach

