# Phase 3: MMDLoader Application

## Overview

**MMDLoader** is a WPF (Windows Presentation Foundation) application designed to load MMD (MikuMikuDance) motion files into Virt-a-Mate scenes. It provides a user-friendly interface for managing MMD projects, selecting motions, and generating scene configurations.

## Project Configuration

- **Framework**: .NET 6.0-windows
- **UI Framework**: WPF
- **Output Type**: WinExe
- **File Version**: 1.0.1.0
- **Application Icon**: icon.ico

## Architecture

### Main Window (MainWindow.xaml)

Primary WPF window with three-column layout:

1. **Left Column**: Project directory list
2. **Middle Column**: Audio controls and person motion configuration
3. **Right Column**: Camera controls and action buttons

### Key Components

#### 1. Project Directory Scanner

**Function**: `buttonLoadMMD_Click`

- Scans user-selected directory for MMD projects
- Identifies projects based on rules:
  - Maximum 5 subdirectories
  - Each subdirectory contains only pure audio OR motion files
- Groups VMD files and audio files by project
- Stores project information in `dictMmd` dictionary

#### 2. Audio Management

**ComboBox**: `comboBoxAudio`

- Lists available audio files (.wav, .mp3)
- Ordered by file size (largest first)
- Integrated with `MediaPlayer` for preview
- Supports audio playback with seek slider

**Features**:
- Auto-play option
- Time display (current/total)
- Seek functionality
- Stops playback when loading scene

#### 3. Camera Motion (VMD)

**ComboBox**: `comboBoxCamVmd`

- Lists camera VMD files
- Auto-detects camera files by name patterns:
  - "cam", "camera", "镜头", "カメラ", "카메라"
- Supports optional camera loading
- Integrates with audio for synchronized playback

#### 4. Person Motion Configuration

**User Control**: `UserCtlPersonVMD`

Custom WPF user control for configuring person/character motions:
- Supports up to 5 additional persons (plus default person)
- Each person can have:
  - Primary motion (Motion1)
  - Secondary motion (Motion2) - optional
  - Position settings (X, Y, Z)
  - Face motion options
  - High heel settings
  - Straight leg configuration

**Features**:
- Dynamic person addition/removal
- Position offsets to prevent collisions
- Motion selection from available VMD files
- Real-time JSON generation for testing

### Communication Pattern

#### File-Based Communication

MMDLoader communicates with the LoadScene Unity plugin via JSON file:

**Location**: `{vampath}\Custom\PluginData\feelfar\loadscene.json`

**JSON Structure**:
```json
{
  "rescan": false,
  "resources": [
    {
      "type": "audio",
      "saveName": "MMDForLoad/audio.mp3",
      "characterGender": "female",
      "ignoreGender": "true",
      "personOrder": 0
    },
    {
      "type": "cameravmd",
      "saveName": "MMDForLoad/camera.vmd",
      "AudioSourceControl": true,
      "personOrder": 0
    },
    {
      "type": "personvmd",
      "saveName": "MMDForLoad/motion.vmd",
      "personOrder": 1,
      "ignoreFace": false,
      "straightLeg": true,
      ...
    }
  ]
}
```

#### MMD Folder Management

**Function**: `ResetMMDFolder`

- Creates/cleans `MMDForLoad` directory in VaM installation
- Copies selected audio and VMD files to this directory
- Files are referenced relative to this directory in JSON

### Settings Integration

**WindowSettings**: Configuration dialog

- VaM installation path
- MMD directory path
- Other preferences

Stored in `Settings.Default` (application settings).

### User Controls

#### UserCtlPersonVMD.xaml

Custom control for person motion configuration:
- VMD file selection (Motion1, Motion2)
- Position sliders (X, Y, Z)
- Checkboxes for options (Face, High Heel, etc.)
- Numeric inputs for advanced settings
- "Test" button for real-time preview

#### UserCtlSlider.xaml

Reusable slider control with numeric display.

### Comm.cs

Utility class for symbolic link operations (shared with varManager pattern):
- `CreateSymbolicLink`: Windows API wrapper

## Workflow

### Loading MMD Project

1. **Select Directory**: User clicks "MMD Dir" button
2. **Scan Projects**: System scans and identifies MMD projects
3. **Select Project**: User selects project from list
4. **Configure Audio**: User selects audio file (optional)
5. **Configure Camera**: User selects camera VMD (optional)
6. **Configure Persons**: User configures person motions
7. **Load**: User clicks "Load" button
8. **File Generation**: System generates loadscene.json
9. **Plugin Execution**: LoadScene plugin reads JSON and loads scene

### Real-Time Testing

Users can test individual person configurations:
- Click "Test" button on person control
- Generates temporary JSON with single person
- LoadScene plugin immediately loads the configuration
- Useful for adjusting positions and settings

## File Management

### MMDForLoad Directory

Temporary directory structure:
```
{vampath}/
  MMDForLoad/
    audio.mp3 (or .wav)
    camera.vmd
    person1_motion.vmd
    person2_motion.vmd
    ...
```

Files are copied (not linked) to ensure availability.

### Cleanup

- Old preset files deleted before each load
- Previous loadscene.json deleted
- MMDForLoad directory recreated each time

## Integration Points

### With LoadScene Plugin

- JSON file format agreement
- Directory structure convention
- Resource type definitions

### With VaM

- Custom directory usage
- Plugin data directory
- Scene loading mechanism

## Error Handling

- Validates VaM path on startup
- Checks file existence before copying
- Handles missing files gracefully
- User-friendly error messages

## UI Features

### Filtering

Text box filter for project names:
- Real-time filtering as user types
- Case-insensitive matching

### Double-Click Navigation

Double-click project in list opens Windows Explorer to project folder.

### Settings Dialog

Accessible via "Settings" button for configuration changes.

## Resource Types

Supported resource types in JSON:

1. **audio**: Audio file for scene
2. **cameravmd**: Camera motion VMD file
3. **personvmd**: Person/character motion VMD file
4. **highheel**: High heel configuration
5. **empytscene**: Empty scene initialization

## Configuration Options

### Person Options

- **ignoreFace**: Whether to ignore face motions
- **straightLeg**: Enable straight leg physics
- **enableHighHeel**: Enable high heel physics
- **AudioSourceControl**: Sync with audio source
- **personOrder**: Character order (1-based)

### Position Settings

- **PosX, PosY, PosZ**: Character position offsets
- Automatic spacing to prevent collisions

## Dependencies

- **SimpleJSON**: JSON generation and parsing
- **System.Windows.Forms**: FolderBrowserDialog
- **System.Media**: MediaPlayer for audio preview

## Limitations

- Maximum 6 persons (1 default + 5 additional)
- Requires VaM installation
- Requires LoadScene plugin installed
- Audio format: WAV or MP3 only

---

Continue to:
- [Phase 4: LoadScene Component](./04-LoadScene.md)
- [Phase 5: UI Components](./05-UI-Components.md)

