# Feature Specifications

## Overview

This document defines the detailed feature specifications for VirtaMatePackageManager, including functional requirements, user stories, and acceptance criteria.

## Feature Categories

1. **Repository Management**
2. **VAR Package Management**
3. **Installation Management**
4. **Dependency Management**
5. **Search & Discovery**
6. **UI/UX Features**
7. **Performance Features**

---

## 1. Repository Management

### 1.1 Multiple Repository Support

**Description**: Users can configure multiple repository locations across different drives and network locations.

**User Story**:
> As a user, I want to store VAR files in multiple locations (different drives, network shares) so that I can organize my collection across available storage.

**Requirements**:
- Support unlimited repositories
- Each repository has:
  - Unique path (local or network)
  - Display name
  - Priority (search order)
  - Enabled/disabled state
- Repositories can be:
  - Added dynamically
  - Edited (path, name, priority)
  - Disabled (without deleting)
  - Deleted (with confirmation)

**Acceptance Criteria**:
- [ ] User can add new repository via UI
- [ ] User can edit repository properties
- [ ] User can enable/disable repositories
- [ ] User can delete repositories (with confirmation)
- [ ] Path validation (exists, accessible, writable)
- [ ] Priority-based search order works correctly
- [ ] Disabled repositories are skipped during scans

**UI Mockups**:
```
Repository Management Dialog:
┌─────────────────────────────────────────┐
│ Repositories                             │
├─────────────────────────────────────────┤
│ [Add] [Edit] [Delete] [Enable/Disable]  │
├─────────────────────────────────────────┤
│ Name          │ Path          │ Priority│
│ Main SSD      │ D:\Vars\Main  │   10    │
│ Network Share │ \\NAS\Vars    │    5    │
│ External      │ E:\Vars       │    1    │
└─────────────────────────────────────────┘
```

### 1.2 Repository Scanning

**Description**: System scans repositories to discover and index VAR files.

**Requirements**:
- Automatic scanning on repository add/edit
- Manual scan trigger
- Background scanning (non-blocking)
- Progress indication with cancellation
- Incremental scanning (only new/changed files)
- Parallel processing for performance
- Error handling for inaccessible files

**Acceptance Criteria**:
- [ ] Scan runs in background without blocking UI
- [ ] Progress bar shows current file and percentage
- [ ] User can cancel scanning operation
- [ ] Only scans enabled repositories
- [ ] Skips already-indexed unchanged files (by hash/timestamp)
- [ ] Handles errors gracefully (logs, continues)
- [ ] Updates database incrementally (not full reload)

**Performance Targets**:
- Scan 1000 VAR files in < 2 minutes
- Process multiple files in parallel
- UI remains responsive during scan

### 1.3 Repository Validation

**Description**: Validate repository paths and accessibility.

**Requirements**:
- Check path exists
- Check read/write permissions
- Check available disk space
- Validate path format (Windows/UNC)
- Network path availability check

**Acceptance Criteria**:
- [ ] Validates path before adding repository
- [ ] Shows clear error messages for invalid paths
- [ ] Checks permissions and warns if insufficient
- [ ] Validates network paths are accessible

---

## 2. VAR Package Management

### 2.1 VAR File Discovery

**Description**: Automatically discover VAR files in repositories.

**Requirements**:
- Recursive directory scanning
- File format validation (.var extension)
- Naming convention validation (Creator.Package.Version.var)
- Metadata extraction from meta.json
- Preview image extraction

**Acceptance Criteria**:
- [ ] Finds all .var files in repository
- [ ] Validates VAR naming convention
- [ ] Extracts metadata from meta.json
- [ ] Extracts preview images
- [ ] Handles malformed VAR files gracefully

### 2.2 VAR Metadata Management

**Description**: Store and manage VAR package metadata.

**Metadata Fields**:
- Basic: Creator, Package, Version
- Descriptive: Description, Credits, Instructions
- Licensing: License Type
- Dependencies: Dependency list
- Technical: File size, hash, timestamps
- Preview: Preview image path

