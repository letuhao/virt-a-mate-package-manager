# Function Specifications

## Overview

This document provides detailed specifications for all functions in VirtaMatePackageManager, including algorithms, input/output, error handling, and implementation details.

---

## 1. VAR File Validation Functions

### 1.1 ValidateVarFileName

**Purpose**: Validates that a VAR filename follows the required naming convention.

**Naming Convention**: `Creator.Package.Version.var`
- Creator: 1-60 characters, alphanumeric and underscore
- Package: 1-80 characters, alphanumeric and underscore
- Version: Numeric (1, 2, 3...) or "latest"

**Algorithm**:
```
FUNCTION ValidateVarFileName(filePath: string): ValidationResult
BEGIN
    IF filePath is NULL or EMPTY THEN
        RETURN ValidationResult(IsValid: false, Error: "File path is empty")
    END IF
    
    fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath)
    extension = Path.GetExtension(filePath)
    
    IF extension ≠ ".var" THEN
        RETURN ValidationResult(IsValid: false, Error: "File extension must be .var")
    END IF
    
    parts = fileNameWithoutExtension.Split('.')
    
    IF parts.Length ≠ 3 THEN
        RETURN ValidationResult(IsValid: false, Error: "Filename must have 3 parts separated by dots")
    END IF
    
    creator = parts[0]
    package = parts[1]
    version = parts[2]
    
    // Validate creator name (1-60 chars, alphanumeric + underscore)
    IF creator.Length < 1 OR creator.Length > 60 THEN
        RETURN ValidationResult(IsValid: false, Error: "Creator name must be 1-60 characters")
    END IF
    
    IF NOT Regex.IsMatch(creator, "^[a-zA-Z0-9_]+$") THEN
        RETURN ValidationResult(IsValid: false, Error: "Creator name contains invalid characters")
    END IF
    
    // Validate package name (1-80 chars, alphanumeric + underscore)
    IF package.Length < 1 OR package.Length > 80 THEN
        RETURN ValidationResult(IsValid: false, Error: "Package name must be 1-80 characters")
    END IF
    
    IF NOT Regex.IsMatch(package, "^[a-zA-Z0-9_]+$") THEN
        RETURN ValidationResult(IsValid: false, Error: "Package name contains invalid characters")
    END IF
    
    // Validate version (numeric or "latest")
    IF version = "latest" THEN
        RETURN ValidationResult(IsValid: true, ParsedName: {Creator: creator, Package: package, Version: "latest"})
    END IF
    
    IF NOT Regex.IsMatch(version, "^[0-9]+$") THEN
        RETURN ValidationResult(IsValid: false, Error: "Version must be numeric or 'latest'")
    END IF
    
    versionNumber = ParseInt(version)
    IF versionNumber < 1 THEN
        RETURN ValidationResult(IsValid: false, Error: "Version must be >= 1")
    END IF
    
    RETURN ValidationResult(
        IsValid: true,
        ParsedName: {
            Creator: creator,
            Package: package,
            Version: versionNumber,
            IsLatest: false
        }
    )
END FUNCTION
```

**Input**:
- `filePath`: Full path to VAR file

**Output**:
- `ValidationResult` containing:
  - `IsValid`: Boolean
  - `Error`: Error message if invalid
  - `ParsedName`: Parsed components if valid

**Error Cases**:
- Empty file path
- Wrong extension
- Invalid format (not 3 parts)
- Invalid characters in creator/package
- Invalid version format

---

### 1.2 ValidateVarFileStructure

**Purpose**: Validates that a VAR file is a valid ZIP archive and contains required files.

**Algorithm**:
```
FUNCTION ValidateVarFileStructure(filePath: string): ValidationResult
BEGIN
    IF NOT File.Exists(filePath) THEN
        RETURN ValidationResult(IsValid: false, Error: "File does not exist")
    END IF
    
    TRY
        // Attempt to open as ZIP file
        USING zipFile = ZipFile.OpenRead(filePath)
            // Check if meta.json exists
            metaJsonEntry = zipFile.GetEntry("meta.json")
            
            IF metaJsonEntry IS NULL THEN
                RETURN ValidationResult(
                    IsValid: false,
                    Error: "VAR file missing meta.json",
                    MissingFiles: ["meta.json"]
                )
            END IF
            
            // Validate meta.json is readable
            TRY
                USING stream = metaJsonEntry.Open()
                    jsonContent = ReadAllText(stream)
                    
                    IF jsonContent IS NULL OR EMPTY THEN
                        RETURN ValidationResult(
                            IsValid: false,
                            Error: "meta.json is empty"
                        )
                    END IF
                    
                    // Try to parse as JSON
                    jsonObject = JsonDocument.Parse(jsonContent)
                    
                    IF jsonObject.RootElement.ValueKind ≠ JsonValueKind.Object THEN
                        RETURN ValidationResult(
                            IsValid: false,
                            Error: "meta.json is not a valid JSON object"
                        )
                    END IF
                END USING
            CATCH JsonException AS ex
                RETURN ValidationResult(
                    IsValid: false,
                    Error: $"meta.json is not valid JSON: {ex.Message}"
                )
            END TRY
            
            // Check for required fields in meta.json
            requiredFields = ["creatorName", "packageName", "contentList"]
            missingFields = []
            
            FOR EACH field IN requiredFields
                IF NOT jsonObject.RootElement.TryGetProperty(field, out _) THEN
                    missingFields.Add(field)
                END IF
            END FOR
            
            IF missingFields.Count > 0 THEN
                RETURN ValidationResult(
                    IsValid: false,
                    Error: "meta.json missing required fields",
                    MissingFields: missingFields
                )
            END IF
            
            RETURN ValidationResult(IsValid: true, Metadata: jsonObject)
        END USING
    CATCH ZipException AS ex
        RETURN ValidationResult(
            IsValid: false,
            Error: $"File is not a valid ZIP archive: {ex.Message}"
        )
    CATCH UnauthorizedAccessException AS ex
        RETURN ValidationResult(
            IsValid: false,
            Error: $"Access denied: {ex.Message}"
        )
    CATCH IOException AS ex
        RETURN ValidationResult(
            IsValid: false,
            Error: $"I/O error: {ex.Message}"
        )
    END TRY
END FUNCTION
```

**Input**:
- `filePath`: Full path to VAR file

**Output**:
- `ValidationResult` containing validation status and parsed metadata if valid

**Error Cases**:
- File doesn't exist
- Not a valid ZIP file
- Missing meta.json
- Invalid JSON in meta.json
- Missing required fields

---

### 1.3 ValidateVarFileComplete

**Purpose**: Performs complete validation including filename, structure, and content integrity.

**Algorithm**:
```
FUNCTION ValidateVarFileComplete(filePath: string): CompleteValidationResult
BEGIN
    result = CompleteValidationResult()
    
    // Step 1: Validate filename
    filenameValidation = ValidateVarFileName(filePath)
    result.FilenameValidation = filenameValidation
    
    IF NOT filenameValidation.IsValid THEN
        result.IsValid = false
        result.OverallError = filenameValidation.Error
        RETURN result
    END IF
    
    // Step 2: Validate file structure
    structureValidation = ValidateVarFileStructure(filePath)
    result.StructureValidation = structureValidation
    
    IF NOT structureValidation.IsValid THEN
        result.IsValid = false
        result.OverallError = structureValidation.Error
        RETURN result
    END IF
    
    // Step 3: Validate filename matches metadata
    metadata = structureValidation.Metadata
    parsedName = filenameValidation.ParsedName
    
    IF metadata.GetProperty("creatorName").GetString() ≠ parsedName.Creator THEN
        result.IsValid = false
        result.OverallError = "Creator name in filename doesn't match meta.json"
        result.Warnings.Add("Creator name mismatch")
        RETURN result
    END IF
    
    IF metadata.GetProperty("packageName").GetString() ≠ parsedName.Package THEN
        result.IsValid = false
        result.OverallError = "Package name in filename doesn't match meta.json"
        result.Warnings.Add("Package name mismatch")
        RETURN result
    END IF
    
    // Step 4: Check file integrity (optional hash check)
    TRY
        fileInfo = FileInfo(filePath)
        
        IF fileInfo.Length = 0 THEN
            result.IsValid = false
            result.OverallError = "VAR file is empty"
            RETURN result
        END IF
        
        IF fileInfo.Length < 100 THEN
            result.Warnings.Add("VAR file is suspiciously small")
        END IF
    CATCH Exception AS ex
        result.Warnings.Add($"Could not check file size: {ex.Message}")
    END TRY
    
    result.IsValid = true
    RETURN result
END FUNCTION
```

