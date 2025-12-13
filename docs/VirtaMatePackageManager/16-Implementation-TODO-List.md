# Implementation TODO List

## Overview

This comprehensive TODO list ensures all functions from the specifications are implemented. Based on `09-Function-Specifications.md` and `10-Function-Implementation-Checklist.md`.

**Status Legend**: ⬜ Not Started | 🟡 In Progress | ✅ Completed | ⏭️ Skipped

---

## Phase 1: Foundation & Infrastructure

### 1.1 Project Setup ✅

- [x] Create solution file
- [x] Create all projects (Core, Application, Infrastructure, Presentation)
- [x] Setup project references
- [x] Create Directory.Build.props
- [x] Install NuGet packages

### 1.2 Domain Entities ✅

- [x] Repository entity
- [x] InstallationTarget entity
- [x] VarPackage entity
- [x] Dependency entity
- [x] Installation entity
- [x] ContentItem entity with ContentType enum

### 1.3 Value Objects ✅

- [x] Result<T> pattern
- [x] ErrorCode enum

### 1.4 EF Core Infrastructure 🟡

- [x] ApplicationDbContext
- [x] RepositoryConfiguration
- [x] VarPackageConfiguration
- [ ] InstallationTargetConfiguration
- [ ] DependencyConfiguration
- [ ] InstallationConfiguration
- [ ] ContentItemConfiguration

### 1.5 Database Setup

- [ ] Create appsettings.json with connection string
- [ ] Create initial EF Core migration
- [ ] Test database creation
- [ ] Verify all tables created correctly

---

## Phase 2: Core Functions - VAR File Validation

### 2.1 VAR File Validation Functions

- [ ] `ValidateVarFileName` (Section 1.1)
  - [ ] Validate filename format (Creator.Package.Version.var)
  - [ ] Validate creator name (1-60 chars, alphanumeric + underscore)
  - [ ] Validate package name (1-80 chars, alphanumeric + underscore)
  - [ ] Validate version (numeric or "latest")
  - [ ] Return ValidationResult with parsed components
  - [ ] Unit tests

- [ ] `ValidateVarFileStructure` (Section 1.2)
  - [ ] Check file exists
  - [ ] Validate ZIP archive structure
  - [ ] Check meta.json exists
  - [ ] Validate meta.json is readable JSON
  - [ ] Check required fields in meta.json
  - [ ] Return ValidationResult with metadata
  - [ ] Unit tests

- [ ] `ValidateVarFileComplete` (Section 1.3)
  - [ ] Validate filename
  - [ ] Validate structure
  - [ ] Validate filename matches metadata
  - [ ] Check file integrity (size, hash)
  - [ ] Return CompleteValidationResult
  - [ ] Unit tests

---

## Phase 3: Core Functions - VAR File Parsing

### 3.1 VAR File Parsing Functions

- [ ] `ParseVarFileName` (Section 2.1)
  - [ ] Split filename by dots
  - [ ] Extract creator, package, version
  - [ ] Handle "latest" version
  - [ ] Return VarFileNameComponents
  - [ ] Handle invalid formats
  - [ ] Unit tests

- [ ] `ExtractVarMetadata` (Section 2.2)
  - [ ] Open VAR file as ZIP
  - [ ] Read meta.json entry
  - [ ] Parse JSON
  - [ ] Extract all metadata fields
  - [ ] Extract content list
  - [ ] Extract custom options
  - [ ] Return VarMetadata object
  - [ ] Unit tests

---

## Phase 4: Core Functions - Dependency Extraction

### 4.1 Dependency Extraction Functions

- [ ] `ExtractDependencies` (Section 3.1)
  - [ ] Read dependencies from meta.json
  - [ ] Call ExtractDependenciesRecursive
  - [ ] Return flattened list
  - [ ] Unit tests

- [ ] `ExtractDependenciesRecursive` (Section 3.1)
  - [ ] Parse dependencies object
  - [ ] Handle nested dependencies
  - [ ] Extract license type
  - [ ] Handle missing flag
  - [ ] Build full path for nested deps
  - [ ] Unit tests

- [ ] `IsValidDependencyName` (Section 3.1)
  - [ ] Validate format: Creator.Package.Version
  - [ ] Validate version is numeric or "latest"
  - [ ] Return boolean
  - [ ] Unit tests

- [ ] `FlattenDependencies` (Section 3.2)
  - [ ] Recursively flatten nested structure
  - [ ] Track depth
  - [ ] Handle circular dependencies
  - [ ] Return FlattenedDependency list
  - [ ] Unit tests

---

## Phase 5: Core Functions - Content Analysis

