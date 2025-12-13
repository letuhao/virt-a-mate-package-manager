# Database Schema

## Overview

VirtaMatePackageManager uses **PostgreSQL 14+** as its database backend, replacing the legacy Microsoft Access database. The schema is designed to support multiple repositories, multiple installation targets, and efficient querying of large datasets.

## Database Design Principles

1. **Normalization**: Third normal form (3NF) to avoid data redundancy
2. **Indexing**: Strategic indexes on frequently queried columns
3. **Constraints**: Foreign keys, unique constraints, check constraints
4. **Performance**: Optimized for queries with 10,000+ VAR files
5. **Extensibility**: Schema designed for future enhancements

## Core Tables

### 1. repositories

Stores information about VAR file repository locations.

```sql
CREATE TABLE repositories (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    path TEXT NOT NULL UNIQUE,
    description TEXT,
    priority INTEGER NOT NULL DEFAULT 0,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    
    CONSTRAINT chk_priority CHECK (priority >= 0),
    CONSTRAINT chk_path_format CHECK (path ~ '^[A-Za-z]:\\\\.+|^\\\\\\\\.+|^/')
);

CREATE INDEX idx_repositories_enabled ON repositories(enabled) WHERE enabled = TRUE;
CREATE INDEX idx_repositories_priority ON repositories(priority DESC, enabled);
```

**Columns**:
- `id`: Primary key (auto-increment)
- `name`: Human-readable name (e.g., "Main SSD Repository")
- `path`: Full path to repository directory (e.g., "D:\Vars\Repository1")
- `description`: Optional description
- `priority`: Search priority (higher = searched first, default: 0)
- `enabled`: Whether repository is active (can be disabled without deletion)
- `created_at`: Timestamp when repository was added
- `updated_at`: Timestamp of last modification

**Indexes**:
- Index on `enabled` for filtering active repositories
- Composite index on `priority, enabled` for ordered queries

### 2. installation_targets

Stores installation target directories where symbolic links are created.

```sql
CREATE TABLE installation_targets (
    id SERIAL PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    path TEXT NOT NULL UNIQUE,
    profile_name VARCHAR(100),
    description TEXT,
    is_active BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    
    CONSTRAINT chk_path_format CHECK (path ~ '^[A-Za-z]:\\\\.+|^\\\\\\\\.+|^/'),
    CONSTRAINT uk_active_target UNIQUE (is_active) WHERE is_active = TRUE
);

CREATE INDEX idx_installation_targets_active ON installation_targets(is_active);
CREATE INDEX idx_installation_targets_profile ON installation_targets(profile_name);
```

**Columns**:
- `id`: Primary key
- `name`: Human-readable name (e.g., "VaM Default")
- `path`: Full path to target directory (e.g., "C:\VaM\AddonPackages")
- `profile_name`: Optional profile name for switching contexts
- `description`: Optional description
- `is_active`: Only one target can be active at a time
- `created_at`, `updated_at`: Timestamps

**Constraints**:
- Only one target can be active (unique constraint with WHERE clause)
- Path format validation

### 3. var_packages

Main table storing VAR file metadata. **VAR name is unique globally** - the game only accepts unique VAR filenames, so we enforce this at database level.