**Requirements**:
- Parse meta.json from VAR files
- Store in database with proper relationships
- Update metadata when VAR file changes
- Display metadata in UI

**Acceptance Criteria**:
- [ ] All metadata fields extracted and stored
- [ ] Dependencies parsed and linked
- [ ] Metadata updates when VAR file modified
- [ ] Preview images extracted and stored

### 2.3 VAR File Operations

**Description**: Operations on VAR files (view, delete, move).

**Requirements**:
- View VAR details
- Open VAR location in Explorer
- Delete VAR from repository (with confirmation)
- Move VAR between repositories
- Validate VAR integrity

**Acceptance Criteria**:
- [ ] View full VAR details dialog
- [ ] Open file location works correctly
- [ ] Delete requires confirmation
- [ ] Move operation updates database correctly
- [ ] Integrity check validates VAR file

---

## 3. Installation Management

### 3.1 Multiple Installation Targets

**Description**: Support multiple target directories for VAR installations.

**User Story**:
> As a user, I want to install VARs to different directories so that I can organize installations for different VaM versions or profiles.

**Requirements**:
- Multiple target directories
- Target properties:
  - Path
  - Display name
  - Profile name (for grouping)
  - Active/inactive state
- Only one active target at a time
- Target validation (exists, writable)

**Acceptance Criteria**:
- [ ] User can add multiple installation targets
- [ ] User can edit target properties
- [ ] Only one target can be active
- [ ] User can switch active target
- [ ] Path validation works correctly

**UI Design**:
```
Installation Targets:
┌──────────────────────────────────────┐
│ Targets          [Add] [Edit] [Delete│
├──────────────────────────────────────┤
│ Name      │ Path              │ Active│
│ VaM 1.20  │ C:\VaM\Addon...   │  [✓]  │
│ VaM 2.0   │ D:\VaM2\Addon...  │       │
│ Profile A │ C:\VaM\ProfileA   │       │
└──────────────────────────────────────┘
```

### 3.2 Install VAR Package

**Description**: Install VAR package to target directory via symbolic link.

**User Story**:
> As a user, I want to install VAR packages with one click so that I can quickly add content to VaM.

**Requirements**:
- Install selected VAR to active target
- Create symbolic link from repository to target
- Check dependencies before installation
- Option to install dependencies automatically
- Support batch installation (multiple VARs)
- Progress indication
- Cancellation support

**Workflow**:
1. User selects VAR(s) to install
2. System checks dependencies
3. If dependencies missing, prompt user
4. Create symbolic links
5. Update installation tracking in database
6. Update UI

**Acceptance Criteria**:
- [ ] Install creates symbolic link correctly
- [ ] Cross-drive linking works (symlink)
- [ ] Dependency checking works
- [ ] Batch installation works
- [ ] Progress shown during installation
- [ ] Can cancel installation
- [ ] Database updated correctly
- [ ] UI reflects installation status

**Error Handling**:
- Handle permission errors
- Handle missing source file
- Handle target directory issues
- Rollback on failure

### 3.3 Uninstall VAR Package

**Description**: Remove VAR package installation by deleting symbolic link.

**Requirements**:
- Uninstall from selected target
- Delete symbolic link
- Update database
- Check for dependent installations
- Option for batch uninstall

**Acceptance Criteria**:
- [ ] Uninstall deletes symlink correctly
- [ ] Database updated
- [ ] Warns if other VARs depend on this one
- [ ] Batch uninstall works

### 3.4 Installation Status Tracking

**Description**: Track which VARs are installed where.

**Requirements**:
- Show installation status in VAR list
- Filter by installation status
- Show installation details (where, when)
- Verify symlink integrity
- Repair broken symlinks

**Acceptance Criteria**:
- [ ] Installation status displayed correctly
- [ ] Filtering works
- [ ] Details shown in VAR details dialog
- [ ] Integrity check works
- [ ] Repair function works