### 5.1 Content Analysis Functions

- [ ] `AnalyzeVarContent` (Section 4.1)
  - [ ] Open VAR file as ZIP
  - [ ] Iterate through all entries
  - [ ] Call DetermineContentType for each entry
  - [ ] Count by content type
  - [ ] Build ContentItem list
  - [ ] Return ContentAnalysisResult
  - [ ] Unit tests

- [ ] `DetermineContentType` (Section 4.1)
  - [ ] Check patterns for scenes
  - [ ] Check patterns for looks
  - [ ] Check patterns for clothing
  - [ ] Check patterns for hairstyle
  - [ ] Check patterns for scripts
  - [ ] Check patterns for assets
  - [ ] Check patterns for morphs
  - [ ] Check patterns for poses
  - [ ] Check patterns for skin
  - [ ] Return ContentType enum
  - [ ] Unit tests

- [ ] `DetermineIfPreset` (Section 4.1)
  - [ ] Check if content type is preset based on extension
  - [ ] Return boolean
  - [ ] Unit tests

---

## Phase 6: Core Functions - Preview Image Extraction

### 6.1 Preview Image Extraction Functions

- [ ] `ExtractPreviewImages` (Section 5.1)
  - [ ] Analyze VAR content
  - [ ] Filter content items that should have previews
  - [ ] Call ExtractSinglePreviewImage for each
  - [ ] Return PreviewImageInfo list
  - [ ] Unit tests

- [ ] `ShouldHavePreview` (Section 5.1)
  - [ ] Check if content type supports preview
  - [ ] Return boolean
  - [ ] Unit tests

- [ ] `FindPreviewImagePath` (Section 5.1)
  - [ ] Build preview path from content path
  - [ ] Check if exists in ZIP
  - [ ] Try alternative naming patterns
  - [ ] Return preview path or null
  - [ ] Unit tests

- [ ] `ExtractSinglePreviewImage` (Section 5.1)
  - [ ] Determine output directory structure
  - [ ] Generate unique filename
  - [ ] Extract image from ZIP
  - [ ] Save to disk
  - [ ] Return PreviewImageInfo
  - [ ] Unit tests

- [ ] `GetTypePrefix` (Section 5.1)
  - [ ] Return prefix based on content type
  - [ ] Unit tests

---

## Phase 7: Core Functions - Repository Organization

### 7.1 Repository Organization Functions

- [ ] `OrganizeVarFiles` (Section 6.1 - Simplified)
  - [ ] Create tidy/redundant/invalid directories
  - [ ] Validate VAR filename
  - [ ] Check if VAR name exists in database (UNIQUE check)
  - [ ] Handle local duplicates
  - [ ] Handle cross-repository conflicts
  - [ ] Move files to appropriate directories
  - [ ] Return OrganizationResult
  - [ ] Unit tests

- [ ] `GetVarPackageByName` (Section 6.1)
  - [ ] Query database by var_name (unique key)
  - [ ] Return VarPackage or null
  - [ ] Unit tests

- [ ] `MoveToInvalid` (Section 6.1)
  - [ ] Generate unique filename
  - [ ] Move file to invalid directory
  - [ ] Log operation
  - [ ] Unit tests

- [ ] `MoveToRedundant` (Section 6.1)
  - [ ] Generate unique filename
  - [ ] Move file to redundant directory
  - [ ] Log operation
  - [ ] Unit tests

- [ ] `GenerateUniqueFileName` (Section 6.1)
  - [ ] Check if file exists
  - [ ] Append counter if exists
  - [ ] Return unique filename
  - [ ] Unit tests

- [ ] `DetectDuplicateVarFiles` (Section 6.2 - Simplified)
  - [ ] Query database grouping by var_name
  - [ ] Find VAR names with multiple repository copies
  - [ ] Apply selection strategy
  - [ ] Build DuplicateGroup list
  - [ ] Return DuplicateDetectionResult
  - [ ] Unit tests

- [ ] `DeterminePrimaryFile` / `DeterminePrimaryVarPackage` (Section 6.2)
  - [ ] Implement HighestPriorityRepository strategy
  - [ ] Implement NewestFile strategy
  - [ ] Implement OldestFile strategy
  - [ ] Implement LargestFile strategy
  - [ ] Implement SmallestFile strategy
  - [ ] Unit tests

- [ ] `ResolveDuplicateVarFile` (Section 6.3 - Simplified)
  - [ ] Query database by VAR name
  - [ ] If multiple copies, apply selection strategy
  - [ ] Check for content mismatch (different hash)
  - [ ] Return Result<VarPackage>
  - [ ] Unit tests

