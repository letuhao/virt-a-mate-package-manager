# Implementation TODO List - Quick Reference

## 📋 Master Checklist

This checklist tracks ALL functions that need implementation. Check off as you complete each one.

---

## ✅ COMPLETED

- [x] Project structure setup
- [x] Domain entities (Repository, VarPackage, InstallationTarget, Dependency, Installation, ContentItem)
- [x] Value objects (Result<T> pattern)
- [x] ApplicationDbContext
- [x] DbContextFactory (for migrations)
- [x] All EF Core configurations (6 configurations)
  - [x] RepositoryConfiguration
  - [x] VarPackageConfiguration
  - [x] InstallationTargetConfiguration
  - [x] DependencyConfiguration
  - [x] InstallationConfiguration
  - [x] ContentItemConfiguration
- [x] Initial database migration created
- [x] appsettings.json created

---

## 🔄 IN PROGRESS

- [ ] Dependency extraction functions (ExtractDependencies, ExtractDependenciesRecursive, etc.)

---

## 📦 Phase 1: Core Infrastructure

### EF Core Configurations ✅
- [x] InstallationTargetConfiguration
- [x] DependencyConfiguration
- [x] InstallationConfiguration
- [x] ContentItemConfiguration

### Database Setup ✅
- [x] Create appsettings.json
- [x] Create initial migration
- [ ] Test database creation (optional - can test later)

---

## 🔍 Phase 2: VAR File Validation (Section 1) ✅

- [x] `ValidateVarFileName` (1.1) ✅
  - [x] Validate filename format (Creator.Package.Version.var)
  - [x] Validate creator name (1-60 chars)
  - [x] Validate package name (1-80 chars)
  - [x] Validate version (numeric or "latest")
  - [x] Return ValidationResult with parsed components
  - [ ] Unit tests (pending)

- [x] `ValidateVarFileStructure` (1.2) ✅
  - [x] Check file exists
  - [x] Validate ZIP archive
  - [x] Check meta.json exists
  - [x] Validate meta.json is JSON
  - [x] Check required fields
  - [ ] Unit tests (pending)

- [x] `ValidateVarFileComplete` (1.3) ✅
  - [x] Combine filename + structure validation
  - [x] Validate filename matches metadata
  - [x] Check file integrity
  - [ ] Unit tests (pending)

---

## 📝 Phase 3: VAR File Parsing (Section 2) ✅

- [x] `ParseVarFileName` (2.1) ✅
  - [x] Split filename by dots
  - [x] Extract creator, package, version
  - [x] Handle "latest" version
  - [x] Return VarFileNameComponents
  - [ ] Unit tests (pending)

- [x] `ExtractVarMetadata` (2.2) ✅
  - [x] Open VAR as ZIP
  - [x] Read meta.json
  - [x] Parse JSON
  - [x] Extract all metadata fields
  - [x] Extract content list
  - [x] Extract custom options
  - [ ] Unit tests (pending)

---

## 🔗 Phase 4: Dependency Extraction (Section 3)

- [ ] `ExtractDependencies` (3.1)
  - [ ] Read dependencies from meta.json
  - [ ] Call recursive extraction
  - [ ] Return flattened list
  - [ ] Unit tests

- [ ] `ExtractDependenciesRecursive` (3.1)
  - [ ] Handle nested dependencies
  - [ ] Extract license type
  - [ ] Handle missing flag
  - [ ] Track depth
  - [ ] Unit tests

- [ ] `IsValidDependencyName` (3.1)
  - [ ] Validate format
  - [ ] Unit tests

- [ ] `FlattenDependencies` (3.2)
  - [ ] Flatten nested structure
  - [ ] Handle circular dependencies
  - [ ] Unit tests

- [ ] `CountDepth` (3.1)
  - [ ] Calculate dependency depth
  - [ ] Unit tests

---

## 📊 Phase 5: Content Analysis (Section 4)

- [ ] `AnalyzeVarContent` (4.1)
  - [ ] Open VAR as ZIP
  - [ ] Iterate entries
  - [ ] Determine content type
  - [ ] Count by type
  - [ ] Build ContentItem list
  - [ ] Unit tests

- [ ] `DetermineContentType` (4.1)
  - [ ] Check scene patterns
  - [ ] Check look patterns
  - [ ] Check clothing patterns
  - [ ] Check hairstyle patterns
  - [ ] Check script patterns
  - [ ] Check asset patterns
  - [ ] Check morph patterns
  - [ ] Check pose patterns
  - [ ] Check skin patterns
  - [ ] Unit tests