---

## 4. Dependency Management

### 4.1 Dependency Resolution

**Description**: Resolve and track VAR package dependencies.

**Requirements**:
- Parse dependencies from meta.json
- Resolve dependency names to VAR packages
- Support version constraints (latest, specific version)
- Track resolved/unresolved dependencies
- Automatic resolution on scan

**Dependency Format**:
```json
{
  "dependencies": {
    "Creator.Package.latest": {
      "licenseType": "CC BY"
    },
    "Creator2.Package2.1": {
      "licenseType": "FC"
    }
  }
}
```

**Acceptance Criteria**:
- [ ] Dependencies parsed correctly
- [ ] Resolves to existing VAR packages
- [ ] Handles "latest" version constraint
- [ ] Tracks unresolved dependencies
- [ ] Shows dependency graph in UI

### 4.2 Missing Dependency Detection

**Description**: Identify and report missing dependencies.

**Requirements**:
- Check dependencies before installation
- List missing dependencies
- Show which VARs require missing dependencies
- Option to download from Hub (future)

**Acceptance Criteria**:
- [ ] Missing dependencies detected
- [ ] Clear list shown to user
- [ ] Shows dependent VAR packages
- [ ] Can search for missing VARs in repositories

### 4.3 Dependency Graph

**Description**: Visualize dependency relationships.

**Requirements**:
- Show dependency tree for VAR
- Show reverse dependencies (what depends on this VAR)
- Navigate dependency graph
- Highlight missing dependencies

**Acceptance Criteria**:
- [ ] Dependency tree displayed
- [ ] Reverse dependencies shown
- [ ] Navigation works
- [ ] Missing dependencies highlighted

---

## 5. Search & Discovery

### 5.1 Advanced Search

**Description**: Search VAR packages with multiple criteria.

**Search Criteria**:
- Creator name
- Package name
- Version
- License type
- Installed status
- Repository
- Keywords (description)
- File size range
- Date range

**Requirements**:
- Multiple criteria combination
- Real-time search results
- Save search queries
- Sort results (multiple columns)
- Filter results

**Acceptance Criteria**:
- [ ] All search criteria work
- [ ] Combine multiple criteria
- [ ] Real-time results (as user types)
- [ ] Sorting works
- [ ] Filtering works
- [ ] Performance: < 200ms for 10,000 VARs

### 5.2 Filtering

**Description**: Filter VAR list by various criteria.

**Filters**:
- Creator
- Repository
- Installation status
- License type
- Date added
- Has dependencies
- Has preview

**Requirements**:
- Multiple filters simultaneously
- Quick filter buttons
- Filter presets
- Clear all filters

**Acceptance Criteria**:
- [ ] All filters work correctly
- [ ] Multiple filters combine (AND)
- [ ] Quick filters work
- [ ] Presets work
- [ ] Clear all works

### 5.3 Sorting

**Description**: Sort VAR list by various columns.

**Sort Options**:
- Name (A-Z, Z-A)
- Creator
- Date added
- File size
- Version
- Installation status

**Requirements**:
- Sort by single column
- Sort by multiple columns (primary, secondary)
- Ascending/descending
- Remember sort preference

**Acceptance Criteria**:
- [ ] All sort options work
- [ ] Multi-column sorting works
- [ ] Ascending/descending toggle
- [ ] Preference saved

---

## 6. UI/UX Features

### 6.1 Responsive UI

**Description**: UI remains responsive during all operations.

**Requirements**:
- No UI freezing
- Background processing
- Progress indicators
- Cancellation support
- Status messages

**Acceptance Criteria**:
- [ ] UI never freezes > 100ms
- [ ] Progress shown for long operations
- [ ] Can cancel operations
- [ ] Clear status messages

### 6.2 Virtual Scrolling

**Description**: Efficient display of large VAR lists.

**Requirements**:
- Virtual scrolling for DataGrid
- Only render visible items
- Smooth scrolling
- Maintain scroll position