- [ ] `MarkDuplicateVarFiles` (Section 6.4)
  - [ ] Update primary file flag
  - [ ] Update duplicate file flags
  - [ ] Create duplicate group records
  - [ ] Transaction handling
  - [ ] Unit tests

- [ ] `HandleDuplicateDuringInstallation` (Section 6.5)
  - [ ] Resolve VAR package by name
  - [ ] Check user preference
  - [ ] Apply selection strategy
  - [ ] Return resolved VAR package
  - [ ] Unit tests

---

## Phase 8: Core Functions - Installation

### 8.1 Installation Functions

- [ ] `InstallVarPackage` (Section 7.1)
  - [ ] Get VAR package from database
  - [ ] Get installation target
  - [ ] Check if already installed
  - [ ] Verify VAR file exists
  - [ ] Create symlink path
  - [ ] Handle existing symlink
  - [ ] Create symbolic link
  - [ ] Install dependencies if requested
  - [ ] Create/update installation record
  - [ ] Return Result<InstallationInfo>
  - [ ] Unit tests

- [ ] `InstallDependencies` (Section 7.1)
  - [ ] Get dependencies for VAR package
  - [ ] Resolve each dependency
  - [ ] Recursively install dependencies
  - [ ] Handle optional dependencies
  - [ ] Return Result
  - [ ] Unit tests

- [ ] `CreateSymbolicLink` (Section 7.2)
  - [ ] Validate target exists
  - [ ] Ensure link directory exists
  - [ ] Convert to absolute paths
  - [ ] Use Windows API CreateSymbolicLink
  - [ ] Handle errors
  - [ ] Return Result
  - [ ] Unit tests

- [ ] `UninstallVarPackage`
  - [ ] Get installation record
  - [ ] Delete symbolic link
  - [ ] Update database
  - [ ] Handle errors
  - [ ] Unit tests

- [ ] `BatchInstallVarPackages`
  - [ ] Process multiple VAR packages
  - [ ] Progress reporting
  - [ ] Error handling
  - [ ] Rollback on failure
  - [ ] Unit tests

- [ ] `VerifyInstallations`
  - [ ] Check all symlinks are valid
  - [ ] Detect broken symlinks
  - [ ] Return verification results
  - [ ] Unit tests

---

## Phase 9: Core Functions - Repository Scanning

### 9.1 Repository Scanning Functions

- [ ] `ScanRepository` (Section 8.1)
  - [ ] Get repository
  - [ ] Get existing VAR files from database
  - [ ] Scan file system for VAR files
  - [ ] Compare and detect changes
  - [ ] Add new VAR packages
  - [ ] Update modified VAR packages
  - [ ] Mark deleted VAR packages
  - [ ] Progress reporting
  - [ ] Cancellation support
  - [ ] Return ScanResult
  - [ ] Integration tests

- [ ] `ScanFileSystemForVarFiles` (Section 8.1)
  - [ ] Get all .var files recursively
  - [ ] Exclude special directories
  - [ ] Skip symlinks
  - [ ] Return ScannedVarFile list
  - [ ] Unit tests

- [ ] `IncrementalScanRepository`
  - [ ] Only scan changed files
  - [ ] Use file modification time
  - [ ] Use database last_scanned_at
  - [ ] Optimize for large repositories
  - [ ] Unit tests

---

## Phase 10: Core Functions - File Hashing

### 10.1 File Hashing Functions

- [ ] `ComputeFileHash` (Section 9.1)
  - [ ] Read file
  - [ ] Compute SHA-256 hash
  - [ ] Return hex string
  - [ ] Handle file errors
  - [ ] Unit tests

- [ ] `ComputeFileHashAsync` (Section 9.1)
  - [ ] Async file reading
  - [ ] Compute hash asynchronously
  - [ ] Cancellation support
  - [ ] Progress reporting for large files
  - [ ] Unit tests

---

## Phase 11: Repository Interfaces & Implementations

### 11.1 Core Layer - Repository Interfaces

- [ ] `IRepository<T>` base interface
  - [ ] GetByIdAsync
  - [ ] GetAllAsync
  - [ ] AddAsync
  - [ ] UpdateAsync
  - [ ] DeleteAsync
  - [ ] FindAsync

- [ ] `IVarPackageRepository`
  - [ ] GetByVarNameAsync (unique key lookup)
  - [ ] GetByRepositoryIdAsync
  - [ ] SearchAsync
  - [ ] GetByCreatorAsync
  - [ ] ExistsByVarNameAsync

- [ ] `IRepositoryRepository`
  - [ ] GetEnabledRepositoriesAsync
  - [ ] GetByPriorityAsync