**Input**:
- `filePath`: Full path to VAR file

**Output**:
- `CompleteValidationResult` containing all validation results

---

## 2. VAR File Parsing Functions

### 2.1 ParseVarFileName

**Purpose**: Parses a VAR filename to extract creator, package, and version components.

**Algorithm**:
```
FUNCTION ParseVarFileName(fileName: string): VarFileNameComponents
BEGIN
    IF fileName IS NULL OR EMPTY THEN
        THROW ArgumentException("Filename cannot be null or empty")
    END IF
    
    fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName)
    parts = fileNameWithoutExtension.Split('.')
    
    IF parts.Length ≠ 3 THEN
        THROW FormatException($"Invalid VAR filename format. Expected Creator.Package.Version, got: {fileNameWithoutExtension}")
    END IF
    
    creator = parts[0]
    package = parts[1]
    versionString = parts[2]
    
    // Parse version
    IF versionString.ToLower() = "latest" THEN
        version = VarVersion.Latest
        versionNumber = null
    ELSE
        IF NOT Int32.TryParse(versionString, out versionNumber) THEN
            THROW FormatException($"Invalid version format: {versionString}. Must be numeric or 'latest'")
        END IF
        
        IF versionNumber < 1 THEN
            THROW ArgumentException($"Version must be >= 1, got: {versionNumber}")
        END IF
        
        version = VarVersion.Number(versionNumber)
    END IF
    
    RETURN VarFileNameComponents(
        Creator: creator,
        Package: package,
        Version: version,
        VersionNumber: versionNumber,
        IsLatest: version.IsLatest,
        FullName: fileNameWithoutExtension,
        OriginalFileName: fileName
    )
END FUNCTION
```

**Input**:
- `fileName`: VAR filename (with or without .var extension)

**Output**:
- `VarFileNameComponents` object with parsed components

**Exceptions**:
- `ArgumentException`: Invalid input
- `FormatException`: Invalid filename format

---

### 2.2 ExtractVarMetadata

**Purpose**: Extracts metadata from a VAR file's meta.json.

**Algorithm**:
```
FUNCTION ExtractVarMetadata(varFilePath: string): VarMetadata
BEGIN
    IF NOT File.Exists(varFilePath) THEN
        THROW FileNotFoundException($"VAR file not found: {varFilePath}")
    END IF
    
    TRY
        USING zipFile = ZipFile.OpenRead(varFilePath)
            metaJsonEntry = zipFile.GetEntry("meta.json")
            
            IF metaJsonEntry IS NULL THEN
                THROW InvalidOperationException("VAR file missing meta.json")
            END IF
            
            USING stream = metaJsonEntry.Open()
            USING reader = StreamReader(stream)
                jsonText = reader.ReadToEnd()
            END USING
            END USING
            
            // Parse JSON
            jsonDocument = JsonDocument.Parse(jsonText)
            root = jsonDocument.RootElement
            
            // Extract basic metadata
            metadata = VarMetadata()
            
            IF root.TryGetProperty("creatorName", out creatorElement) THEN
                metadata.CreatorName = creatorElement.GetString()
            END IF
            
            IF root.TryGetProperty("packageName", out packageElement) THEN
                metadata.PackageName = packageElement.GetString()
            END IF
            
            IF root.TryGetProperty("licenseType", out licenseElement) THEN
                metadata.LicenseType = licenseElement.GetString()
            END IF
            
            IF root.TryGetProperty("description", out descElement) THEN
                metadata.Description = descElement.GetString()
            END IF
            
            IF root.TryGetProperty("credits", out creditsElement) THEN
                metadata.Credits = creditsElement.GetString()
            END IF
            
            IF root.TryGetProperty("instructions", out instructionsElement) THEN
                metadata.Instructions = instructionsElement.GetString()
            END IF
            
            IF root.TryGetProperty("promotionalLink", out promoElement) THEN
                metadata.PromotionalLink = promoElement.GetString()
            END IF
            
            IF root.TryGetProperty("programVersion", out programElement) THEN
                metadata.ProgramVersion = programElement.GetString()
            END IF
            
            // Extract content list
            IF root.TryGetProperty("contentList", out contentElement) THEN
                IF contentElement.ValueKind = JsonValueKind.Array THEN
                    metadata.ContentList = []
                    FOR EACH item IN contentElement.EnumerateArray()
                        metadata.ContentList.Add(item.GetString())
                    END FOR
                END IF
            END IF
            
            // Extract dependencies (handled separately - see ExtractDependencies)
            IF root.TryGetProperty("dependencies", out depsElement) THEN
                metadata.HasDependencies = depsElement.ValueKind = JsonValueKind.Object
            END IF
            
            // Extract custom options
            IF root.TryGetProperty("customOptions", out optionsElement) THEN
                metadata.CustomOptions = ParseCustomOptions(optionsElement)
            END IF
            
            metadata.RawJson = jsonText
            metadata.MetaDate = metaJsonEntry.LastWriteTime.DateTime
            
            RETURN metadata
        END USING
    CATCH ZipException AS ex
        THROW InvalidOperationException($"Failed to read VAR file as ZIP: {ex.Message}", ex)
    CATCH JsonException AS ex
        THROW InvalidOperationException($"Failed to parse meta.json: {ex.Message}", ex)
    END TRY
END FUNCTION
```

**Input**:
- `varFilePath`: Full path to VAR file

**Output**:
- `VarMetadata` object containing all extracted metadata

**Exceptions**:
- `FileNotFoundException`: VAR file doesn't exist
- `InvalidOperationException`: Invalid ZIP or JSON structure
- `JsonException`: Invalid JSON format

---

## 3. Dependency Extraction Functions

### 3.1 ExtractDependencies

**Purpose**: Extracts dependencies from meta.json, including nested dependencies.

**Algorithm**:
```
FUNCTION ExtractDependencies(metaJson: JsonElement): List<DependencyInfo>
BEGIN
    dependencies = []
    
    IF metaJson.ValueKind ≠ JsonValueKind.Object THEN
        RETURN dependencies
    END IF
    
    IF NOT metaJson.TryGetProperty("dependencies", out depsElement) THEN
        RETURN dependencies
    END IF
    
    IF depsElement.ValueKind ≠ JsonValueKind.Object THEN
        RETURN dependencies
    END IF
    
    // Recursively extract dependencies
    ExtractDependenciesRecursive(depsElement, dependencies, parentPath: "")
    
    RETURN dependencies.Distinct().ToList()
END FUNCTION

FUNCTION ExtractDependenciesRecursive(
    depsElement: JsonElement,
    dependencies: List<DependencyInfo>,
    parentPath: string)
BEGIN
    FOR EACH dependencyProperty IN depsElement.EnumerateObject()
        dependencyName = dependencyProperty.Name
        dependencyValue = dependencyProperty.Value
        
        // Remove path prefix if present (e.g., "creator/package.version")
        IF dependencyName.Contains('/') THEN
            dependencyName = dependencyName.Substring(dependencyName.LastIndexOf('/') + 1)
        END IF
        
        // Validate dependency name format
        IF NOT IsValidDependencyName(dependencyName) THEN
            CONTINUE // Skip invalid dependency names
        END IF
        
        dependencyInfo = DependencyInfo()
        dependencyInfo.Name = dependencyName
        dependencyInfo.FullPath = IF parentPath ≠ "" THEN parentPath + " > " + dependencyName ELSE dependencyName
        dependencyInfo.Depth = CountDepth(parentPath)
        
        // Extract license type
        IF dependencyValue.ValueKind = JsonValueKind.Object THEN
            IF dependencyValue.TryGetProperty("licenseType", out licenseElement) THEN
                dependencyInfo.LicenseType = licenseElement.GetString()
            END IF
            
            // Check if missing
            IF dependencyValue.TryGetProperty("missing", out missingElement) THEN
                dependencyInfo.IsMissing = missingElement.GetBoolean()
            END IF
            
            // Extract nested dependencies
            IF dependencyValue.TryGetProperty("dependencies", out nestedDeps) THEN
                IF nestedDeps.ValueKind = JsonValueKind.Object THEN
                    ExtractDependenciesRecursive(
                        nestedDeps,
                        dependencies,
                        dependencyInfo.FullPath
                    )
                END IF
            END IF
        END IF
        
        dependencies.Add(dependencyInfo)
    END FOR
END FUNCTION

FUNCTION IsValidDependencyName(name: string): bool
BEGIN
    // Format: Creator.Package.Version or Creator.Package.latest
    parts = name.Split('.')
    
    IF parts.Length < 2 OR parts.Length > 3 THEN
        RETURN false
    END IF
    
    // Last part should be version (numeric) or "latest"
    lastPart = parts[parts.Length - 1]
    
    IF lastPart.ToLower() = "latest" THEN
        RETURN true
    END IF
    
    RETURN Regex.IsMatch(lastPart, "^[0-9]+$")
END FUNCTION

FUNCTION CountDepth(path: string): int
BEGIN
    IF path IS NULL OR EMPTY THEN
        RETURN 0
    END IF
    
    RETURN path.Split('>').Length - 1
END FUNCTION
```

