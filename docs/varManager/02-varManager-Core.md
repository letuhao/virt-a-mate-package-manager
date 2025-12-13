# Phase 2: varManager Core Application

## Overview

The **varManager** project is the core Windows Forms application that manages VAR (Virt-a-Mate Addon Resource) files. It implements a repository-based package management system using symbolic links.

## Project Configuration

- **Framework**: .NET Framework 4.8
- **Platform Target**: x64
- **Output Type**: WinExe (Windows Application)
- **AllowUnsafeBlocks**: Enabled (for P/Invoke operations)
- **Application Icon**: VarManager.ico

## Entry Point

**Program.cs**: Simple entry point that initializes Windows Forms application
```csharp
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
Application.Run(new Form1());
```

## Core Classes

### Form1 (Main Form)

The primary application window containing all main functionality.

#### Key Responsibilities

1. **VAR File Management**
   - Scanning and indexing VAR files
   - Organizing VAR files into repository structure
   - Creating and managing symbolic links

2. **Database Operations**
   - Loading VAR metadata into database
   - Tracking installation status
   - Managing dependencies

3. **UI Management**
   - Displaying VAR lists with filtering
   - Preview image display
   - Progress reporting for long operations

#### Major Components

##### 1. VAR Naming Convention Validation

```csharp
public static bool ComplyVarName(string varname)
```
- Validates VAR name format: `CreatorName.PackageName.Version`
- Version must be numeric or "latest"
- Returns true if format is compliant

##### 2. VAR Organization (TidyVars)

Organizes VAR files into directory structure:
- `___VarTidied___/CreatorName/PackageName.version.var`
- Moves non-compliant files to `___VarnotComplyRule___`
- Handles duplicates in `___VarRedundant___`

##### 3. Database Update (UpdDB)

Scans VAR files and updates database:
- Extracts metadata from VAR files
- Extracts preview images
- Parses `meta.json` files
- Updates dependency information

##### 4. Installation Management

- Creates symbolic links in `AddonPackages` directory
- Tracks installation status in database
- Manages installation directory structure

#### Background Workers

**backgroundWorkerInstall**: Handles long-running operations
- Fills data tables on startup
- Processes VAR installations
- Updates database

### Comm.cs (Common Utilities)

Static utility class providing system-level operations.

#### Key Functions

##### 1. File System Operations

- **LocateFile**: Opens Windows Explorer to file location
- **ValidFileName**: Sanitizes file names
- **DirectoryMoveAll**: Recursively moves directory contents
- **MakeRelativePath**: Converts absolute paths to relative

##### 2. Symbolic Link Operations

Uses P/Invoke to call Windows Kernel32 APIs:

- **CreateHardLink**: Creates hard links
- **CreateSymbolicLink**: Creates symbolic links (file/directory)
- **ReparsePoint**: Resolves symbolic link targets
- **SetSymboLinkFileTime**: Sets file times on symbolic links

Uses Windows backup privileges for reparse point access.

### ZipHandler.cs

Handles ZIP file operations using SharpZipLib and 7-Zip.

#### Key Functions

##### 1. Extraction Methods

- **ExtractZipFile**: Uses SharpZipLib (basic)
- **ExtractZipFileBy7Z**: Uses 7-Zip executable
- **ExtractZipFileWithOptimalEncoding**: Auto-detects encoding

##### 2. Compression Methods

- **CreateZipFile**: Uses SharpZipLib
- **CreateZipFile7Z**: Uses 7-Zip executable (recommended)

##### 3. Encoding Detection

- Supports multiple encodings (UTF-8, GBK, Shift_JIS, etc.)
- Auto-detects optimal encoding for ZIP entries
- Handles mixed-encoding ZIP files

### SimpleLogger.cs

Thread-safe logging utility (MIT License).

#### Log Levels

- TRACE
- INFO
- DEBUG
- WARNING
- ERROR
- FATAL

#### Features

- Thread-safe file writing
- Timestamped log entries
- Automatic log file creation
- Format: `AssemblyName.log`

### Database Integration

Uses Microsoft Access database via OLEDB.

#### DataSet Structure (varManagerDataSet)

Generated from XSD schema:

- **vars**: VAR file metadata
- **dependencies**: Dependency relationships
- **installStatus**: Installation tracking

## Forms Architecture

### Form1 (Main Form)

Primary application interface with:
- DataGridView for VAR listing
- Preview panel for images
- Filter controls
- Action buttons (Install, Uninstall, etc.)

### FormSettings

Application configuration:
- VaM installation path
- VAR repository path
- Other user preferences

### FormAnalysis