**Acceptance Criteria**:
- [ ] Handles 10,000+ VARs smoothly
- [ ] Scroll performance: 60 FPS
- [ ] No memory issues with large lists
- [ ] Scroll position maintained

### 6.3 Preview Images

**Description**: Display preview images for VAR packages.

**Requirements**:
- Extract preview from VAR
- Display in VAR list
- Full-size preview in details
- Lazy loading
- Placeholder for missing previews

**Acceptance Criteria**:
- [ ] Preview extracted during scan
- [ ] Thumbnails in list view
- [ ] Full preview in details
- [ ] Lazy loading works
- [ ] Placeholder shown when missing

### 6.4 Progress Reporting

**Description**: Show progress for long-running operations.

**Requirements**:
- Progress bar
- Current item/operation
- Time remaining estimate
- Percentage complete
- Detailed log

**Acceptance Criteria**:
- [ ] Progress bar accurate
- [ ] Current operation shown
- [ ] Time estimate shown
- [ ] Detailed log available

---

## 7. Performance Features

### 7.1 Fast Startup

**Description**: Application starts quickly even with large datasets.

**Target**: < 10 seconds for 10,000 VAR packages

**Requirements**:
- Lazy loading
- Incremental data loading
- Caching
- Background initialization

**Acceptance Criteria**:
- [ ] Startup < 10 seconds
- [ ] UI appears immediately
- [ ] Data loads progressively
- [ ] No blocking operations

### 7.2 Parallel Processing

**Description**: Process multiple operations in parallel.

**Requirements**:
- Parallel file scanning
- Parallel ZIP extraction
- Parallel metadata parsing
- Configurable parallelism

**Acceptance Criteria**:
- [ ] Uses all CPU cores
- [ ] Configurable parallelism
- [ ] No thread safety issues
- [ ] Performance scales with cores

### 7.3 Efficient Database Queries

**Description**: Optimized database queries for performance.

**Requirements**:
- Proper indexing
- Batch operations
- Query optimization
- Connection pooling

**Acceptance Criteria**:
- [ ] Queries use indexes
- [ ] Batch inserts/updates
- [ ] Query performance < 100ms
- [ ] Connection pooling configured

---

## 8. Additional Features

### 8.1 Import from Legacy varManager

**Description**: Import data from legacy Microsoft Access database.

**Requirements**:
- Read Access database
- Map to new schema
- Import repositories
- Import VAR packages
- Import installations

**Acceptance Criteria**:
- [ ] Reads Access database
- [ ] Maps all data correctly
- [ ] Creates repositories
- [ ] Imports VAR packages
- [ ] Imports installations

### 8.2 Export/Backup

**Description**: Export database and configuration.

**Requirements**:
- Export database dump
- Export configuration
- Scheduled backups
- Restore from backup

**Acceptance Criteria**:
- [ ] Export database works
- [ ] Export config works
- [ ] Scheduled backups work
- [ ] Restore works

### 8.3 Logging

**Description**: Comprehensive logging system.

**Requirements**:
- Structured logging
- Log levels
- File logging
- Log rotation
- Search logs

**Acceptance Criteria**:
- [ ] All operations logged
- [ ] Log levels configurable
- [ ] File logging works
- [ ] Log rotation works
- [ ] Can search logs

---

## Feature Priority

### Phase 1 (MVP)
1. Single repository management
2. VAR scanning and indexing
3. Basic installation/uninstallation
4. Simple search
5. Basic UI

### Phase 2
1. Multiple repositories
2. Multiple installation targets
3. Dependency management
4. Advanced search/filtering
5. Preview images

### Phase 3
1. Performance optimizations
2. Advanced features
3. Import/export
4. Advanced UI features

---

## Next Steps

Continue to:
- [API Specifications](./04-API-Specifications.md) - Service interfaces
- [UI Design](./05-UI-Design.md) - User interface design