- [ ] `DetermineIfPreset` (4.1)
  - [ ] Check based on extension
  - [ ] Unit tests

- [ ] `IncrementCount` (4.1)
  - [ ] Helper for counting
  - [ ] Unit tests

---

## 🖼️ Phase 6: Preview Image Extraction (Section 5)

- [ ] `ExtractPreviewImages` (5.1)
  - [ ] Analyze content
  - [ ] Filter previewable content
  - [ ] Extract each preview
  - [ ] Unit tests

- [ ] `ShouldHavePreview` (5.1)
  - [ ] Check content type
  - [ ] Unit tests

- [ ] `FindPreviewImagePath` (5.1)
  - [ ] Build preview path
  - [ ] Try alternatives
  - [ ] Unit tests

- [ ] `ExtractSinglePreviewImage` (5.1)
  - [ ] Determine output structure
  - [ ] Generate filename
  - [ ] Extract from ZIP
  - [ ] Save to disk
  - [ ] Unit tests

- [ ] `GetTypePrefix` (5.1)
  - [ ] Return prefix by type
  - [ ] Unit tests

---

## 📁 Phase 7: Repository Organization (Section 6)

- [ ] `OrganizeVarFiles` (6.1)
  - [ ] Validate VAR filename
  - [ ] Check database for existing VAR name
  - [ ] Handle local duplicates
  - [ ] Handle cross-repo conflicts
  - [ ] Move files appropriately
  - [ ] Unit tests

- [ ] `GetVarPackageByName` (6.1)
  - [ ] Query by VAR name (unique key)
  - [ ] Unit tests

- [ ] `MoveToInvalid` (6.1)
  - [ ] Generate unique filename
  - [ ] Move to invalid directory
  - [ ] Unit tests

- [ ] `MoveToRedundant` (6.1)
  - [ ] Generate unique filename
  - [ ] Move to redundant directory
  - [ ] Unit tests

- [ ] `GenerateUniqueFileName` (6.1)
  - [ ] Append counter if exists
  - [ ] Unit tests

- [x] `FilesAreIdentical` (6.1) ✅ (implemented in FileHashService)
  - [x] Compare file sizes
  - [x] Compare hashes
  - [ ] Unit tests (pending)

- [ ] `DetectDuplicateVarFiles` (6.2)
  - [ ] Query database by VAR name
  - [ ] Group by VAR name
  - [ ] Apply selection strategy
  - [ ] Unit tests

- [ ] `DeterminePrimaryVarPackage` (6.2)
  - [ ] Implement all selection strategies
  - [ ] Unit tests

- [ ] `ResolveDuplicateVarFile` (6.3)
  - [ ] Query by VAR name
  - [ ] Apply strategy if multiple
  - [ ] Check content mismatch
  - [ ] Unit tests

- [ ] `MarkDuplicateVarFiles` (6.4)
  - [ ] Update database flags
  - [ ] Create duplicate group records
  - [ ] Unit tests

- [ ] `HandleDuplicateDuringInstallation` (6.5)
  - [ ] Resolve VAR package
  - [ ] Check user preference
  - [ ] Install from selected
  - [ ] Unit tests

---

## 🔌 Phase 8: Installation Functions (Section 7)

- [ ] `InstallVarPackage` (7.1)
  - [ ] Get VAR package
  - [ ] Get installation target
  - [ ] Check if already installed
  - [ ] Create symlink
  - [ ] Install dependencies
  - [ ] Create installation record
  - [ ] Unit tests

- [ ] `InstallDependencies` (7.1)
  - [ ] Get dependencies
  - [ ] Resolve each
  - [ ] Recursively install
  - [ ] Unit tests

- [ ] `CreateSymbolicLink` (7.2)
  - [ ] Validate target exists
  - [ ] Use Windows API
  - [ ] Handle errors
  - [ ] Unit tests

- [ ] `UninstallVarPackage`
  - [ ] Get installation
  - [ ] Delete symlink
  - [ ] Update database
  - [ ] Unit tests

- [ ] `BatchInstallVarPackages`
  - [ ] Process multiple
  - [ ] Progress reporting
  - [ ] Error handling
  - [ ] Unit tests