- [ ] `IInstallationTargetRepository`
  - [ ] GetActiveTargetAsync
  - [ ] GetByProfileAsync

- [ ] `IDependencyRepository`
  - [ ] GetByVarPackageIdAsync
  - [ ] GetUnresolvedDependenciesAsync

- [ ] `IInstallationRepository`
  - [ ] GetByVarPackageIdAsync
  - [ ] GetByTargetIdAsync
  - [ ] GetInstallationAsync

- [ ] `IUnitOfWork`
  - [ ] BeginTransactionAsync
  - [ ] CommitAsync
  - [ ] RollbackAsync
  - [ ] SaveChangesAsync

### 11.2 Infrastructure Layer - Repository Implementations

- [ ] `BaseRepository<T>` implementation
- [ ] `VarPackageRepository` implementation
- [ ] `RepositoryRepository` implementation
- [ ] `InstallationTargetRepository` implementation
- [ ] `DependencyRepository` implementation
- [ ] `InstallationRepository` implementation
- [ ] `UnitOfWork` implementation

---

## Phase 12: Application Layer - Services

### 12.1 Service Interfaces (from API Specifications)

- [ ] `IVarPackageService` (Section 4)
  - [ ] AddVarPackageAsync
  - [ ] UpdateVarPackageAsync
  - [ ] DeleteVarPackageAsync
  - [ ] GetVarPackageByIdAsync
  - [ ] GetVarPackageByNameAsync
  - [ ] SearchVarPackagesAsync
  - [ ] GetVarPackagesPagedAsync
  - [ ] ExtractMetadataAsync
  - [ ] ExtractPreviewImageAsync

- [ ] `IRepositoryService` (Section 1)
  - [ ] AddRepositoryAsync
  - [ ] UpdateRepositoryAsync
  - [ ] DeleteRepositoryAsync
  - [ ] EnableRepositoryAsync
  - [ ] GetAllRepositoriesAsync
  - [ ] GetRepositoryByIdAsync
  - [ ] GetEnabledRepositoriesAsync
  - [ ] ScanRepositoryAsync
  - [ ] ScanAllRepositoriesAsync

- [ ] `IInstallationService` (Section 3)
  - [ ] InstallVarPackageAsync
  - [ ] UninstallVarPackageAsync
  - [ ] BatchInstallVarPackagesAsync
  - [ ] BatchUninstallVarPackagesAsync
  - [ ] EnableInstallationAsync
  - [ ] GetInstallationsByVarPackageAsync
  - [ ] GetInstallationsByTargetAsync
  - [ ] GetInstallationAsync
  - [ ] VerifyInstallationsAsync
  - [ ] RepairBrokenSymlinksAsync

- [ ] `IInstallationTargetService` (Section 4)
  - [ ] AddInstallationTargetAsync
  - [ ] UpdateInstallationTargetAsync
  - [ ] DeleteInstallationTargetAsync
  - [ ] SetActiveTargetAsync
  - [ ] GetAllTargetsAsync
  - [ ] GetTargetByIdAsync
  - [ ] GetActiveTargetAsync
  - [ ] GetTargetsByProfileAsync

- [ ] `IDependencyService` (Section 5)
  - [ ] ResolveDependenciesAsync
  - [ ] GetDependenciesAsync
  - [ ] GetReverseDependenciesAsync
  - [ ] ValidateDependenciesAsync

- [ ] `ISearchService` (Section 6)
  - [ ] SearchAsync
  - [ ] GetCreatorsAsync
  - [ ] GetPackagesByCreatorAsync
  - [ ] GetSearchStatisticsAsync

- [ ] `IFileSystemService` (Section 7)
  - [ ] CreateSymbolicLinkAsync
  - [ ] DeleteSymbolicLinkAsync
  - [ ] SymbolicLinkExistsAsync
  - [ ] ResolveSymbolicLinkAsync
  - [ ] GetFileInfoAsync
  - [ ] FileExistsAsync
  - [ ] DeleteFileAsync
  - [ ] MoveFileAsync
  - [ ] GetFilesAsync
  - [ ] DirectoryExistsAsync
  - [ ] CreateDirectoryAsync
  - [ ] ValidatePathAsync

### 12.2 Service Implementations

- [ ] `VarPackageService` implementation
- [ ] `RepositoryService` implementation
- [ ] `InstallationService` implementation
- [ ] `InstallationTargetService` implementation
- [ ] `DependencyService` implementation
- [ ] `SearchService` implementation
- [ ] `FileSystemService` implementation

---

## Phase 13: Application Layer - DTOs & Commands

