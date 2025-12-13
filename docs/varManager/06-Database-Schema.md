# Phase 6: Database Schema

## Overview

The varManager application uses a Microsoft Access database (`.mdb` format) to store VAR package metadata, installation status, and dependency relationships. The database is accessed through OLEDB connection and managed via typed DataSet/DataAdapter pattern.

## Database File

- **File Name**: `varManager.mdb`
- **Location**: Application directory
- **Provider**: Microsoft Access Database Engine (OLEDB)
- **Connection**: Managed through application settings

## Schema Definition

The database schema is defined in `varManagerDataSet.xsd` and generates strongly-typed DataSet classes.

## Tables

### 1. vars

Stores VAR file metadata and information.

#### Columns

| Column Name | Data Type | Constraints | Description |
|-------------|-----------|-------------|-------------|
| ID | AutoNumber (Int32) | Primary Key, Auto Increment | Unique identifier |
| varName | Text (String) | Not Null, Unique | VAR file name (Creator.Package.Version) |
| creatorName | Text (String) | Not Null | Creator/author name |
| packageName | Text (String) | Not Null | Package name |
| version | Number (Int32) | Not Null | Version number |
| licenseType | Text (String) | | License type (CC BY, FC, etc.) |
| description | Memo (String) | | Package description |
| credits | Memo (String) | | Credits information |
| instructions | Memo (String) | | Usage instructions |
| promotionalLink | Text (String) | | Promotional URL |
| programVersion | Text (String) | | VaM version compatibility |
| contentList | Memo (String) | | JSON array of content files |
| dependencies | Memo (String) | | JSON object of dependencies |
| customOptions | Memo (String) | | Custom options JSON |
| previewImagePath | Text (String) | | Path to preview image |
| filePath | Text (String) | | Full path to VAR file |
| fileSize | Number (Int64) | | File size in bytes |
| lastModified | DateTime | | File last modified date |
| dateAdded | DateTime | | Date added to database |

#### Indexes

- Primary Key on `ID`
- Unique index on `varName`
- Index on `creatorName`
- Index on `packageName`
- Index on `version`

#### Relationships

- One-to-many with `dependencies` table (via `varName`)
- One-to-one with `installStatus` table (via `varName`)

### 2. dependencies

Stores dependency relationships between VAR packages.

#### Columns

| Column Name | Data Type | Constraints | Description |
|-------------|-----------|-------------|-------------|
| ID | AutoNumber (Int32) | Primary Key, Auto Increment | Unique identifier |
| varName | Text (String) | Not Null, Foreign Key | VAR that has dependency |
| dependency | Text (String) | Not Null | Required VAR name |

#### Indexes

- Primary Key on `ID`
- Index on `varName`
- Index on `dependency`
- Composite index on (`varName`, `dependency`)

#### Relationships

- Foreign Key to `vars.varName`
- Represents: VAR → Dependency relationship

#### Usage

Tracks which VAR packages depend on other VAR packages. Used for:
- Dependency resolution during installation
- Missing dependency detection
- Dependency graph analysis

### 3. installStatus

Tracks installation status of VAR packages.

#### Columns

| Column Name | Data Type | Constraints | Description |
|-------------|-----------|-------------|-------------|
| varName | Text (String) | Primary Key, Foreign Key | VAR file name |
| Installed | Yes/No (Boolean) | Not Null, Default: False | Installation status |
| Disabled | Yes/No (Boolean) | Not Null, Default: False | Disabled status |

#### Indexes

- Primary Key on `varName`

#### Relationships

- Foreign Key to `vars.varName`
- One-to-one relationship

#### Status Combinations

