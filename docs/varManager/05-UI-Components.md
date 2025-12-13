# Phase 5: UI Components

## Overview

This phase documents the reusable UI components used across the varManager-MMDLoader system. These components provide specialized functionality for data filtering, drag-and-drop operations, rating displays, and tree view controls.

## Component Projects

### 1. DgvFilterPopup

**Purpose**: Advanced filtering system for DataGridView controls

**Framework**: .NET Framework 2.0

**Namespace**: `DgvFilterPopup`

#### Architecture

The component follows an extensible architecture:

```
DgvBaseFilterHost (abstract)
    └── DgvFilterHost (implementation)
    
DgvBaseColumnFilter (abstract base)
    ├── Implementations/
    │   ├── DgvTextBoxColumnFilter
    │   ├── DgvDateColumnFilter
    │   ├── DgvCheckBoxColumnFilter
    │   └── DgvComboBoxColumnFilter
    └── Extensions/
        ├── DgvDateRangeColumnFilter
        ├── DgvNumRangeColumnFilter
        ├── DgvMultiCheckBoxColumnFilter
        ├── DgvMultiTextBoxColumnFilter
        └── DgvMonthYearColumnFilter
```

#### Core Classes

##### DgvFilterManager

Main manager class that:
- Associates filters with DataGridView columns
- Manages filter popup windows
- Handles filter application
- Maintains filter state

##### DgvBaseColumnFilter

Abstract base class for all column filters:
- Defines filter interface
- Manages filter popup display
- Provides filter result evaluation

##### Filter Implementations

1. **DgvTextBoxColumnFilter**: Text search filter
2. **DgvDateColumnFilter**: Date selection filter
3. **DgvCheckBoxColumnFilter**: Boolean filter
4. **DgvComboBoxColumnFilter**: Dropdown selection filter

##### Filter Extensions

1. **DgvDateRangeColumnFilter**: Date range filtering
2. **DgvNumRangeColumnFilter**: Numeric range filtering
3. **DgvMultiCheckBoxColumnFilter**: Multiple checkbox selection
4. **DgvMultiTextBoxColumnFilter**: Multiple text filters
5. **DgvMonthYearColumnFilter**: Month/year selection

#### Features

- **Popup Windows**: Filters appear in popup windows
- **Visual Indicators**: Filter icons on column headers
- **Multiple Filters**: Can combine multiple filter types
- **Persistent State**: Filters can remember their state
- **Extensible**: Easy to add custom filter types

#### Usage Pattern

```csharp
DgvFilterManager filterManager = new DgvFilterManager(dataGridView1);
filterManager.EnableFiltering = true;
// Filters are automatically attached to columns
```

#### Design Diagrams

The project includes Class Diagram files:
- `BasicDiagram.cd`: Basic architecture
- `DetailDiagram.cd`: Detailed relationships
- `Extensions.cd`: Extension classes
- `Implementations.cd`: Implementation classes

### 2. DragNDrop

**Purpose**: Drag-and-drop functionality for ListView controls

**Framework**: .NET Framework (version not specified)

**Namespace**: `DragNDrop`

#### Core Class

##### DragAndDropListView

Inherits from `System.Windows.Forms.ListView`

**Key Features**:
- **Reorder Items**: Drag items to reorder within list
- **Cross-List Drag**: Drag items between ListViews
- **Visual Feedback**: Shows drop position indicator
- **Custom Events**: Raises events on drag-drop operations

**Properties**:
- `AllowReorder`: Enable/disable reordering
- `AllowSelfDrop`: Allow dropping on same control
- `LineColor`: Color of drop indicator line

**Events**:
- `ListViewDragDrop`: Custom drag-drop event

#### Implementation Details

- Overrides `OnDragDrop`, `OnDragOver`, `OnDragLeave`
- Manages drag item data structure
- Provides visual feedback during drag
- Handles item insertion and removal

#### Usage

```csharp
DragAndDropListView listView = new DragAndDropListView();
listView.AllowReorder = true;
listView.ListViewDragDrop += OnDragDrop;
```

### 3. StarRatingControl

**Purpose**: Star rating display and input control

**Framework**: .NET Framework (Windows Forms)

**Namespace**: `RatingControls`

#### Core Class

##### StarRatingControl

Inherits from `System.Windows.Forms.Control`

**Features**:
- Visual star display (empty, half, full)
- Mouse interaction for rating input
- Configurable star count (default: 5)
- Customizable appearance

**Properties**:
- `StarCount`: Number of stars (default: 5)
- `Rating`: Current rating value (0-5)
- `ReadOnly`: Whether control is editable
- Margins: Left, Right, Top, Bottom
- `StarSpacing`: Space between stars

**Events**:
- `RatingChanged`: Fired when rating changes

#### Visual States

Stars can display:
- **Empty**: No rating
- **Half**: Half star (0.5 increments)
- **Full**: Full star

Uses image resources:
- `starEmpty.png`
- `starHalf.png`
- `starFull.png`
- `starOneQuarter.png`
- `starTriQuarter.png`

#### Implementation

- Custom painting in `OnPaint`
- Mouse event handling for interaction
- Double-buffered rendering
- Support for fractional ratings (0.5 increments)

### 4. ThreeStateTreeView

**Purpose**: Tree view with three-state checkboxes

**Framework**: .NET Framework

**Namespace**: Not specified in source

#### Core Components

##### ThreeStateTreeView

Tree view control with checkboxes that have three states:
- **Checked**: Fully selected
- **Unchecked**: Not selected
- **Indeterminate**: Partially selected (some children checked)

##### CheckBoxState Enumeration

Defines checkbox states:
```csharp
public enum CheckBoxState
{
    Unchecked,
    Checked,
    Indeterminate
}
```

#### Features

- **Hierarchical Selection**: Checking parent affects children
- **State Propagation**: Children state affects parent state
- **Visual Feedback**: Shows indeterminate state
- **Custom Rendering**: Custom-drawn checkboxes

#### Usage Pattern

Used in varManager for:
- Dependency selection
- Feature toggling
- Hierarchical option selection

### 5. ThreeStateTreeview (varManager)

**Purpose**: Custom implementation in varManager project

**Location**: `varManager/ThreeStateTreeview.cs`

Note: Separate implementation from ThreeStateTreeView project, likely for specific varManager needs.

## Integration

### varManager Integration

**DgvFilterPopup**:
- Used in Form1 main DataGridView
- Filters VAR lists by various criteria

**DragNDrop**:
- Used in FormScenes for scene reordering
- Enables drag-and-drop scene management

**StarRatingControl**:
- Used for VAR rating display
- Integrated with database for rating storage

**ThreeStateTreeView**:
- Used in dependency management
- Hierarchical selection interfaces

### Design Patterns

1. **Template Method Pattern**: DgvBaseColumnFilter defines template
2. **Strategy Pattern**: Different filter implementations
3. **Observer Pattern**: Event-based communication
4. **Decorator Pattern**: Extending base functionality

## Reusability

All components are designed as reusable libraries:
- Minimal dependencies
- Clear interfaces
- Self-contained functionality
- Can be used in other projects

## Customization

Components support customization through:
- Properties for appearance
- Events for behavior
- Extensibility through inheritance
- Resource files for images/icons

## Build Output

Components compile to DLL libraries:
- `DgvFilterPopup.dll`
- `DragNDrop.dll`
- `StarRatingControl.dll`
- `ThreeStateTreeView.dll`

These are referenced as project references in the solution.

---

Continue to:
- [Phase 6: Database Schema](./06-Database-Schema.md)
- [Phase 7: Build Configuration](./07-Build-Configuration.md)