```sql
CREATE TABLE var_packages (
    id SERIAL PRIMARY KEY,
    repository_id INTEGER NOT NULL REFERENCES repositories(id) ON DELETE CASCADE,
    var_name VARCHAR(500) NOT NULL UNIQUE,
    creator_name VARCHAR(255) NOT NULL,
    package_name VARCHAR(255) NOT NULL,
    version VARCHAR(50) NOT NULL,
    file_path TEXT NOT NULL,
    file_size BIGINT NOT NULL,
    file_hash VARCHAR(64), -- SHA-256 hash
    relative_path TEXT, -- Path relative to repository root
    
    -- Metadata from meta.json
    license_type VARCHAR(50),
    description TEXT,
    credits TEXT,
    instructions TEXT,
    promotional_link TEXT,
    program_version VARCHAR(50),
    
    -- Preview image
    preview_image_path TEXT,
    
    -- File timestamps
    file_created_at TIMESTAMP WITH TIME ZONE,
    file_modified_at TIMESTAMP WITH TIME ZONE,
    
    -- Database timestamps
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    last_scanned_at TIMESTAMP WITH TIME ZONE,
    
    CONSTRAINT uk_var_package_repo_path UNIQUE (repository_id, file_path),
    CONSTRAINT uk_var_name UNIQUE (var_name), -- VAR name is globally unique (game requirement)
    CONSTRAINT chk_file_size CHECK (file_size >= 0),
    CONSTRAINT chk_var_name_format CHECK (var_name ~ '^[^.]+\\.[^.]+\\.[^.]+\\.var$')
);

CREATE INDEX idx_var_packages_var_name ON var_packages(var_name);
CREATE INDEX idx_var_packages_creator ON var_packages(creator_name);
CREATE INDEX idx_var_packages_package ON var_packages(package_name);
CREATE INDEX idx_var_packages_version ON var_packages(version);
CREATE INDEX idx_var_packages_repository ON var_packages(repository_id);
CREATE INDEX idx_var_packages_composite ON var_packages(creator_name, package_name, version);
CREATE INDEX idx_var_packages_updated ON var_packages(updated_at DESC);
```

**Columns**:
- `id`: Primary key
- `repository_id`: Foreign key to repositories table
- `var_name`: Full VAR name (e.g., "Creator.Package.1.var") - **UNIQUE globally**
- `creator_name`, `package_name`, `version`: Parsed components
- `file_path`: Full absolute path to VAR file
- `file_size`: File size in bytes
- `file_hash`: SHA-256 hash for duplicate detection
- `relative_path`: Path relative to repository root
- Metadata fields from `meta.json`
- `preview_image_path`: Path to extracted preview image
- File timestamps for change detection
- Database timestamps for tracking

**Indexes**:
- Multiple indexes for fast searching by various criteria
- Composite index for common query patterns

### 4. dependencies

Stores dependency relationships between VAR packages.

```sql
CREATE TABLE dependencies (
    id SERIAL PRIMARY KEY,
    var_package_id INTEGER NOT NULL REFERENCES var_packages(id) ON DELETE CASCADE,
    dependency_name VARCHAR(500) NOT NULL, -- e.g., "Creator.Package.latest"
    resolved_var_package_id INTEGER REFERENCES var_packages(id) ON DELETE SET NULL,
    version_constraint VARCHAR(50), -- "latest", "1", ">=1", etc.
    is_optional BOOLEAN NOT NULL DEFAULT FALSE,
    is_resolved BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    
    CONSTRAINT uk_var_dependency UNIQUE (var_package_id, dependency_name)
);

CREATE INDEX idx_dependencies_var_package ON dependencies(var_package_id);
CREATE INDEX idx_dependencies_resolved ON dependencies(resolved_var_package_id);
CREATE INDEX idx_dependencies_unresolved ON dependencies(is_resolved) WHERE is_resolved = FALSE;
CREATE INDEX idx_dependencies_name ON dependencies(dependency_name);
```

**Columns**:
- `id`: Primary key
- `var_package_id`: VAR that has the dependency
- `dependency_name`: Required VAR name (from meta.json)
- `resolved_var_package_id`: Resolved VAR package (if found)
- `version_constraint`: Version requirement
- `is_optional`: Whether dependency is optional
- `is_resolved`: Whether dependency was found

### 5. installations

Tracks installed VAR packages in target directories.