Scene analysis tool:
- Analyzes scene JSON files
- Extracts character information
- Maps VAR dependencies
- Prepares scene data for loading

### FormScenes

Scene management interface:
- Lists available scenes
- Shows scene previews
- Manages scene installations

### FormHub

Hub integration for package browsing:
- Connects to hub.virtamate.com
- Downloads VAR metadata
- Manages download queues

### FormMissingVars

Detects and reports missing dependencies:
- Scans installed VARs
- Identifies missing dependencies
- Generates download links

### FormStaleVars

Identifies outdated VAR files:
- Compares versions
- Marks stale entries
- Suggests updates

### FormVarDetail

Detailed VAR information viewer:
- Shows VAR metadata
- Displays dependencies
- Shows installation status

### FormVarsMove

Bulk VAR file movement tool:
- Moves VARs between locations
- Updates database references

### FormUninstallVars

VAR uninstallation interface:
- Removes symbolic links
- Updates database
- Optionally deletes VAR files

### PrepareSaves

Prepares save files for packaging:
- Organizes scene files
- Prepares appearance presets
- Creates package structure

## Data Flow

### Startup Sequence

1. **Application Start**
   - Checks VaM installation path
   - Opens settings if path invalid
   - Initializes database connection

2. **Background Worker: FillDataTables**
   - Loads VAR data from database
   - Scans repository directory
   - Updates VAR list in UI

3. **UI Initialization**
   - Populates DataGridView
   - Loads filter settings
   - Restores window state

### VAR Installation Flow

1. **User Selection**: User selects VARs to install
2. **Dependency Check**: System checks for dependencies
3. **Link Creation**: Creates symbolic links in AddonPackages
4. **Database Update**: Updates installation status
5. **UI Refresh**: Updates UI to reflect changes

### VAR Organization Flow

1. **Scan**: Scans VAR repository directory
2. **Validate**: Checks naming convention compliance
3. **Organize**: Moves files to organized structure
4. **Database**: Updates database with new locations
5. **Cleanup**: Removes duplicates and non-compliant files

## VAR File Structure

### Naming Convention

```
CreatorName.PackageName.Version.var
```

Examples:
- `MeshedVR.ExamplePackage.1.var`
- `CreatorName.PackageName.latest.var`

### Internal Structure

VAR files are ZIP archives containing:
- `meta.json`: Package metadata
- Content files organized in VaM directory structure
- Preview images (optional)

### meta.json Format

```json
{
  "licenseType": "CC BY",
  "creatorName": "Creator",
  "packageName": "Package",
  "description": "...",
  "contentList": [...],
  "dependencies": {...}
}
```

## Special Directories

All special directories use `___` prefix:

- `___VarTidied___`: Organized VAR files
- `___VarRedundant___`: Duplicate files
- `___VarnotComplyRule___`: Non-compliant names
- `___PreviewPics___`: Extracted preview images
- `___StaleVars___`: Outdated versions
- `___OldVersionVars___`: Old versions
- `___DeletedVars___`: Soft-deleted files
- `___VarsLink___`: Installation links
- `___MissingVarLink___`: Missing dependency links

## Threading Model

- **Main Thread**: UI operations only
- **BackgroundWorker**: Long-running operations
- **Thread Safety**: Uses `Invoke`/`BeginInvoke` for UI updates

## Error Handling

- Comprehensive try-catch blocks
- Logging of all errors
- User-friendly error messages
- Graceful degradation on errors

## Configuration

Stored in `Settings.settings`:
- `vampath`: VaM installation directory
- `varspath`: VAR repository directory
- Other user preferences

## Dependencies

### External Libraries

- **SharpZipLib** (v1.4.2): ZIP file operations
- **SimpleJSON**: JSON parsing
- **DgvFilterPopup**: DataGridView filtering
- **DragNDrop**: Drag-and-drop support

### System Requirements

- Windows OS (for symbolic link support)
- .NET Framework 4.8
- Administrator privileges (for symbolic links)

## Performance Considerations

1. **Database Queries**: Optimized with indexed lookups
2. **File Operations**: Background processing
3. **UI Updates**: Throttled to prevent blocking
4. **Memory Management**: Disposes resources properly

## Security Considerations

1. **Path Validation**: Prevents directory traversal
2. **Symlink Security**: Validates symlink targets
3. **File Access**: Proper exception handling
4. **Privilege Escalation**: Uses minimal required privileges

---

Continue to:
- [Phase 3: MMDLoader Application](./03-MMDLoader.md)
- [Phase 4: LoadScene Component](./04-LoadScene.md)