**Input**:
- `metaJson`: JSON element containing dependencies

**Output**:
- List of `DependencyInfo` objects, flattened from nested structure

**Algorithm Complexity**: O(n) where n is total number of dependencies including nested

---

### 3.2 FlattenDependencies

**Purpose**: Flattens nested dependencies into a simple list with depth information.

**Algorithm**:
```
FUNCTION FlattenDependencies(dependencies: List<DependencyInfo>): List<FlattenedDependency>
BEGIN
    flattened = []
    visited = Set<string>()
    
    FOR EACH dependency IN dependencies
        ProcessDependency(dependency, flattened, visited, depth: 0)
    END FOR
    
    RETURN flattened.DistinctBy(d => d.Name).ToList()
END FUNCTION

FUNCTION ProcessDependency(
    dependency: DependencyInfo,
    flattened: List<FlattenedDependency>,
    visited: Set<string>,
    depth: int)
BEGIN
    IF visited.Contains(dependency.Name) THEN
        RETURN // Avoid infinite loops in circular dependencies
    END IF
    
    visited.Add(dependency.Name)
    
    flattened.Add(FlattenedDependency(
        Name: dependency.Name,
        LicenseType: dependency.LicenseType,
        IsMissing: dependency.IsMissing,
        Depth: depth,
        FullPath: dependency.FullPath
    ))
    
    // Process nested dependencies if any
    IF dependency.NestedDependencies IS NOT NULL THEN
        FOR EACH nested IN dependency.NestedDependencies
            ProcessDependency(nested, flattened, visited, depth + 1)
        END FOR
    END IF
    
    visited.Remove(dependency.Name) // Backtrack for other paths
END FUNCTION
```

**Input**:
- `dependencies`: List of DependencyInfo with potential nesting

**Output**:
- Flattened list of dependencies with depth information

---

---

## 4. Content Analysis Functions

### 4.1 AnalyzeVarContent

**Purpose**: Analyzes VAR file content and categorizes all entries by type (scenes, looks, clothing, etc.).

**Content Type Patterns**:

| Content Type | Pattern | File Extensions |
|--------------|---------|----------------|
| Scenes | `saves/scene/` | `.json` |
| Looks | `saves/person/appearance/` or `custom/atom/person/(general\|appearance)/` | `.json`, `.vac`, `.vap` |
| Clothing | `custom/clothing/` or `custom/atom/person/clothing/` | `.vam`, `.vap` |
| Hairstyle | `custom/hair/` or `custom/atom/person/hair/` | `.vam`, `.vap` |
| Scripts | `custom/scripts/` or `custom/atom/person/scripts/` | `.cs`, `.cslist` |
| Assets | `custom/assets/` | `.assetbundle` |
| Morphs | `custom/atom/person/morphs/` | `.vmi`, `.vap` |
| Poses | `saves/person/pose/` or `custom/atom/person/pose/` | `.json`, `.vac`, `.vap` |
| Skin | `custom/atom/person/skin/` | `.vap` |

**Algorithm**:
```
FUNCTION AnalyzeVarContent(varFilePath: string): ContentAnalysisResult
BEGIN
    contentCounts = ContentCounts() // Initialize all counts to 0
    contentItems = []
    
    TRY
        USING zipFile = ZipFile.OpenRead(varFilePath)
            FOR EACH entry IN zipFile.Entries
                IF entry.IsDirectory THEN
                    CONTINUE
                END IF
                
                contentType = DetermineContentType(entry.Name)
                
                IF contentType ≠ ContentType.Unknown THEN
                    isPreset = DetermineIfPreset(entry.Name, contentType)
                    
                    contentItem = ContentItem(
                        Type: contentType,
                        Path: entry.Name,
                        IsPreset: isPreset,
                        Size: entry.Length,
                        LastModified: entry.LastWriteTime
                    )
                    
                    contentItems.Add(contentItem)
                    IncrementCount(contentCounts, contentType)
                END IF
            END FOR
        END USING
        
        RETURN ContentAnalysisResult(
            ContentCounts: contentCounts,
            ContentItems: contentItems,
            TotalItems: contentItems.Count
        )
    CATCH Exception AS ex
        THROW InvalidOperationException($"Failed to analyze VAR content: {ex.Message}", ex)
    END TRY
END FUNCTION

FUNCTION DetermineContentType(entryPath: string): ContentType
BEGIN
    entryPathLower = entryPath.ToLower()
    
    // Scenes
    IF Regex.IsMatch(entryPathLower, @"saves/scene/.*?\.(json)$", RegexOptions.IgnoreCase) THEN
        RETURN ContentType.Scene
    END IF
    
    // Looks - two patterns
    IF Regex.IsMatch(entryPathLower, @"saves/person/appearance/.*?\.(json|vac)$", RegexOptions.IgnoreCase) THEN
        RETURN ContentType.Look
    END IF
    IF Regex.IsMatch(entryPathLower, @"custom/atom/person/(general|appearance)/.*?\.(json|vap)$", RegexOptions.IgnoreCase) THEN
        RETURN ContentType.Look
    END IF
    
    // Clothing - two patterns
    IF Regex.IsMatch(entryPathLower, @"custom/clothing/.*?\.(vam|vap)$", RegexOptions.IgnoreCase) THEN
        RETURN ContentType.Clothing
    END IF
    IF Regex.IsMatch(entryPathLower, @"custom/atom/person/clothing/.*?\.(vam|vap)$", RegexOptions.IgnoreCase) THEN
        RETURN ContentType.Clothing
    END IF
    
    // Hairstyle - two patterns
    IF Regex.IsMatch(entryPathLower, @"custom/hair/.*?\.(vam|vap)$", RegexOptions.IgnoreCase) THEN
        RETURN ContentType.Hairstyle
    END IF
    IF Regex.IsMatch(entryPathLower, @"custom/atom/person/hair/.*?\.(vam|vap)$", RegexOptions.IgnoreCase) THEN
        RETURN ContentType.Hairstyle
    END IF
    
    // Scripts
    IF Regex.IsMatch(entryPathLower, @"custom/(scripts|atom/person/scripts)/.*?\.(cs)$", RegexOptions.IgnoreCase) THEN
        RETURN ContentType.Script
    END IF
    IF Regex.IsMatch(entryPathLower, @"custom/(scripts|atom/person/scripts)/.*?\.(cslist)$", RegexOptions.IgnoreCase) THEN
        RETURN ContentType.ScriptList
    END IF
    
    // Assets
    IF Regex.IsMatch(entryPathLower, @"custom/assets/.*?\.(assetbundle)$", RegexOptions.IgnoreCase) THEN
        RETURN ContentType.Asset
    END IF
    
    // Morphs
    IF Regex.IsMatch(entryPathLower, @"custom/atom/person/morphs/.*?\.(vmi|vap)$", RegexOptions.IgnoreCase) THEN
        RETURN ContentType.Morph
    END IF
    
    // Poses - two patterns
    IF Regex.IsMatch(entryPathLower, @"saves/person/pose/.*?\.(json|vac)$", RegexOptions.IgnoreCase) THEN
        RETURN ContentType.Pose
    END IF
    IF Regex.IsMatch(entryPathLower, @"custom/atom/person/pose/.*?\.(vap)$", RegexOptions.IgnoreCase) THEN
        RETURN ContentType.Pose
    END IF
    
    // Skin
    IF Regex.IsMatch(entryPathLower, @"custom/atom/person/skin/.*?\.(vap)$", RegexOptions.IgnoreCase) THEN
        RETURN ContentType.Skin
    END IF
    
    RETURN ContentType.Unknown
END FUNCTION

FUNCTION DetermineIfPreset(entryPath: string, contentType: ContentType): bool
BEGIN
    entryPathLower = entryPath.ToLower()
    
    SWITCH contentType
        CASE ContentType.Look:
            // JSON files are presets, VAC/VAP may or may not be
            RETURN entryPathLower.EndsWith(".json") OR entryPathLower.EndsWith(".vap")
            
        CASE ContentType.Clothing:
            // Only VAP files are presets
            RETURN entryPathLower.EndsWith(".vap")
            
        CASE ContentType.Hairstyle:
            // Only VAP files are presets
            RETURN entryPathLower.EndsWith(".vap")
            
        CASE ContentType.Morph:
            // Only VAP files are presets
            RETURN entryPathLower.EndsWith(".vap")
            
        CASE ContentType.Pose:
            // JSON and VAP files are presets
            RETURN entryPathLower.EndsWith(".json") OR entryPathLower.EndsWith(".vap")
            
        CASE ContentType.Skin:
            // VAP files are presets
            RETURN entryPathLower.EndsWith(".vap")
            
        CASE ContentType.Scene:
            // Scenes are always presets
            RETURN true
            
        DEFAULT:
            RETURN false
    END SWITCH
END FUNCTION

FUNCTION IncrementCount(counts: ContentCounts, contentType: ContentType)
BEGIN
    SWITCH contentType
        CASE ContentType.Scene:
            counts.Scenes++
        CASE ContentType.Look:
            counts.Looks++
        CASE ContentType.Clothing:
            counts.Clothing++
        CASE ContentType.Hairstyle:
            counts.Hairstyles++
        CASE ContentType.Script:
            counts.Scripts++
        CASE ContentType.ScriptList:
            counts.ScriptLists++
        CASE ContentType.Asset:
            counts.Assets++
        CASE ContentType.Morph:
            counts.Morphs++
        CASE ContentType.Pose:
            counts.Poses++
        CASE ContentType.Skin:
            counts.Skins++
    END SWITCH
END FUNCTION
```