### 13.1 DTOs (from API Specifications Section 4)

- [ ] All DTOs from API Specifications
  - [ ] RepositoryDto
  - [ ] AddRepositoryCommand
  - [ ] UpdateRepositoryCommand
  - [ ] VarPackageDto
  - [ ] AddVarPackageCommand
  - [ ] UpdateVarPackageCommand
  - [ ] VarSearchQuery
  - [ ] VarPagedQuery
  - [ ] PagedResult<T>
  - [ ] InstallationDto
  - [ ] InstallVarPackageCommand
  - [ ] UninstallVarPackageCommand
  - [ ] BatchInstallCommand
  - [ ] InstallationTargetDto
  - [ ] AddInstallationTargetCommand
  - [ ] DependencyDto
  - [ ] DependencyValidationResult
  - [ ] ... (all DTOs from Section 4)

---

## Phase 14: Utility Functions

### 14.1 File System Utilities

- [ ] `IsSymbolicLink` - Check if path is symlink
- [ ] `ResolveSymbolicLink` - Get target of symlink
- [ ] `DeleteSymbolicLink` - Remove symlink safely

### 14.2 VAR File Utilities

- [ ] `GetFileSizeFormatted` - Format file size as human-readable
- [ ] `ParseVersionString` - Parse version string to number

---

## Phase 15: Cleanup & Maintenance Functions

### 15.1 Cleanup Functions

- [ ] `CleanupOrphanedRecords`
  - [ ] Find VAR packages without files
  - [ ] Find dependencies without VAR packages
  - [ ] Find installations without VAR packages
  - [ ] Delete orphaned records

- [ ] `FixBrokenSymlinks`
  - [ ] Find all installations
  - [ ] Check if symlinks are valid
  - [ ] Repair or remove broken symlinks

- [ ] `CleanupPreviewImages`
  - [ ] Find orphaned preview images
  - [ ] Delete orphaned images

- [ ] `RepairRepository`
  - [ ] Comprehensive repository repair
  - [ ] Fix all issues

---

## Phase 16: Migration Tool

### 16.1 Migration Functions

- [ ] `ReadLegacyDatabase` - Read from Access database
- [ ] `MigrateRepositories` - Migrate repository data
- [ ] `MigrateVarPackages` - Migrate VAR package data
- [ ] `MigrateDependencies` - Migrate dependencies
- [ ] `MigrateInstallations` - Migrate installation records
- [ ] `ValidateMigration` - Validate migrated data
- [ ] Migration command-line tool

---

## Phase 17: Presentation Layer

### 17.1 ViewModels

- [ ] ViewModelBase class
- [ ] MainWindowViewModel
- [ ] VarPackageListViewModel
- [ ] RepositoryManagementViewModel
- [ ] InstallationTargetViewModel
- [ ] SettingsViewModel

### 17.2 Views

- [ ] MainWindow
- [ ] RepositoryManagementDialog
- [ ] InstallationTargetDialog
- [ ] VARDetailsDialog
- [ ] SettingsDialog
- [ ] ProgressDialog

### 17.3 UI Components

- [ ] Virtual scrolling DataGrid
- [ ] Preview image viewer
- [ ] Search/filter controls
- [ ] Dependency graph viewer

---

## Phase 18: Testing

### 18.1 Unit Tests

- [ ] All Core functions unit tests
- [ ] All Application services unit tests
- [ ] Repository implementations unit tests
- [ ] Coverage target: 80%+

### 18.2 Integration Tests

- [ ] Database integration tests
- [ ] Service integration tests
- [ ] File system integration tests

### 18.3 E2E Tests

- [ ] Install VAR workflow
- [ ] Uninstall VAR workflow
- [ ] Scan repository workflow
- [ ] Search workflow

---

## Summary Statistics

**Total Functions**: ~150+ functions/methods  
**Total Tests Needed**: ~300+ test cases  
**Estimated Time**: 10-12 weeks for full implementation

---

## Priority Ranking

### 🔴 Critical (Must Have for MVP)
1. VAR file validation
2. VAR file parsing
3. Metadata extraction
4. Repository scanning
5. Installation/uninstallation
6. Database operations

### 🟡 High Priority (Important for v1.0)
7. Dependency resolution
8. Content analysis
9. Preview extraction
10. Search functionality
11. Duplicate detection

### 🟢 Medium Priority (Nice to Have)
12. Batch operations
13. Advanced search
14. Dependency graph
15. Statistics

### ⚪ Low Priority (Future)
16. Migration tool
17. Advanced UI features
18. Performance optimizations

---

**Last Updated**: 2024-11-29  
**Next Review**: After Phase 1 completion