```sql
CREATE TABLE installations (
    id SERIAL PRIMARY KEY,
    var_package_id INTEGER NOT NULL REFERENCES var_packages(id) ON DELETE CASCADE,
    installation_target_id INTEGER NOT NULL REFERENCES installation_targets(id) ON DELETE CASCADE,
    symlink_path TEXT NOT NULL,
    is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    installed_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    installed_by VARCHAR(255),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    
    CONSTRAINT uk_installation_target_var UNIQUE (installation_target_id, var_package_id),
    CONSTRAINT chk_symlink_path_format CHECK (symlink_path ~ '^[A-Za-z]:\\\\.+|^\\\\\\\\.+|^/')
);

CREATE INDEX idx_installations_var_package ON installations(var_package_id);
CREATE INDEX idx_installations_target ON installations(installation_target_id);
CREATE INDEX idx_installations_enabled ON installations(is_enabled) WHERE is_enabled = TRUE;
CREATE INDEX idx_installations_symlink_path ON installations(symlink_path);
```

**Columns**:
- `id`: Primary key
- `var_package_id`: Installed VAR package
- `installation_target_id`: Target directory
- `symlink_path`: Full path to symbolic link
- `is_enabled`: Can be disabled without uninstalling
- `installed_at`: Installation timestamp
- `installed_by`: User/system that installed
- `updated_at`: Last modification timestamp

### 6. content_items

Stores file listings from VAR packages (optional, for advanced features).

```sql
CREATE TABLE content_items (
    id SERIAL PRIMARY KEY,
    var_package_id INTEGER NOT NULL REFERENCES var_packages(id) ON DELETE CASCADE,
    relative_path TEXT NOT NULL,
    content_type VARCHAR(50), -- "scene", "appearance", "clothing", "script", etc.
    file_size BIGINT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    
    CONSTRAINT uk_var_content_path UNIQUE (var_package_id, relative_path)
);

CREATE INDEX idx_content_items_var_package ON content_items(var_package_id);
CREATE INDEX idx_content_items_type ON content_items(content_type);
```

**Columns**:
- `id`: Primary key
- `var_package_id`: Parent VAR package
- `relative_path`: Path within VAR archive
- `content_type`: Type of content
- `file_size`: Size of item

### 7. scan_history

Tracks repository scan operations for monitoring and debugging.

```sql
CREATE TABLE scan_history (
    id SERIAL PRIMARY KEY,
    repository_id INTEGER NOT NULL REFERENCES repositories(id) ON DELETE CASCADE,
    started_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMP WITH TIME ZONE,
    status VARCHAR(50) NOT NULL, -- "running", "completed", "failed", "cancelled"
    files_scanned INTEGER DEFAULT 0,
    files_added INTEGER DEFAULT 0,
    files_updated INTEGER DEFAULT 0,
    files_deleted INTEGER DEFAULT 0,
    errors_count INTEGER DEFAULT 0,
    error_message TEXT,
    
    CONSTRAINT chk_status CHECK (status IN ('running', 'completed', 'failed', 'cancelled'))
);

CREATE INDEX idx_scan_history_repository ON scan_history(repository_id);
CREATE INDEX idx_scan_history_status ON scan_history(status);
CREATE INDEX idx_scan_history_started ON scan_history(started_at DESC);
```

**Columns**:
- Tracking information for scan operations
- Statistics: files scanned, added, updated, deleted
- Error tracking

## Views

### 1. var_packages_view

Convenience view joining var_packages with repository information.

```sql
CREATE VIEW var_packages_view AS
SELECT 
    vp.id,
    vp.var_name,
    vp.creator_name,
    vp.package_name,
    vp.version,
    vp.file_path,
    vp.file_size,
    vp.license_type,
    vp.description,
    vp.preview_image_path,
    r.name AS repository_name,
    r.path AS repository_path,
    r.enabled AS repository_enabled,
    vp.created_at,
    vp.updated_at
FROM var_packages vp
INNER JOIN repositories r ON vp.repository_id = r.id;
```

### 2. installed_packages_view

View showing installed packages with target information.

```sql
CREATE VIEW installed_packages_view AS
SELECT 
    i.id AS installation_id,
    vp.id AS var_package_id,
    vp.var_name,
    vp.creator_name,
    vp.package_name,
    it.name AS target_name,
    it.path AS target_path,
    i.symlink_path,
    i.is_enabled,
    i.installed_at
FROM installations i
INNER JOIN var_packages vp ON i.var_package_id = vp.id
INNER JOIN installation_targets it ON i.installation_target_id = it.id;
```

