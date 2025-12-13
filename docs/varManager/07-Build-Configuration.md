# Phase 7: Build Configuration & Dependencies

## Overview

This phase documents the build system, dependencies, packaging, and deployment configuration for the varManager-MMDLoader solution. It covers project references, NuGet packages, build tools, and deployment strategies.

## Solution Configuration

### Solution File

- **File**: `varManager.sln`
- **Visual Studio Version**: 17.1.32407.343
- **Minimum Version**: 10.0.40219.1

### Build Configurations

Both Debug and Release configurations available:
- **Debug**: Development builds with symbols
- **Release**: Optimized production builds

### Platform Targets

- **varManager**: x64 only
- **Other Projects**: AnyCPU

## Project-Specific Configurations

### 1. varManager

**Framework**: .NET Framework 4.8

**Key Settings**:
```xml
<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
<PlatformTarget>x64</PlatformTarget>
<AllowUnsafeBlocks>true</AllowUnsafeBlocks>  <!-- For P/Invoke -->
<Prefer32Bit>false</Prefer32Bit>
```

**Dependencies**:
- Project References:
  - `DgvFilterPopup`
  - `DragNDrop`
- NuGet Packages:
  - `SharpZipLib` v1.4.2

**External References**:
- System libraries (standard .NET Framework)
- Windows Forms
- System.Data (OLEDB for Access)

**Output**:
- Type: Windows Executable (WinExe)
- Icon: `VarManager.ico`
- Manifest: `app.manifest`

### 2. MMDLoader

**Framework**: .NET 6.0-windows

**Key Settings**:
```xml
<TargetFramework>net6.0-windows</TargetFramework>
<UseWPF>true</UseWPF>
<UseWindowsForms>True</UseWindowsForms>
<FileVersion>1.0.1.0</FileVersion>
```

**Dependencies**:
- WPF framework
- Windows Forms (for FolderBrowserDialog)
- SimpleJSON (included source)

**Output**:
- Type: Windows Executable (WinExe)
- Icon: `icon.ico`

### 3. LoadScene

**Framework**: .NET Framework 3.5 (Unity compatibility)

**Key Settings**:
```xml
<TargetFrameworkVersion>v3.5</TargetFrameworkVersion>
<PlatformTarget>AnyCPU</PlatformTarget>
<OutputType>Library</OutputType>
```

**Dependencies**:
- Unity Engine assemblies (referenced, not copied):
  - `Assembly-CSharp.dll`
  - `UnityEngine.dll`
  - `UnityEngine.CoreModule.dll`
  - `UnityEngine.PhysicsModule.dll`
  - `UnityEngine.TextRenderingModule.dll`
  - `UnityEngine.UI.dll`

**Build Tools**:
- **ILMerge** v3.0.29
- **MSBuild.ILMerge.Task** v1.1.3

**ILMerge Configuration**:
- Merges all dependencies into single DLL
- Order specified in `ILMergeOrder.txt`
- Configuration in `ILMerge.props`

**Output**:
- Type: Library (DLL)
- Merged output for Unity plugin deployment

### 4. DgvFilterPopup

**Framework**: .NET Framework 2.0

**Key Settings**:
```xml
<TargetFrameworkVersion>v2.0</TargetFrameworkVersion>
<OutputType>Library</OutputType>
```

**Output**:
- Type: Library (DLL)
- Used as reference by varManager

### 5. DragNDrop

**Framework**: .NET Framework

**Output**:
- Type: Library (DLL)

### 6. StarRatingControl

**Framework**: .NET Framework

**Output**:
- Type: Library (DLL)

### 7. ThreeStateTreeView

**Framework**: .NET Framework

**Output**:
- Type: Library (DLL)

## NuGet Packages

### Packages Directory

Location: `packages/`

### Packages Used

1. **ILMerge.3.0.29**
   - Purpose: Assembly merging for LoadScene
   - Location: `packages/ILMerge.3.0.29/`

2. **MSBuild.ILMerge.Task.1.1.3**
   - Purpose: MSBuild integration for ILMerge
   - Location: `packages/MSBuild.ILMerge.Task.1.1.3/`

3. **SharpZipLib.1.4.2**
   - Purpose: ZIP file operations in varManager
   - Installed via PackageReference

### Package Management

- Uses NuGet package restore
- Packages.config for LoadScene
- PackageReference for varManager

## Build Tools

### ILMerge

**Purpose**: Merge multiple assemblies into single DLL for Unity plugin

**Configuration** (`ILMerge.props`):
- Input assemblies
- Output file
- Merge order

**Usage**: 
- Integrated into MSBuild
- Runs automatically during Release build
- Merges LoadScene dependencies

### MSBuild

**Tasks**:
- Compile projects
- Resolve references
- Copy outputs
- Run ILMerge

## External Tools

### 7-Zip

**Usage**: Used by ZipHandler for ZIP operations

**Path**: `C:\Program Files\7-Zip\7z.exe`

**Operations**:
- Extract ZIP files
- Create ZIP files
- Handles encoding issues