**Input**:
- `varFilePath`: Full path to VAR file

**Output**:
- `ContentAnalysisResult` containing counts and detailed items

**Complexity**: O(n) where n is number of entries in ZIP

---

## 5. Preview Image Extraction Functions

### 5.1 ExtractPreviewImages

**Purpose**: Extracts preview images (JPG files) associated with content items from VAR file.

**Algorithm**:
```
FUNCTION ExtractPreviewImages(
    varFilePath: string,
    varPackageId: int,
    outputDirectory: string): List<PreviewImageInfo>
BEGIN
    previewImages = []
    
    TRY
        USING zipFile = ZipFile.OpenRead(varFilePath)
            // Get all content items that should have previews
            contentItems = AnalyzeVarContent(varFilePath).ContentItems
            
            FOR EACH contentItem IN contentItems
                IF ShouldHavePreview(contentItem.Type) THEN
                    previewPath = FindPreviewImagePath(zipFile, contentItem.Path)
                    
                    IF previewPath ≠ NULL THEN
                        previewImage = ExtractSinglePreviewImage(
                            zipFile: zipFile,
                            previewEntry: zipFile.GetEntry(previewPath),
                            contentItem: contentItem,
                            varPackageId: varPackageId,
                            outputDirectory: outputDirectory
                        )
                        
                        IF previewImage ≠ NULL THEN
                            previewImages.Add(previewImage)
                        END IF
                    END IF
                END IF
            END FOR
        END USING
        
        RETURN previewImages
    CATCH Exception AS ex
        THROW InvalidOperationException($"Failed to extract preview images: {ex.Message}", ex)
    END TRY
END FUNCTION

FUNCTION ShouldHavePreview(contentType: ContentType): bool
BEGIN
    previewTypes = [ContentType.Scene, ContentType.Look, ContentType.Clothing, 
                    ContentType.Hairstyle, ContentType.Asset, ContentType.Morph, 
                    ContentType.Pose, ContentType.Skin]
    
    RETURN previewTypes.Contains(contentType)
END FUNCTION

FUNCTION FindPreviewImagePath(zipFile: ZipArchive, contentPath: string): string
BEGIN
    // Preview image has same path as content item but with .jpg extension
    pathWithoutExtension = contentPath.Substring(0, contentPath.LastIndexOf('.'))
    previewPath = pathWithoutExtension + ".jpg"
    
    // Check if preview exists in ZIP
    IF zipFile.GetEntry(previewPath) ≠ NULL THEN
        RETURN previewPath
    END IF
    
    // Alternative: preview might be in same directory with different name
    directory = Path.GetDirectoryName(contentPath)
    fileName = Path.GetFileNameWithoutExtension(contentPath)
    
    // Try common preview naming patterns
    previewPatterns = [
        directory + "/" + fileName + ".jpg",
        directory + "/preview.jpg",
        directory + "/" + fileName + "_preview.jpg"
    ]
    
    FOR EACH pattern IN previewPatterns
        IF zipFile.GetEntry(pattern) ≠ NULL THEN
            RETURN pattern
        END IF
    END FOR
    
    RETURN NULL // No preview found
END FUNCTION

FUNCTION ExtractSinglePreviewImage(
    zipFile: ZipArchive,
    previewEntry: ZipArchiveEntry,
    contentItem: ContentItem,
    varPackageId: int,
    outputDirectory: string): PreviewImageInfo
BEGIN
    // Determine output path structure
    // Format: {outputDirectory}/{contentType}/{varPackageName}/{typePrefix}{count}_{originalName}.jpg
    contentTypeDir = contentItem.Type.ToString().ToLower()
    varPackageName = GetVarPackageName(varPackageId)
    
    outputBaseDir = Path.Combine(outputDirectory, contentTypeDir, varPackageName)
    Directory.CreateDirectory(outputBaseDir)
    
    // Generate unique filename
    count = GetContentTypeCount(varPackageId, contentItem.Type) + 1
    typePrefix = GetTypePrefix(contentItem.Type)
    originalName = Path.GetFileNameWithoutExtension(previewEntry.Name).ToLower()
    
    outputFileName = $"{typePrefix}{count:000}_{originalName}.jpg"
    outputPath = Path.Combine(outputBaseDir, outputFileName)
    
    // Extract if not already exists
    IF NOT File.Exists(outputPath) THEN
        USING inputStream = previewEntry.Open()
        USING outputStream = File.Create(outputPath)
            inputStream.CopyTo(outputStream)
        END USING
        END USING
    END IF
    
    RETURN PreviewImageInfo(
        ContentItemPath: contentItem.Path,
        ContentType: contentItem.Type,
        PreviewPath: outputPath,
        RelativePath: Path.GetRelativePath(outputDirectory, outputPath),
        FileName: outputFileName,
        Size: previewEntry.Length,
        Extracted: true
    )
END FUNCTION

FUNCTION GetTypePrefix(contentType: ContentType): string
BEGIN
    SWITCH contentType
        CASE ContentType.Scene: RETURN "scenes"
        CASE ContentType.Look: RETURN "looks"
        CASE ContentType.Clothing: RETURN "clothing"
        CASE ContentType.Hairstyle: RETURN "hairstyle"
        CASE ContentType.Asset: RETURN "assets"
        CASE ContentType.Morph: RETURN "morphs"
        CASE ContentType.Pose: RETURN "pose"
        CASE ContentType.Skin: RETURN "skin"
        DEFAULT: RETURN "unknown"
    END SWITCH
END FUNCTION
```

**Input**:
- `varFilePath`: VAR file path
- `varPackageId`: Database ID of VAR package
- `outputDirectory`: Base directory for extracted previews

**Output**:
- List of `PreviewImageInfo` with extraction details

---

---

## 6. Repository Organization Functions

### 6.1 OrganizeVarFiles (Multi-Repository)

**Purpose**: Organizes VAR files within a single repository. Handles local duplicates within the repository, but cross-repository duplicates are handled separately by DetectDuplicateVarFiles.

**Directory Structure**:
```
Repository/
├── {Creator}/
│   └── Creator.Package.Version.var
├── redundant/          (local duplicates within this repository)
│   └── Duplicate files
└── invalid/            (invalid/non-compliant files)
    └── Invalid files
```

