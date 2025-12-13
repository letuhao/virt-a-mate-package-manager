# Phase 1: Architecture Overview

## Solution Structure

The varManager-MMDLoader system is organized as a multi-project Visual Studio solution containing 8 distinct projects, each serving a specific purpose in the overall system architecture.

### Solution File

- **File**: `varManager.sln`
- **Visual Studio Version**: 17.1.32407.343
- **Minimum Version**: 10.0.40219.1
- **Format**: Visual Studio Solution File Format Version 12.00

### Project Breakdown

The solution consists of the following projects:

1. **varManager** - Main Windows Forms application
2. **MMDLoader** - WPF application for MMD loading
3. **LoadScene** - Unity plugin library
4. **DgvFilterPopup** - DataGridView filter component
5. **DragNDrop** - Drag and drop ListView component
6. **StarRatingControl** - Star rating UI control
7. **ThreeStateTreeView** - Three-state checkbox tree view
8. **HUB** - Minimal placeholder component

## Architecture Pattern

The system follows a **layered component architecture** with clear separation of concerns:

```
┌─────────────────────────────────────────────────────────┐
│                    Application Layer                     │
│  ┌──────────────┐              ┌──────────────┐         │
│  │  varManager  │              │  MMDLoader   │         │
│  │ (WinForms)   │              │   (WPF)      │         │
│  └──────┬───────┘              └──────┬───────┘         │
└─────────┼──────────────────────────────┼─────────────────┘
          │                              │
┌─────────┼──────────────────────────────┼─────────────────┐
│         │    Component Layer           │                 │
│  ┌──────▼──────┐              ┌───────▼────────┐        │
│  │DgvFilterPopup│              │   DragNDrop    │        │
│  │StarRating    │              │ThreeStateTree  │        │
│  └──────────────┘              └────────────────┘        │
└──────────────────────────────────────────────────────────┘
          │                              │
┌─────────┼──────────────────────────────┼─────────────────┐
│         │    Plugin/Unity Layer        │                 │
│  ┌──────▼──────────────────────────────▼───────┐        │
│  │            LoadScene                         │        │
│  │        (Unity Plugin)                        │        │
│  └──────────────────────────────────────────────┘        │
└──────────────────────────────────────────────────────────┘
          │
┌─────────┼─────────────────────────────────────────────────┐
│         │    Data Layer                                   │
│  ┌──────▼───────────────────────────────────────┐        │
│  │     varManager.mdb (Access Database)         │        │
│  │     SimpleJSON (JSON Parser)                 │        │
│  │     ZipHandler (ZIP Operations)              │        │
│  └──────────────────────────────────────────────┘        │
└──────────────────────────────────────────────────────────┘
```

## Project Dependencies

### varManager Project
- **Depends on**:
  - `DgvFilterPopup`
  - `DragNDrop`
  - External: `ICSharpCode.SharpZipLib` (v1.4.2)
  - .NET Framework 4.8
  - Windows Forms

### MMDLoader Project
- **Depends on**:
  - .NET 6.0-windows
  - WPF
  - Windows Forms (for FolderBrowserDialog)
  - SimpleJSON library

### LoadScene Project
- **Depends on**:
  - Unity engine libraries (Assembly-CSharp, UnityEngine, etc.)
  - .NET Framework 3.5 (for Unity compatibility)
  - Uses ILMerge for assembly merging

## Key Architectural Decisions

### 1. Repository Pattern for VAR Files

The system implements a repository-based approach where:
- All VAR files are stored in a centralized repository directory
- Symbolic links are created in `AddonPackages` directory pointing to repository files
- This allows efficient disk space usage and easier management

### 2. Symbolic Link Management

The application uses Windows symbolic links (both hard links and symbolic links) to manage VAR installations:
- Uses P/Invoke to call Windows Kernel32 APIs
- Supports both file and directory symbolic links
- Handles reparse point detection and resolution

### 3. Database-Driven Metadata

- Uses Microsoft Access database for tracking:
  - VAR installation status
  - Dependency relationships
  - Package metadata
- Implements DataSet/DataAdapter pattern for database operations

### 4. JSON-Based Configuration

- Uses SimpleJSON library for parsing VAR package metadata
- JSON files used for:
  - VAR package definitions (`meta.json`)
  - Scene configurations
  - LoadScene plugin communication

### 5. Component-Based UI

- Reusable UI components for common functionality:
  - Filter popups for DataGridView
  - Drag-and-drop list views
  - Star rating controls
  - Three-state tree views

## Communication Patterns

### Inter-Process Communication

1. **File-Based Communication**:
   - `loadscene.json` file in `Custom\PluginData\feelfar\` directory
   - MMDLoader writes JSON configuration
   - LoadScene Unity plugin reads the file

2. **Directory Structure Communication**:
   - `MMDForLoad` directory for MMD files
   - Various special directories with `___` prefix for organization

### Threading Model

- Uses `BackgroundWorker` for long-running operations
- UI updates via `Invoke` and `BeginInvoke` patterns
- Thread-safe logging with `SimpleLogger`

## Directory Structure Strategy

The system uses special directory names prefixed with `___` for organization:

- `___VarTidied___` - Organized VAR files by creator
- `___VarRedundant___` - Duplicate VAR files
- `___VarnotComplyRule___` - Non-compliant VAR names
- `___PreviewPics___` - Preview images
- `___StaleVars___` - Outdated VAR files
- `___OldVersionVars___` - Older versions
- `___DeletedVars___` - Deleted VAR files
- `___AddonPacksSwitch ___` - Package switching
- `___VarsLink___` - Installation links
- `___MissingVarLink___` - Missing VAR links

## Technology Stack Summary

| Component | Technology | Version |
|-----------|-----------|---------|
| varManager | .NET Framework | 4.8 |
| MMDLoader | .NET | 6.0-windows |
| LoadScene | .NET Framework | 3.5 |
| UI Framework | Windows Forms / WPF | - |
| Database | Microsoft Access | OLEDB |
| JSON Library | SimpleJSON | Custom |
| ZIP Library | SharpZipLib | 1.4.2 |
| Build Tool | ILMerge | 3.0.29 |

## Build Configuration

- **Platform**: x64 (varManager), AnyCPU (others)
- **Configuration**: Debug/Release
- **AllowUnsafeBlocks**: Enabled for varManager (symbolic link operations)

## Security Considerations

1. **Privilege Escalation**: Uses backup privileges for reparse point access
2. **File System Operations**: Comprehensive error handling for file operations
3. **Path Validation**: Validates file paths and prevents directory traversal
4. **Symlink Handling**: Careful handling of symbolic links to prevent security issues

## Extension Points

The architecture supports extensibility through:

1. **Custom Filters**: Extendable filter system for DataGridView
2. **Plugin System**: LoadScene can be extended with new scene loaders
3. **Custom Scripts**: VAR packages can include custom C# scripts
4. **Component Library**: Reusable UI components for other projects

## Next Steps

Continue to:
- [Phase 2: varManager Core Application](./02-varManager-Core.md) - Detailed analysis of the main application
- [Phase 3: MMDLoader Application](./03-MMDLoader.md) - MMD loading functionality
- [Phase 4: LoadScene Component](./04-LoadScene.md) - Unity plugin details