**Note**: Hardcoded path may need configuration.

## Dependencies

### System Dependencies

#### varManager
- .NET Framework 4.8 Runtime
- Windows OS (for symbolic links)
- Microsoft Access Database Engine (for OLEDB)
- 7-Zip (optional, for ZIP operations)

#### MMDLoader
- .NET 6.0 Runtime
- Windows OS

#### LoadScene
- Unity Engine (VaM 1.20.77.9)
- .NET Framework 3.5 Runtime (via Unity)

### Third-Party Libraries

#### Included Source

**SimpleJSON**:
- Location: `varManager/SimpleJSON/`, `MMDLoader/SimpleJSON/`
- License: Not specified
- Purpose: JSON parsing (lightweight alternative)

#### NuGet Packages

- **SharpZipLib**: ZIP file operations
- **ILMerge**: Assembly merging

## Build Process

### Compilation Order

1. Component libraries first:
   - DgvFilterPopup
   - DragNDrop
   - StarRatingControl
   - ThreeStateTreeView

2. LoadScene (with ILMerge):
   - Compiles library
   - Merges dependencies
   - Outputs single DLL

3. Main applications:
   - varManager (depends on components)
   - MMDLoader (standalone)

### Build Steps

#### Standard Build
```
1. Restore NuGet packages
2. Build component libraries
3. Build LoadScene (with ILMerge)
4. Build varManager
5. Build MMDLoader
```

#### Clean Build
```
1. Clean solution
2. Delete bin/obj folders
3. Restore packages
4. Rebuild all
```

## Deployment

### Output Structure

#### varManager
```
bin/Release/
  varManager.exe
  varManager.exe.config
  varManager.mdb
  VarManager.ico
  DgvFilterPopup.dll
  DragNDrop.dll
  ICSharpCode.SharpZipLib.dll
  [Resources]
```

#### MMDLoader
```
bin/Release/net6.0-windows/
  MMDLoader.exe
  MMDLoader.dll
  icon.ico
  [Resources]
```

#### LoadScene
```
bin/Release/
  LoadScene.dll (merged)
  (deployed to VaM Custom/Scripts/feelfar/)
```

### Deployment Targets

1. **varManager**: Standalone application
   - Copy entire bin/Release folder
   - Database file included

2. **MMDLoader**: Standalone application
   - Self-contained or framework-dependent
   - Settings stored in user config

3. **LoadScene**: Unity plugin
   - Copy DLL to VaM plugin directory
   - Requires Unity/VaM runtime

## Configuration Files

### App.config (varManager)

Application configuration:
- Connection strings
- Application settings
- Runtime configuration

### Settings.settings

User settings stored in:
- `Properties/Settings.settings`
- User-specific storage

### app.manifest

Application manifest:
- Execution level
- Windows version compatibility
- DPI awareness

## Versioning

### Assembly Versions

- **varManager**: From AssemblyInfo.cs
- **MMDLoader**: 1.0.1.0 (FileVersion/AssemblyVersion)
- **LoadScene**: From AssemblyInfo.cs

### Version Management

- Update AssemblyInfo.cs
- Update FileVersion in project files
- Maintain version consistency

## Build Environment

### Requirements

- **Visual Studio**: 2017+ (recommended 2022)
- **MSBuild**: Included with Visual Studio
- **.NET SDK**: For .NET 6.0 projects
- **Windows SDK**: For Windows APIs

### Optional Tools

- **7-Zip**: For ZIP operations
- **ILMerge**: Included via NuGet
- **Access Database Engine**: For database operations

## Troubleshooting

### Common Issues

1. **ILMerge Failures**:
   - Check assembly references
   - Verify ILMergeOrder.txt
   - Check for conflicting types

2. **Missing References**:
   - Restore NuGet packages
   - Check project references
   - Verify Unity assemblies exist

3. **7-Zip Not Found**:
   - Install 7-Zip
   - Update path in ZipHandler
   - Falls back to SharpZipLib

4. **Database Connection**:
   - Install Access Database Engine
   - Check connection string
   - Verify database file exists

## Optimization

### Build Optimizations

- **Release Build**: Optimizations enabled
- **Debug Symbols**: Separate PDB files
- **Code Analysis**: Optional static analysis

### Runtime Optimizations

- Lazy loading where applicable
- Efficient database queries
- Background processing for long operations

## Security

### Code Signing

- Not currently implemented
- Can be added for distribution

### Dependencies

- Verify NuGet package sources
- Review third-party code
- Keep dependencies updated

---

## Summary

The build system uses:
- Multiple .NET Framework versions for compatibility
- ILMerge for Unity plugin deployment
- NuGet for dependency management
- MSBuild for compilation
- Component-based architecture for reusability

All projects compile to executable or library outputs ready for deployment.

---

**Documentation Complete**

This completes the comprehensive technical specification for varManager-MMDLoader v1.0.1.0. All phases have been documented with detailed analysis of architecture, components, database structure, and build configuration.