| Installed | Disabled | Meaning |
|-----------|----------|---------|
| False | False | Not installed |
| True | False | Installed and active |
| True | True | Installed but disabled |
| False | True | Invalid state (shouldn't occur) |

### 4. varsView (View)

A database view providing combined information from multiple tables.

**Note**: Views are defined in XSD but may not be directly queryable in Access. May be implemented as a query or in-memory DataView.

**Combined Columns**:
- All columns from `vars` table
- `Installed` status from `installStatus`
- `Disabled` status from `installStatus`
- Dependency count (calculated)

## Data Access Layer

### DataSet Structure

Generated from XSD schema:

**varManagerDataSet** class contains:

1. **Data Tables**:
   - `varsDataTable`: Strongly-typed rows for vars
   - `dependenciesDataTable`: Strongly-typed rows for dependencies
   - `installStatusDataTable`: Strongly-typed rows for installStatus

2. **Table Adapters**:
   - `varsTableAdapter`: CRUD operations for vars
   - `dependenciesTableAdapter`: CRUD operations for dependencies
   - `installStatusTableAdapter`: CRUD operations for installStatus
   - `varsViewTableAdapter`: Query operations for view

3. **Row Classes**:
   - `varsRow`: Strongly-typed row with properties
   - `dependenciesRow`: Strongly-typed row
   - `installStatusRow`: Strongly-typed row

### Connection String

Stored in application settings:
```
Provider=Microsoft.ACE.OLEDB.12.0;Data Source=|DataDirectory|\varManager.mdb
```

Or for older Access:
```
Provider=Microsoft.Jet.OLEDB.4.0;Data Source=|DataDirectory|\varManager.mdb
```

## Operations

### CRUD Operations

#### Create (Insert)

```csharp
varManagerDataSet.varsRow newRow = varManagerDataSet.vars.NewvarsRow();
newRow.varName = "Creator.Package.1";
newRow.creatorName = "Creator";
// ... set other fields
varManagerDataSet.vars.AddvarsRow(newRow);
varsTableAdapter.Update(varManagerDataSet.vars);
```

#### Read (Select)

```csharp
varsTableAdapter.Fill(varManagerDataSet.vars);
// Or with filter
varsTableAdapter.FillByCreator(varManagerDataSet.vars, "CreatorName");
```

#### Update

```csharp
varManagerDataSet.varsRow row = varManagerDataSet.vars.FindByvarName("Creator.Package.1");
row.description = "Updated description";
varsTableAdapter.Update(varManagerDataSet.vars);
```

#### Delete

```csharp
varManagerDataSet.varsRow row = varManagerDataSet.vars.FindByvarName("Creator.Package.1");
row.Delete();
varsTableAdapter.Update(varManagerDataSet.vars);
```

### Query Patterns

#### Find VAR by Name
```csharp
var row = varManagerDataSet.vars.FindByvarName(varName);
```

#### Get Dependencies for VAR
```csharp
var dependencies = varManagerDataSet.dependencies
    .Where(d => d.varName == varName)
    .Select(d => d.dependency)
    .ToList();
```

#### Get VARs by Creator
```csharp
var vars = varManagerDataSet.vars
    .Where(v => v.creatorName == creatorName)
    .ToList();
```

#### Get Latest Version
```csharp
var maxVersion = varManagerDataSet.vars
    .Where(v => v.creatorName == creator && v.packageName == package)
    .Max(v => v.version);
```

## Data Relationships

### Dependency Graph

The `dependencies` table forms a directed graph:
- Nodes: VAR packages
- Edges: Dependencies

**Example**:
```
VAR_A depends on VAR_B
VAR_B depends on VAR_C
```

Creates dependency chain: VAR_A → VAR_B → VAR_C

### Circular Dependency Detection

Application should detect and prevent circular dependencies:
```
VAR_A → VAR_B → VAR_A (circular!)
```

## Data Validation

### VAR Name Format

VAR names must follow format: `CreatorName.PackageName.Version`

Stored in database but validated in application code.

### Version Numbers

- Numeric versions stored as integers
- "latest" is special string value
- Version comparison for updates

## Backup and Recovery

### Database Backup

The `.mdb` file can be backed up by:
- Copying file directly
- Exporting data to CSV/XML
- Using Access database tools

### Recovery

- Restore from backup file
- Rebuild from VAR repository scan
- Data can be regenerated by re-scanning VAR files

## Performance Considerations

### Indexing

Critical indexes on:
- `varName` (primary lookup key)
- `creatorName` (grouping/filtering)
- Dependency columns (join operations)

### Query Optimization

- Use indexed columns in WHERE clauses
- Limit result sets with TOP/SELECT LIMIT
- Cache frequently accessed data

### Database Size

- Monitor database file size
- Consider archiving old data
- Compact database periodically

## Migration

### Schema Evolution

If schema changes are needed:
1. Create migration script
2. Backup existing database
3. Apply schema changes
4. Migrate data
5. Verify integrity

### Version Compatibility

Database structure should remain backward compatible or include migration logic.

---

Continue to:
- [Phase 7: Build Configuration](./07-Build-Configuration.md)