**Algorithm**:
```
FUNCTION OrganizeVarFiles(
    repositoryId: int,
    varFiles: List<string>,
    allRepositories: List<Repository>,
    progressCallback: ProgressCallback): OrganizationResult
BEGIN
    result = OrganizationResult()
    repository = GetRepositoryById(repositoryId)
    
    tidyDir = Path.Combine(repository.Path, "tidied")
    redundantDir = Path.Combine(repository.Path, "redundant")
    invalidDir = Path.Combine(repository.Path, "invalid")
    
    // Ensure directories exist
    Directory.CreateDirectory(tidyDir)
    Directory.CreateDirectory(redundantDir)
    Directory.CreateDirectory(invalidDir)
    
    processed = 0
    total = varFiles.Count
    
    // Build map of existing files across all repositories for cross-repo duplicate checking
    existingFilesMap = BuildExistingFilesMap(allRepositories, excludeRepositoryId: repositoryId)
    
    FOR EACH varFile IN varFiles
        processed++
        progressCallback(processed, total, varFile)
        
        // Skip symlinks
        IF IsSymbolicLink(varFile) THEN
            CONTINUE
        END IF
        
        // Validate filename
        validation = ValidateVarFileName(varFile)
        
        IF NOT validation.IsValid THEN
            MoveToInvalid(varFile, invalidDir, result)
            CONTINUE
        END IF
        
        parsedName = validation.ParsedName
        varName = parsedName.FullName // e.g., "Creator.Package.Version"
        
        // Check if VAR name already exists in database (UNIQUE constraint)
        existingVarPackage = GetVarPackageByName(varName)
        
        IF existingVarPackage ≠ NULL THEN
            // VAR name already exists - UNIQUE constraint ensures only one record
            IF existingVarPackage.RepositoryId = repositoryId THEN
                // Same repository - update existing record if file changed
                IF existingVarPackage.FilePath = varFile THEN
                    // Same file - already indexed, skip organization
                    CONTINUE
                ELSE
                    // Different file path but same VAR name in same repository
                    // This shouldn't happen, but handle it
                    MoveToRedundant(varFile, redundantDir, result, 
                        reason: $"VAR name {varName} already indexed from different location in this repository")
                END IF
            ELSE
                // Different repository - VAR name already registered from another repository
                // Since VAR name is unique globally, we log this as conflict
                result.CrossRepositoryConflicts.Add(CrossRepositoryConflictInfo(
                    VarName: varName,
                    ExistingRepositoryId: existingVarPackage.RepositoryId,
                    ExistingRepositoryName: GetRepositoryName(existingVarPackage.RepositoryId),
                    ExistingFilePath: existingVarPackage.FilePath,
                    NewRepositoryId: repositoryId,
                    NewFilePath: varFile,
                    Warning: $"VAR {varName} already exists in repository {existingVarPackage.RepositoryId}. Current file will not be indexed (UNIQUE constraint)."
                ))
                // Don't move - just skip indexing this file
                LogWarning($"VAR {varName} skipped - already exists in repository {existingVarPackage.RepositoryId}")
                CONTINUE
            END IF
        END IF
        
        // Determine destination path within this repository
        creatorDir = Path.Combine(tidyDir, parsedName.Creator)
        Directory.CreateDirectory(creatorDir)
        
        destinationPath = Path.Combine(creatorDir, Path.GetFileName(varFile))
        
        // Check for local duplicate (same repository)
        IF File.Exists(destinationPath) THEN
            existingHash = ComputeFileHash(destinationPath)
            
            IF existingHash = fileHash THEN
                // Same file content - move source to redundant
                MoveToRedundant(varFile, redundantDir, result, reason: "Identical file already exists in tidy directory")
            ELSE
                // Different file with same name - this should not happen after validation
                // But handle it by renaming
                LogWarning($"File {varFile} has same name as existing but different content. Renaming.")
                destinationPath = GenerateUniqueFileName(destinationPath)
                MoveFile(varFile, destinationPath, result)
            END IF
        ELSE
            // No local duplicate, move to tidy directory
            MoveFile(varFile, destinationPath, result)
        END IF
    END FOR
    
    RETURN result
END FUNCTION

FUNCTION GetVarPackageByName(varName: string): VarPackage
BEGIN
    // Simple database query - VAR name is unique key (indexed)
    RETURN QuerySingle(
        "SELECT * FROM var_packages WHERE var_name = @varName",
        new { varName }
    )
END FUNCTION

FUNCTION MoveToInvalid(filePath: string, invalidDir: string, result: OrganizationResult)
BEGIN
    fileName = Path.GetFileName(filePath)
    destination = Path.Combine(invalidDir, fileName)
    destination = GenerateUniqueFileName(destination)
    
    TRY
        File.Move(filePath, destination)
        result.InvalidFiles.Add(filePath)
        result.Log($"Moved invalid file: {fileName}")
    CATCH Exception AS ex
        result.Errors.Add($"Failed to move invalid file {fileName}: {ex.Message}")
    END TRY
END FUNCTION

FUNCTION MoveToRedundant(filePath: string, redundantDir: string, result: OrganizationResult)
BEGIN
    fileName = Path.GetFileName(filePath)
    destination = Path.Combine(redundantDir, fileName)
    destination = GenerateUniqueFileName(destination)
    
    TRY
        File.Move(filePath, destination)
        result.RedundantFiles.Add(filePath)
        result.Log($"Moved redundant file: {fileName}")
    CATCH Exception AS ex
        result.Errors.Add($"Failed to move redundant file {fileName}: {ex.Message}")
    END TRY
END FUNCTION

FUNCTION GenerateUniqueFileName(filePath: string): string
BEGIN
    IF NOT File.Exists(filePath) THEN
        RETURN filePath
    END IF
    
    directory = Path.GetDirectoryName(filePath)
    fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath)
    extension = Path.GetExtension(filePath)
    
    counter = 1
    WHILE TRUE
        newFileName = $"{fileNameWithoutExt}({counter}){extension}"
        newPath = Path.Combine(directory, newFileName)
        
        IF NOT File.Exists(newPath) THEN
            RETURN newPath
        END IF
        
        counter++
    END WHILE
END FUNCTION

FUNCTION FilesAreIdentical(file1: string, file2: string): bool
BEGIN
    fileInfo1 = FileInfo(file1)
    fileInfo2 = FileInfo(file2)
    
    // Quick check: file size
    IF fileInfo1.Length ≠ fileInfo2.Length THEN
        RETURN false
    END IF
    
    // Compare modification time
    IF Math.Abs((fileInfo1.LastWriteTime - fileInfo2.LastWriteTime).TotalSeconds) > 2 THEN
        RETURN false
    END IF
    
    // Compare file hashes (optional but more accurate)
    hash1 = ComputeFileHash(file1)
    hash2 = ComputeFileHash(file2)
    
    RETURN hash1 = hash2
END FUNCTION
```

**Input**:
- `repositoryPath`: Base repository path
- `varFiles`: List of VAR file paths to organize
- `progressCallback`: Progress reporting callback

**Output**:
- `OrganizationResult` with statistics and moved files

---

### 6.2 DetectDuplicateVarFiles (Simplified - VAR Name as Unique Key)

**Purpose**: Detects duplicate VAR files across multiple repositories. Since VAR name is globally unique (game requirement), we simplify detection by checking VAR names only. The database enforces UNIQUE constraint on `var_name`.

**Key Principle**: VAR name is the unique identifier. If same VAR name exists in multiple repositories, we need to determine which one to use (primary).