- [ ] `VerifyInstallations`
  - [ ] Check symlinks valid
  - [ ] Detect broken links
  - [ ] Unit tests

- [ ] `RepairBrokenSymlinks`
  - [ ] Find broken links
  - [ ] Repair or remove
  - [ ] Unit tests

---

## 🔎 Phase 9: Repository Scanning (Section 8)

- [ ] `ScanRepository` (8.1)
  - [ ] Get existing VARs from DB
  - [ ] Scan file system
  - [ ] Compare and detect changes
  - [ ] Add new VARs
  - [ ] Update modified VARs
  - [ ] Mark deleted VARs
  - [ ] Progress reporting
  - [ ] Cancellation support
  - [ ] Integration tests

- [ ] `ScanFileSystemForVarFiles` (8.1)
  - [ ] Get all .var files
  - [ ] Exclude special directories
  - [ ] Skip symlinks
  - [ ] Unit tests

- [ ] `IncrementalScanRepository`
  - [ ] Scan only changed files
  - [ ] Use modification time
  - [ ] Unit tests

---

## 🔐 Phase 10: File Hashing (Section 9) ✅

- [x] `ComputeFileHash` (9.1) ✅
  - [x] Read file
  - [x] Compute SHA-256
  - [x] Return hex string (lowercase)
  - [ ] Unit tests (pending)

- [x] `ComputeFileHashAsync` (9.1) ✅
  - [x] Async file reading
  - [x] Cancellation support
  - [ ] Unit tests (pending)

- [x] `FilesAreIdentical` ✅
  - [x] Compare file sizes
  - [x] Compare hashes
  - [ ] Unit tests (pending)

- [x] `FilesAreIdenticalAsync` ✅
  - [x] Async comparison
  - [ ] Unit tests (pending)

---

## 🗄️ Phase 11: Repository Pattern

### Interfaces (Core Layer)
- [ ] `IRepository<T>`
- [ ] `IVarPackageRepository`
- [ ] `IRepositoryRepository`
- [ ] `IInstallationTargetRepository`
- [ ] `IDependencyRepository`
- [ ] `IInstallationRepository`
- [ ] `IUnitOfWork`

### Implementations (Infrastructure)
- [ ] `BaseRepository<T>`
- [ ] `VarPackageRepository`
- [ ] `RepositoryRepository`
- [ ] `InstallationTargetRepository`
- [ ] `DependencyRepository`
- [ ] `InstallationRepository`
- [ ] `UnitOfWork`

---

## 🎯 Phase 12: Application Services

### Service Interfaces
- [ ] `IVarPackageService`
- [ ] `IRepositoryService`
- [ ] `IInstallationService`
- [ ] `IInstallationTargetService`
- [ ] `IDependencyService`
- [ ] `ISearchService`
- [ ] `IFileSystemService`

### Service Implementations
- [ ] `VarPackageService`
- [ ] `RepositoryService`
- [ ] `InstallationService`
- [ ] `InstallationTargetService`
- [ ] `DependencyService`
- [ ] `SearchService`
- [ ] `FileSystemService`

---

## 📋 Phase 13: DTOs & Commands

- [ ] All DTOs from API Specifications (30+)
- [ ] All Commands (10+)
- [ ] All Queries (10+)

---

## 🧹 Phase 14: Utilities

- [ ] `IsSymbolicLink`
- [ ] `ResolveSymbolicLink`
- [ ] `DeleteSymbolicLink`
- [ ] `GetFileSizeFormatted`
- [ ] `ParseVersionString`

---

## 🧪 Phase 15: Testing

- [ ] Unit tests for all functions
- [ ] Integration tests
- [ ] E2E tests
- [ ] Achieve 80%+ coverage

---

## 📊 Progress Summary

**Total Functions**: ~150+  
**Completed**: 15 (6 entities + DbContext + 9 core functions) ✅  
**In Progress**: Dependency extraction functions  
**Remaining**: ~135

**Current Focus**: Implement dependency extraction functions (ExtractDependencies, ExtractDependenciesRecursive, etc.)

---

## Quick Status

✅ Foundation: DONE  
✅ EF Core: DONE (all configs + migration)  
🟡 Core Functions: IN PROGRESS (9/24 completed)  
⬜ Services: NOT STARTED  
⬜ UI: NOT STARTED

---

**Last Updated**: 2024-11-29
