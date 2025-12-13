# Phase 4: LoadScene Component

## Overview

**LoadScene** is a Unity plugin library designed to load various resources into Virt-a-Mate scenes. It primarily handles MMD (MikuMikuDance) model and motion file loading, timeline integration, and scene resource management. The component is compiled as a .NET Framework 3.5 library for Unity compatibility.

## Project Configuration

- **Framework**: .NET Framework 3.5
- **Output Type**: Library (DLL)
- **Platform**: AnyCPU
- **Unity Compatibility**: VaM 1.20.77.9
- **Build Tool**: ILMerge (for assembly merging)

## Architecture

### Core Components

#### 1. loadscene.cs (Main Plugin Entry)

**Namespace**: `MVRPlugin`

**Base Class**: `MVRScript` (VaM plugin base class)

**Key Responsibilities**:
- Monitors for `loadscene.json` file
- Parses JSON configuration
- Coordinates resource loading
- Manages MMD person atoms
- Handles scene initialization

**Main Loop**:
```csharp
void Update()
{
    StartCoroutine(LoopWaitLoadFile());
}
```

The plugin continuously checks for the JSON file in a coroutine.

#### 2. MMD Processing Library (LibMMD)

Complete MMD file format support:

**Model Parsing**:
- `ModelReader2.cs`: Reads PMX/PMD model files
- `PmxReader2.cs`: PMX format parser
- Model structures: Bones, Morphs, Materials, Rigid Bodies

**Motion Parsing**:
- `VmdReader2.cs`: VMD motion file parser
- `MotionPlayer.cs`: Motion playback controller
- `BoneKeyframe.cs`, `MorphKeyframe.cs`: Keyframe structures
- `CameraKeyframe.cs`: Camera animation support

**Unity Integration**:
- `MmdGameObject.cs`: Unity GameObject wrapper for MMD models
- `MmdCameraObject.cs`: Camera animation handler
- `Utils.cs`: Unity-specific utilities

#### 3. Timeline Integration

**TimelineJson.cs**: Timeline data structure
- Clips, controls, frames
- Integration with VaM timeline system

**Components**:
- `TimelineClipJson.cs`: Clip definitions
- `TimelineControlJson.cs`: Control parameters
- `TimelineFrameJson.cs`: Frame data

#### 4. Bone Mapping & Morphs

**DazBoneMapping.cs**: Maps MMD bones to Daz3D/VaM bone structure

**Morph Support**:
- `FaceMorph.cs`: Facial expression morphs
- `FingerMorph.cs`: Finger morphs
- `FloatParamsJson.cs`: Parameter mapping

#### 5. Joint Physics

**ConfigurableJointExtensions.cs**: Unity joint extensions for MMD physics
- Rigid body constraints
- Physics simulation setup

### Resource Types

The plugin supports loading various resource types:

1. **personvmd**: Person/character motion VMD
   - Bone motions
   - Face morphs
   - Physics setup

2. **cameravmd**: Camera motion VMD
   - Camera position/rotation
   - Field of view changes
   - Timeline integration

3. **audio**: Audio file loading
   - MP3/WAV support
   - Timeline synchronization

4. **highheel**: High heel physics
   - Foot angle adjustments
   - Physics constraints

5. **empytscene**: Empty scene initialization
   - Clean scene creation

## File Processing

### JSON Configuration

**Location**: `Custom\PluginData\feelfar\loadscene.json`

**Processing Flow**:
1. Plugin checks for file existence in Update loop
2. If found, reads and parses JSON
3. Processes each resource in resources array
4. Deletes JSON file after processing
5. Optionally rescans packages if requested

### MMD File Parsing

**PMX Model Files**:
- Vertex data
- Bone hierarchy
- Material definitions
- Texture mappings
- Morph targets

**VMD Motion Files**:
- Bone keyframes
- Morph keyframes
- Camera keyframes
- Interpolation curves

### Encoding Handling

**SJISToUnicode.cs**: Handles Japanese character encoding
- Shift-JIS to Unicode conversion
- File name encoding detection

## Unity Integration

### Atom Management

**VamTools.cs**: VaM-specific utilities
- Atom creation
- Control management
- Plugin interaction

### Transform Utilities

**TransformFindEx.cs**: Extended transform finding
- Recursive search
- Path-based lookup

### File Management

Uses VaM's secure file management:
- `FileManagerSecure`: Secure file access
- Path validation
- Security restrictions

## Physics Integration

### Rigid Body Setup

- Configures Unity physics joints
- Applies MMD rigid body constraints
- Handles collision detection

### Bone Physics

- Bone rotation constraints
- Physics-driven animation
- IK (Inverse Kinematics) support

## Timeline Integration

### Motion to Timeline

Converts MMD motions to VaM timeline:
- Keyframe extraction
- Animation curve generation
- Timeline clip creation
- Synchronization with audio

### Camera Animation

- Camera keyframe processing
- Smooth interpolation
- Timeline integration

## Code Organization

### Directory Structure

```
src/
  loadscene.cs                 # Main plugin entry
  LibMMD/
    Model/                     # Model structures
    Motion/                    # Motion structures
    Reader/                    # File parsers
    Unity3D/                   # Unity integration
    Util/                      # Utilities
  TimelineJson.cs              # Timeline structures
  AssetBoneProcess.cs          # Bone processing
  DazBoneMapping.cs            # Bone mapping
  FaceMorph.cs                 # Face morphs
  FingerMorph.cs               # Finger morphs
  VamTools.cs                  # VaM utilities
  ...
```

## Dependencies

### Unity Engine

- **Assembly-CSharp**: VaM core assemblies
- **UnityEngine**: Core Unity engine
- **UnityEngine.CoreModule**: Core functionality
- **UnityEngine.PhysicsModule**: Physics simulation
- **UnityEngine.UI**: UI components

### External Libraries

- SimpleJSON: JSON parsing
- Custom MMD parsing library

## Build Process

### ILMerge Integration

The project uses ILMerge to merge dependencies:

**Configuration**:
- `ILMerge.props`: Build properties
- `ILMergeOrder.txt`: Merge order specification
- Output: Single merged DLL

### Compilation Target

- .NET Framework 3.5 for Unity compatibility
- AnyCPU platform
- Referenced assemblies marked Private=False (Unity provides)

## Usage Flow

1. **Plugin Initialization**:
   - Plugin loads in VaM
   - Initializes dictionaries and structures
   - Registers for update loop

2. **JSON Detection**:
   - Checks for loadscene.json each frame
   - Waits if VaM is loading

3. **Resource Processing**:
   - Parses JSON configuration
   - Processes each resource type
   - Creates/updates atoms as needed

4. **Scene Update**:
   - Applies motions
   - Updates timeline
   - Synchronizes audio

5. **Cleanup**:
   - Deletes JSON file
   - Optionally rescans packages

## Error Handling

- Try-catch blocks around critical operations
- VaM logging integration
- Graceful degradation on errors

## Performance Considerations

- Coroutines for non-blocking operations
- Efficient file I/O
- Optimized bone mapping
- Lazy loading where possible

## Limitations

- Requires Unity/VaM runtime
- Limited to VaM atom system
- Physics constraints may need tuning
- Large MMD files may impact performance

---

Continue to:
- [Phase 5: UI Components](./05-UI-Components.md)
- [Phase 6: Database Schema](./06-Database-Schema.md)