**Algorithm**:
```
FUNCTION DetectDuplicateVarFiles(
    repositories: List<Repository>,
    detectionOptions: DuplicateDetectionOptions): DuplicateDetectionResult
BEGIN
    result = DuplicateDetectionResult()
    
    // Build map by VAR name (which is unique identifier)
    varNameMap = Dictionary<string, List<VarFileInfo>>() // Key: VarName (e.g., "Creator.Package.Version")
    
    // Scan all repositories and group by VAR name
    FOR EACH repository IN repositories
        IF NOT repository.Enabled THEN
            CONTINUE
        END IF
        
        // Get all VAR packages from database for this repository
        varPackages = GetVarPackagesByRepository(repository.Id)
        
        FOR EACH varPackage IN varPackages
            varName = varPackage.VarName // e.g., "Creator.Package.1.var"
            
            IF NOT varNameMap.ContainsKey(varName) THEN
                varNameMap[varName] = []
            END IF
            
            varNameMap[varName].Add(VarFileInfo(
                VarPackageId: varPackage.Id,
                Path: varPackage.FilePath,
                RepositoryId: repository.Id,
                RepositoryName: repository.Name,
                RepositoryPriority: repository.Priority,
                VarName: varName,
                FileHash: varPackage.FileHash,
                Size: varPackage.FileSize,
                LastModified: varPackage.FileModifiedAt
            ))
        END FOR
    END FOR
    
    // Find VAR names with multiple copies
    FOR EACH varName IN varNameMap.Keys
        files = varNameMap[varName]
        
        IF files.Count > 1 THEN
            // Same VAR name exists in multiple repositories
            primaryFile = DeterminePrimaryFile(files, detectionOptions.SelectionStrategy)
            
            duplicateGroup = DuplicateGroup(
                Type: DuplicateType.FilenameDuplicate,
                VarName: varName,
                PrimaryFile: primaryFile,
                DuplicateFiles: files.Where(f => f.Path ≠ primaryFile.Path).ToList(),
                TotalWastedSpace: (files.Count - 1) * files[0].Size
            )
            
            result.Duplicates.Add(duplicateGroup)
        END IF
    END FOR
    
    // Optional: Detect content duplicates (same hash, different names) - rare case
    IF detectionOptions.DetectContentDuplicates THEN
        hashMap = Dictionary<string, List<VarFileInfo>>()
        
        // Build hash map
        FOR EACH varName IN varNameMap.Keys
            FOR EACH file IN varNameMap[varName]
                IF file.FileHash IS NOT NULL THEN
                    IF NOT hashMap.ContainsKey(file.FileHash) THEN
                        hashMap[file.FileHash] = []
                    END IF
                    hashMap[file.FileHash].Add(file)
                END IF
            END FOR
        END FOR
        
        // Find content duplicates (same hash, different names)
        FOR EACH hash IN hashMap.Keys
            files = hashMap[hash]
            
            IF files.Count > 1 THEN
                varNames = files.Select(f => f.VarName).Distinct().ToList()
                
                IF varNames.Count > 1 THEN
                    // Same content, different VAR names (rare but possible)
                    duplicateGroup = DuplicateGroup(
                        Type: DuplicateType.ContentDuplicate,
                        VarName: null, // Multiple names
                        PrimaryFile: files[0], // First one
                        DuplicateFiles: files.Skip(1).ToList(),
                        TotalWastedSpace: (files.Count - 1) * files[0].Size,
                        AlternativeNames: varNames
                    )
                    
                    result.ContentDuplicates.Add(duplicateGroup)
                END IF
            END IF
        END FOR
    END IF
    
    // Calculate statistics
    result.TotalDuplicates = result.Duplicates.Count + result.ContentDuplicates.Count
    result.TotalWastedSpace = result.Duplicates.Sum(d => d.TotalWastedSpace) + 
                              result.ContentDuplicates.Sum(d => d.TotalWastedSpace)
    
    RETURN result
END FUNCTION

FUNCTION DeterminePrimaryFile(
    files: List<VarFileInfo>,
    strategy: PrimaryFileSelectionStrategy): VarFileInfo
BEGIN
    SWITCH strategy
        CASE PrimaryFileSelectionStrategy.HighestPriorityRepository:
            // Sort by repository priority (higher = better)
            RETURN files.OrderByDescending(f => f.RepositoryPriority).ThenByDescending(f => f.LastModified).First()
            
        CASE PrimaryFileSelectionStrategy.NewestFile:
            // Most recently modified
            RETURN files.OrderByDescending(f => f.LastModified).First()
            
        CASE PrimaryFileSelectionStrategy.OldestFile:
            // Oldest file (might be original)
            RETURN files.OrderBy(f => f.LastModified).First()
            
        CASE PrimaryFileSelectionStrategy.LargestFile:
            // Largest file size
            RETURN files.OrderByDescending(f => f.Size).First()
            
        CASE PrimaryFileSelectionStrategy.SmallestFile:
            // Smallest file size
            RETURN files.OrderBy(f => f.Size).First()
            
        CASE PrimaryFileSelectionStrategy.FirstFound:
            // First file in list (order of scanning)
            RETURN files.First()
            
        DEFAULT:
            RETURN files.OrderByDescending(f => f.RepositoryPriority).ThenByDescending(f => f.LastModified).First()
    END SWITCH
END FUNCTION
```

**Primary File Selection Strategies**:
- `HighestPriorityRepository`: File from repository with highest priority
- `NewestFile`: Most recently modified file
- `OldestFile`: Oldest file (might be original)
- `LargestFile`: Largest file size
- `SmallestFile`: Smallest file size
- `FirstFound`: First file encountered during scan

---

### 6.3 ResolveDuplicateVarFile (Simplified)

**Purpose**: Resolve which VAR file to use when multiple copies exist. Since VAR name is unique, we simply query by VAR name from database and apply selection strategy if multiple copies exist.

**Algorithm**:
```
FUNCTION ResolveDuplicateVarFile(
    varName: string,
    options: DuplicateResolutionOptions): Result<VarPackageInfo>
BEGIN
    // Query database - VAR name is unique key, but may exist in multiple repositories
    varPackages = GetVarPackagesByName(varName)
    
    IF varPackages.Count = 0 THEN
        RETURN Result.Failure("VAR package not found", ErrorCode.VarPackageNotFound)
    END IF
    
    IF varPackages.Count = 1 THEN
        RETURN Result.Success(varPackages[0])
    END IF
    
    // Multiple copies found in different repositories
    // Apply selection strategy to determine primary
    primaryVarPackage = DeterminePrimaryVarPackage(varPackages, options.SelectionStrategy)
    
    // Check if files have different content (different hash) - should not happen but check anyway
    hashes = varPackages.Select(v => v.FileHash).Distinct().Where(h => h ≠ NULL).ToList()
    IF hashes.Count > 1 THEN
        // Same VAR name but different content - warning
        IF options.StrictMode THEN
            RETURN Result.Failure(
                $"VAR {varName} exists in multiple repositories with different content. Hashes: {string.Join(", ", hashes)}",
                ErrorCode.DuplicateContentMismatch
            )
        ELSE
            LogWarning($"VAR {varName} exists with different content. Using: {primaryVarPackage.FilePath}")
        END IF
    END IF
    
    RETURN Result.Success(primaryVarPackage)
END FUNCTION

FUNCTION GetVarPackagesByName(varName: string): List<VarPackageInfo>
BEGIN
    // Simple database query - VAR name is indexed
    RETURN Query(
        "SELECT * FROM var_packages WHERE var_name = @varName",
        new { varName }
    ).ToList()
END FUNCTION

FUNCTION DeterminePrimaryVarPackage(
    varPackages: List<VarPackageInfo>,
    strategy: PrimaryFileSelectionStrategy): VarPackageInfo
BEGIN
    repositories = GetRepositoriesByIds(varPackages.Select(v => v.RepositoryId).ToList())
    
    // Enrich with repository priority
    enrichedPackages = varPackages.Select(vp => new {
        VarPackage: vp,
        Repository: repositories.First(r => r.Id = vp.RepositoryId)
    }).ToList()
    
    SWITCH strategy
        CASE PrimaryFileSelectionStrategy.HighestPriorityRepository:
            RETURN enrichedPackages
                .OrderByDescending(ep => ep.Repository.Priority)
                .ThenByDescending(ep => ep.VarPackage.FileModifiedAt)
                .First().VarPackage
            
        CASE PrimaryFileSelectionStrategy.NewestFile:
            RETURN varPackages.OrderByDescending(vp => vp.FileModifiedAt).First()
            
        CASE PrimaryFileSelectionStrategy.OldestFile:
            RETURN varPackages.OrderBy(vp => vp.FileModifiedAt).First()
            
        CASE PrimaryFileSelectionStrategy.LargestFile:
            RETURN varPackages.OrderByDescending(vp => vp.FileSize).First()
            
        CASE PrimaryFileSelectionStrategy.SmallestFile:
            RETURN varPackages.OrderBy(vp => vp.FileSize).First()
            
        DEFAULT:
            RETURN enrichedPackages
                .OrderByDescending(ep => ep.Repository.Priority)
                .ThenByDescending(ep => ep.VarPackage.FileModifiedAt)
                .First().VarPackage
    END SWITCH
END FUNCTION
```

---

### 6.4 MarkDuplicateVarFiles

**Purpose**: After detecting duplicates, mark them in database for tracking and user decision.