## Stored Procedures / Functions

### 1. Resolve Dependencies

```sql
CREATE OR REPLACE FUNCTION resolve_dependencies()
RETURNS TABLE(updated_count INTEGER) AS $$
DECLARE
    v_count INTEGER;
BEGIN
    -- Update unresolved dependencies
    UPDATE dependencies d
    SET 
        resolved_var_package_id = vp.id,
        is_resolved = TRUE,
        updated_at = NOW()
    FROM var_packages vp
    WHERE 
        d.is_resolved = FALSE
        AND d.dependency_name LIKE vp.creator_name || '.' || vp.package_name || '%'
        AND (
            d.version_constraint = 'latest' 
            OR d.dependency_name = vp.var_name
            OR (d.version_constraint IS NOT NULL AND vp.version = d.version_constraint)
        );
    
    GET DIAGNOSTICS v_count = ROW_COUNT;
    RETURN QUERY SELECT v_count;
END;
$$ LANGUAGE plpgsql;
```

### 2. Cleanup Orphaned Installations

```sql
CREATE OR REPLACE FUNCTION cleanup_orphaned_installations()
RETURNS INTEGER AS $$
DECLARE
    v_count INTEGER;
BEGIN
    -- Delete installations where symlink doesn't exist
    DELETE FROM installations
    WHERE NOT EXISTS (
        SELECT 1 FROM pg_stat_file(symlink_path)
    );
    
    GET DIAGNOSTICS v_count = ROW_COUNT;
    RETURN v_count;
END;
$$ LANGUAGE plpgsql;
```

## Triggers

### Update Timestamps

```sql
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER update_repositories_updated_at
    BEFORE UPDATE ON repositories
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_var_packages_updated_at
    BEFORE UPDATE ON var_packages
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_installation_targets_updated_at
    BEFORE UPDATE ON installation_targets
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();
```

## Database Migrations

Using Entity Framework Core migrations:

```bash
# Initial migration
dotnet ef migrations add InitialCreate --project VirtaMatePackageManager.Infrastructure

# Apply migrations
dotnet ef database update --project VirtaMatePackageManager.Infrastructure
```

## Indexing Strategy

### Primary Indexes
- All primary keys (automatic)
- All foreign keys
- Unique constraint columns

### Performance Indexes
- Composite indexes for common query patterns
- Partial indexes for filtered queries (WHERE enabled = TRUE)
- Covering indexes for frequent SELECT queries

### Maintenance
- Regular `VACUUM ANALYZE` for query optimization
- Monitor index usage and adjust as needed
- Consider partitioning for very large tables (future)

## Data Integrity

### Foreign Key Constraints
- All relationships have proper foreign keys
- CASCADE deletes where appropriate
- SET NULL for optional relationships

### Check Constraints
- Path format validation
- Status value validation
- Numeric range validation

### Unique Constraints
- Prevent duplicate repositories
- Prevent duplicate installations
- Prevent duplicate dependencies

## Backup Strategy

### Recommended Approach
1. **Daily full backups** using `pg_dump`
2. **WAL archiving** for point-in-time recovery
3. **Test restore procedures** regularly

### Backup Script Example
```bash
pg_dump -h localhost -U varm_user -d varm_db -F c -f backup_$(date +%Y%m%d).dump
```

## Performance Considerations

### Query Optimization
- Use EXPLAIN ANALYZE for slow queries
- Optimize N+1 query problems
- Use batch operations for bulk inserts

### Connection Pooling
- Configure appropriate pool size
- Use connection string with pooling enabled

### Table Statistics
- Regular `ANALYZE` to update statistics
- Auto-vacuum configured for maintenance

## Next Steps

Continue to:
- [Feature Specifications](./03-Feature-Specifications.md) - Feature requirements
- [API Specifications](./04-API-Specifications.md) - Service interfaces