**Algorithm**:
```
FUNCTION MarkDuplicateVarFiles(
    duplicateGroups: List<DuplicateGroup>,
    options: DuplicateMarkingOptions): Result
BEGIN
    BEGIN TRANSACTION
    TRY
        FOR EACH duplicateGroup IN duplicateGroups
            primaryVarPackage = GetVarPackageByPath(duplicateGroup.PrimaryFile.Path)
            
            IF primaryVarPackage IS NULL THEN
                CONTINUE // Skip if not in database yet
            END IF
            
            // Mark primary file
            IF options.MarkPrimary THEN
                primaryVarPackage.IsPrimaryDuplicate = true
                primaryVarPackage.DuplicateGroupId = duplicateGroup.Id
                UpdateVarPackage(primaryVarPackage)
            END IF
            
            // Mark duplicate files
            FOR EACH duplicateFile IN duplicateGroup.DuplicateFiles
                duplicateVarPackage = GetVarPackageByPath(duplicateFile.Path)
                
                IF duplicateVarPackage IS NULL THEN
                    CONTINUE
                END IF
                
                duplicateVarPackage.IsDuplicate = true
                duplicateVarPackage.IsPrimaryDuplicate = false
                duplicateVarPackage.DuplicateGroupId = duplicateGroup.Id
                duplicateVarPackage.PrimaryVarPackageId = primaryVarPackage.Id
                UpdateVarPackage(duplicateVarPackage)
            END FOR
            
            // Create duplicate group record
            duplicateGroupRecord = DuplicateGroupRecord(
                Id: duplicateGroup.Id,
                Type: duplicateGroup.Type,
                VarName: duplicateGroup.VarName,
                PrimaryVarPackageId: primaryVarPackage.Id,
                TotalWastedSpace: duplicateGroup.TotalWastedSpace,
                DetectedAt: DateTime.UtcNow
            )
            SaveDuplicateGroup(duplicateGroupRecord)
        END FOR
        
        COMMIT TRANSACTION
        RETURN Result.Success()
        
    CATCH Exception AS ex
        ROLLBACK TRANSACTION
        RETURN Result.Failure($"Failed to mark duplicates: {ex.Message}", ErrorCode.DatabaseError)
    END TRY
END FUNCTION
```

---

### 6.5 HandleDuplicateDuringInstallation

**Purpose**: When installing a VAR, handle case where duplicate exists and user needs to choose which one to use.

**Algorithm**:
```
FUNCTION HandleDuplicateDuringInstallation(
    varName: string,
    installationTargetId: int,
    options: InstallationOptions): Result<InstallationInfo>
BEGIN
    // Resolve which VAR package to use (database query by unique VAR name)
    resolutionResult = ResolveDuplicateVarFile(varName, options.DuplicateResolution)
    
    IF NOT resolutionResult.IsSuccess THEN
        RETURN Result.Failure(resolutionResult.Error, resolutionResult.ErrorCode)
    END IF
    
    primaryVarPackage = resolutionResult.Value
    
    // Check if user preference exists
    userPreference = GetUserPreferenceForDuplicate(varName)
    
    IF userPreference ≠ NULL THEN
        preferredVarPackage = GetVarPackageById(userPreference.VarPackageId)
        
        IF preferredVarPackage ≠ NULL AND File.Exists(preferredVarPackage.FilePath) THEN
            // Use user preference
            targetVarPackage = preferredVarPackage
        ELSE
            // Preference no longer valid, use primary
            targetVarPackage = primaryVarPackage
            LogWarning($"User preference for {varName} no longer valid, using primary file")
        END IF
    ELSE
        targetVarPackage = primaryVarPackage
    END IF
    
    // Install from selected VAR package
    RETURN InstallVarPackage(targetVarPackage.Id, installationTargetId, options)
END FUNCTION

FUNCTION GetUserPreferenceForDuplicate(varName: string): UserPreference
BEGIN
    // Check if user has explicitly chosen which duplicate to use
    preference = QueryUserPreferences()
        .Where(p => p.Key = $"DuplicatePreference.{varName}")
        .FirstOrDefault()
    
    IF preference ≠ NULL THEN
        filePath = preference.Value
        RETURN UserPreference(FilePath: filePath, RepositoryId: GetRepositoryIdFromPath(filePath))
    END IF
    
    RETURN NULL
END FUNCTION
```

---

## 7. Installation Functions

### 7.1 InstallVarPackage

**Purpose**: Installs a VAR package by creating symbolic link to installation target.

**Algorithm**:
```
FUNCTION InstallVarPackage(
    varPackageId: int,
    installationTargetId: int,
    options: InstallationOptions): Result<InstallationInfo>
BEGIN
    // Get VAR package
    varPackage = GetVarPackageById(varPackageId)
    IF varPackage IS NULL THEN
        RETURN Result.Failure("VAR package not found", ErrorCode.VarPackageNotFound)
    END IF
    
    // Get installation target
    target = GetInstallationTargetById(installationTargetId)
    IF target IS NULL THEN
        RETURN Result.Failure("Installation target not found", ErrorCode.InstallationTargetNotFound)
    END IF
    
    // Check if already installed
    existingInstallation = GetInstallation(varPackageId, installationTargetId)
    IF existingInstallation IS NOT NULL AND existingInstallation.IsEnabled THEN
        RETURN Result.Failure("VAR package already installed", ErrorCode.AlreadyInstalled)
    END IF
    
    // Verify VAR file exists
    IF NOT File.Exists(varPackage.FilePath) THEN
        RETURN Result.Failure("VAR file not found", ErrorCode.FileNotFound)
    END IF
    
    // Create symlink path
    symlinkPath = Path.Combine(target.Path, varPackage.VarName + ".var")
    
    // Handle existing symlink (disabled or broken)
    IF File.Exists(symlinkPath) THEN
        IF IsSymbolicLink(symlinkPath) THEN
            // Remove existing symlink
            DeleteSymbolicLink(symlinkPath)
        ELSE
            // Regular file exists - error
            RETURN Result.Failure("File exists at symlink location", ErrorCode.PathConflict)
        END IF
    END IF
    
    IF File.Exists(symlinkPath + ".disabled") THEN
        File.Delete(symlinkPath + ".disabled")
    END IF
    
    // Create symbolic link
    TRY
        CreateSymbolicLink(
            linkPath: symlinkPath,
            targetPath: varPackage.FilePath,
            isDirectory: false
        )
    CATCH Exception AS ex
        RETURN Result.Failure($"Failed to create symlink: {ex.Message}", ErrorCode.SymlinkCreationFailed)
    END TRY
    
    // Install dependencies if requested
    IF options.InstallDependencies THEN
        dependencyResult = InstallDependencies(varPackage, installationTargetId, options)
        IF NOT dependencyResult.IsSuccess THEN
            // Log warning but continue
            LogWarning($"Some dependencies failed to install: {dependencyResult.Error}")
        END IF
    END IF
    
    // Create/update installation record
    installation = CreateOrUpdateInstallation(
        VarPackageId: varPackageId,
        InstallationTargetId: installationTargetId,
        SymlinkPath: symlinkPath,
        IsEnabled: true,
        InstalledAt: DateTime.UtcNow
    )
    
    RETURN Result.Success(InstallationInfo(installation))
END FUNCTION

FUNCTION InstallDependencies(
    varPackage: VarPackage,
    targetId: int,
    options: InstallationOptions): Result
BEGIN
    dependencies = GetDependencies(varPackage.Id)
    unresolvedDependencies = []
    
    FOR EACH dependency IN dependencies
        IF dependency.IsOptional AND NOT options.InstallOptionalDependencies THEN
            CONTINUE
        END IF
        
        resolvedVarPackage = ResolveDependency(dependency)
        
        IF resolvedVarPackage IS NULL THEN
            unresolvedDependencies.Add(dependency)
            CONTINUE
        END IF
        
        // Recursively install dependency
        installResult = InstallVarPackage(
            resolvedVarPackage.Id,
            targetId,
            options
        )
        
        IF NOT installResult.IsSuccess THEN
            unresolvedDependencies.Add(dependency)
        END IF
    END FOR
    
    IF unresolvedDependencies.Count > 0 THEN
        RETURN Result.Failure(
            $"Failed to install {unresolvedDependencies.Count} dependencies",
            ErrorCode.DependencyUnresolved
        )
    END IF
    
    RETURN Result.Success()
END FUNCTION
```

**Input**:
- `varPackageId`: Database ID of VAR package
- `installationTargetId`: Database ID of installation target
- `options`: Installation options (install dependencies, etc.)

**Output**:
- `Result<InstallationInfo>` with installation details

---

### 7.2 CreateSymbolicLink

**Purpose**: Creates a Windows symbolic link (cross-drive support).

**Algorithm**:
```
FUNCTION CreateSymbolicLink(
    linkPath: string,
    targetPath: string,
    isDirectory: bool): Result
BEGIN
    // Ensure target exists
    IF isDirectory THEN
        IF NOT Directory.Exists(targetPath) THEN
            RETURN Result.Failure("Target directory does not exist", ErrorCode.DirectoryNotFound)
        END IF
    ELSE
        IF NOT File.Exists(targetPath) THEN
            RETURN Result.Failure("Target file does not exist", ErrorCode.FileNotFound)
        END IF
    END IF
    
    // Ensure link directory exists
    linkDirectory = Path.GetDirectoryName(linkPath)
    IF NOT Directory.Exists(linkDirectory) THEN
        TRY
            Directory.CreateDirectory(linkDirectory)
        CATCH Exception AS ex
            RETURN Result.Failure($"Failed to create link directory: {ex.Message}", ErrorCode.DirectoryCreationFailed)
        END TRY
    END IF
    
    // Convert to absolute paths
    absoluteTargetPath = Path.GetFullPath(targetPath)
    absoluteLinkPath = Path.GetFullPath(linkPath)
    
    // Use Windows API CreateSymbolicLink
    flags = IF isDirectory THEN SYMBOLIC_LINK_FLAG_DIRECTORY ELSE SYMBOLIC_LINK_FLAG_FILE
    flags = flags OR SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE
    
    TRY
        success = Kernel32.CreateSymbolicLink(absoluteLinkPath, absoluteTargetPath, flags)
        
        IF NOT success THEN
            errorCode = Marshal.GetLastWin32Error()
            RETURN Result.Failure(
                $"Failed to create symlink. Error code: {errorCode}",
                ErrorCode.SymlinkCreationFailed
            )
        END IF
        
        RETURN Result.Success()
    CATCH Exception AS ex
        RETURN Result.Failure($"Exception creating symlink: {ex.Message}", ErrorCode.SymlinkCreationFailed)
    END TRY
END FUNCTION
```

**Platform Note**: Uses Windows API `CreateSymbolicLinkW` via P/Invoke

---

## 8. Repository Scanning Functions

### 8.1 ScanRepository

**Purpose**: Scans a repository for VAR files, detects changes, and updates database.

**Algorithm**:
```
FUNCTION ScanRepository(
    repositoryId: int,
    scanOptions: ScanOptions,
    progressCallback: ProgressCallback,
    cancellationToken: CancellationToken): Result<ScanResult>
BEGIN
    repository = GetRepositoryById(repositoryId)
    IF repository IS NULL THEN
        RETURN Result.Failure("Repository not found", ErrorCode.RepositoryNotFound)
    END IF
    
    IF NOT Directory.Exists(repository.Path) THEN
        RETURN Result.Failure("Repository path does not exist", ErrorCode.DirectoryNotFound)
    END IF
    
    scanResult = ScanResult(RepositoryId: repositoryId, StartTime: DateTime.UtcNow)
    
    TRY
        // Get existing VAR files in database
        existingVarFiles = GetVarFilesByRepository(repositoryId)
        existingFilePaths = Set(existingVarFiles.Select(v => v.FilePath))
        
        // Scan file system
        varFiles = ScanFileSystemForVarFiles(repository.Path, cancellationToken)
        scanResult.FilesScanned = varFiles.Count
        
        // Process each VAR file
        processed = 0
        FOR EACH varFile IN varFiles
            IF cancellationToken.IsCancellationRequested THEN
                scanResult.Status = ScanStatus.Cancelled
                RETURN Result.Success(scanResult)
            END IF
            
            processed++
            progressCallback(processed, varFiles.Count, varFile.Path)
            
            // Check if file is new or modified
            IF existingFilePaths.Contains(varFile.Path) THEN
                existingVar = existingVarFiles.First(v => v.FilePath = varFile.Path)
                
                // Check if file was modified
                IF varFile.LastWriteTime > existingVar.FileModifiedAt THEN
                    UpdateVarPackage(existingVar.Id, varFile, scanResult)
                ELSE
                    scanResult.FilesUnchanged++
                END IF
            ELSE
                // New file
                AddVarPackage(repositoryId, varFile, scanResult)
                scanResult.FilesAdded++
            END IF
        END FOR
        
        // Find deleted files (in DB but not in file system)
        FOR EACH existingVar IN existingVarFiles
            IF NOT varFiles.Any(v => v.Path = existingVar.FilePath) THEN
                MarkVarPackageAsDeleted(existingVar.Id, scanResult)
                scanResult.FilesDeleted++
            END IF
        END FOR
        
        scanResult.EndTime = DateTime.UtcNow
        scanResult.Duration = scanResult.EndTime - scanResult.StartTime
        scanResult.Status = ScanStatus.Completed
        
        RETURN Result.Success(scanResult)
        
    CATCH OperationCanceledException
        scanResult.Status = ScanStatus.Cancelled
        RETURN Result.Success(scanResult)
    CATCH Exception AS ex
        scanResult.Status = ScanStatus.Failed
        scanResult.Error = ex.Message
        RETURN Result.Failure($"Scan failed: {ex.Message}", ErrorCode.ScanFailed)
    END TRY
END FUNCTION

FUNCTION ScanFileSystemForVarFiles(
    repositoryPath: string,
    cancellationToken: CancellationToken): List<ScannedVarFile>
BEGIN
    varFiles = []
    
    // Get all .var files recursively, excluding special directories
    excludeDirs = ["redundant", "invalid", "tidied", "stale", "old_version", "deleted"]
    
    allFiles = Directory.GetFiles(
        repositoryPath,
        "*.var",
        SearchOption.AllDirectories
    )
    
    FOR EACH filePath IN allFiles
        IF cancellationToken.IsCancellationRequested THEN
            BREAK
        END IF
        
        // Skip excluded directories
        relativePath = Path.GetRelativePath(repositoryPath, filePath)
        pathParts = relativePath.Split(Path.DirectorySeparatorChar)
        
        IF excludeDirs.Any(dir => pathParts.Contains(dir)) THEN
            CONTINUE
        END IF
        
        // Skip symlinks
        IF IsSymbolicLink(filePath) THEN
            CONTINUE
        END IF
        
        fileInfo = FileInfo(filePath)
        
        varFiles.Add(ScannedVarFile(
            Path: filePath,
            Size: fileInfo.Length,
            LastWriteTime: fileInfo.LastWriteTime,
            CreatedTime: fileInfo.CreationTime
        ))
    END FOR
    
    RETURN varFiles
END FUNCTION
```

**Input**:
- `repositoryId`: Repository ID to scan
- `scanOptions`: Scan configuration options
- `progressCallback`: Progress reporting
- `cancellationToken`: Cancellation support

**Output**:
- `Result<ScanResult>` with scan statistics

---

## 9. File Hashing Functions

### 9.1 ComputeFileHash

**Purpose**: Computes SHA-256 hash of a file for duplicate detection and integrity verification.

**Algorithm**:
```
FUNCTION ComputeFileHash(filePath: string, algorithm: HashAlgorithm = SHA256): string
BEGIN
    IF NOT File.Exists(filePath) THEN
        THROW FileNotFoundException($"File not found: {filePath}")
    END IF
    
    TRY
        USING stream = File.OpenRead(filePath)
        USING hashAlgorithm = algorithm.Create()
            hashBytes = hashAlgorithm.ComputeHash(stream)
            RETURN Convert.ToHexString(hashBytes).ToLower()
        END USING
        END USING
    CATCH IOException AS ex
        THROW InvalidOperationException($"Failed to read file for hashing: {ex.Message}", ex)
    END TRY
END FUNCTION

FUNCTION ComputeFileHashAsync(
    filePath: string,
    algorithm: HashAlgorithm = SHA256,
    cancellationToken: CancellationToken): Task<string>
BEGIN
    IF NOT File.Exists(filePath) THEN
        THROW FileNotFoundException($"File not found: {filePath}")
    END IF
    
    TRY
        USING stream = File.OpenRead(filePath)
        USING hashAlgorithm = algorithm.Create()
            hashBytes = await hashAlgorithm.ComputeHashAsync(stream, cancellationToken)
            RETURN Convert.ToHexString(hashBytes).ToLower()
        END USING
        END USING
    CATCH IOException AS ex
        THROW InvalidOperationException($"Failed to read file for hashing: {ex.Message}", ex)
    END TRY
END FUNCTION
```

**Performance Note**: For large files, use async version with buffer size optimization

---

## Summary

This document covers all major functions with detailed algorithms. Additional helper functions and utilities will be implemented following these patterns.

**Key Design Principles**:
1. **Error Handling**: Always return Result<T> for business logic, throw exceptions only for unexpected errors
2. **Async/Await**: Use async for all I/O operations
3. **Cancellation**: Support CancellationToken for long-running operations
4. **Progress Reporting**: Provide progress callbacks for user feedback
5. **Validation**: Validate inputs early and return clear error messages

---

## Next Steps

Continue implementation based on these specifications, ensuring:
- All functions follow the documented algorithms
- Error handling is consistent
- Performance optimizations are applied where needed
- Tests are written for each function

