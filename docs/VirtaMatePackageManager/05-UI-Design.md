# UI Design

## Overview

The user interface for VirtaMatePackageManager will be built using **WPF** (Windows Presentation Foundation) or **Avalonia UI** (cross-platform option). The design follows modern desktop application patterns with focus on performance and usability.

## Design Principles

1. **Performance First**: Virtual scrolling, lazy loading, async operations
2. **Responsive**: Never blocks UI, always shows progress
3. **Intuitive**: Familiar patterns, clear navigation
4. **Efficient**: Keyboard shortcuts, bulk operations
5. **Modern**: Clean design, good use of space

## UI Technology Decision

### Option 1: WPF (Recommended)
- **Pros**: Native Windows, mature, good tooling
- **Cons**: Windows-only

### Option 2: Avalonia UI
- **Pros**: Cross-platform, modern, MVVM-friendly
- **Cons**: Newer, smaller community

**Decision**: Start with WPF for MVP, consider Avalonia for future cross-platform support.

## Architecture: MVVM Pattern

```
View (XAML)
    ↓ (Data Binding)
ViewModel
    ↓ (Commands, Properties)
Service/Application Layer
    ↓
Domain/Infrastructure
```

### ViewModel Base

```csharp
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
            
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
```

---

## Main Window Layout

### Overall Structure

```
┌─────────────────────────────────────────────────────────────┐
│ [Menu Bar] File Edit View Tools Help                        │
├─────────────────────────────────────────────────────────────┤
│ [Toolbar] [Refresh] [Scan] [Install] [Uninstall] [Settings]│
├──────────────┬──────────────────────────────────────────────┤
│              │                                               │
│  LEFT PANEL  │            MAIN CONTENT AREA                  │
│              │                                               │
│  - Filters   │  ┌─────────────────────────────────────────┐ │
│  - Repos     │  │  [Search Box]          [Sort] [View]    │ │
│  - Targets   │  ├─────────────────────────────────────────┤ │
│              │  │  VAR Packages List (Virtual Scrolling)  │ │
│              │  │  ┌─────┬─────┬─────┬─────┬─────┐       │ │
│              │  │  │ Icon│Name │Crtr │Ver  │Inst │       │ │
│              │  │  ├─────┼─────┼─────┼─────┼─────┤       │ │
│              │  │  │ 📦  │Var1 │Crtr1│1.0  │[✓]  │       │ │
│              │  │  │ 📦  │Var2 │Crtr2│2.0  │     │       │ │
│              │  │  └─────┴─────┴─────┴─────┴─────┘       │ │
│              │  └─────────────────────────────────────────┘ │
│              │                                               │
│              │  [Details Panel - when item selected]        │
│              │  ┌─────────────────────────────────────────┐ │
│              │  │ Preview Image                           │ │
│              │  │ Description                             │ │
│              │  │ Dependencies                            │ │
│              │  └─────────────────────────────────────────┘ │
├──────────────┴──────────────────────────────────────────────┤
│ [Status Bar] Ready | 10,234 VARs | 1,234 Installed          │
└─────────────────────────────────────────────────────────────┘
```

### Panel Breakdown

#### 1. Left Panel (Navigation/Filters)

**Width**: 250px (collapsible)

**Sections**:
- **Quick Filters**
  - [All] [Installed] [Not Installed] [Missing Deps]
- **Repositories**
  - Tree view of repositories
  - Checkboxes to filter by repository
- **Installation Targets**
  - List of targets
  - Active target highlighted
- **Creators**
  - Filter by creator name
  - Search box for creator

#### 2. Main Content Area

**VAR Packages List**:
- Virtual scrolling DataGrid
- Columns:
  - Checkbox (for selection)
  - Preview thumbnail
  - VAR Name
  - Creator
  - Version
  - Repository
  - Installation Status
  - File Size
  - Date Added
- Sortable columns
- Resizable columns
- Context menu:
  - Install
  - Uninstall
  - View Details
  - Open Location
  - Delete

**Search Bar**:
- Real-time search
- Search in: Name, Creator, Description
- Advanced search dialog

#### 3. Details Panel (Right/Bottom)

**Shown when VAR selected**:
- Large preview image
- Full description
- Creator, Package, Version
- License type
- File path
- File size, dates
- Dependencies list
- Reverse dependencies
- Action buttons: Install, Uninstall, Open Location

---

## Key UI Components

### 1. Virtual Scrolling DataGrid

**Requirement**: Handle 10,000+ items smoothly

**Implementation**:
```csharp
public class VirtualVarPackageDataGrid : DataGrid
{
    // Only render visible items
    // Load data on demand
    // Maintain scroll position
}
```

**Features**:
- Virtual scrolling
- Lazy data loading
- Selection preservation
- Smooth scrolling (60 FPS)

### 2. Progress Dialog

**For long-running operations**:

```
┌─────────────────────────────────────┐
│ Scanning Repository...              │
├─────────────────────────────────────┤
│ ████████████░░░░░░░░  60%          │
│                                      │
│ Processing: Creator.Package.1.var   │
│ Files scanned: 600 / 1000           │
│                                      │
│ [Cancel]                             │
└─────────────────────────────────────┘
```

**Features**:
- Progress bar
- Current operation text
- Statistics
- Time remaining estimate
- Cancellation support

### 3. Repository Management Dialog

```
┌──────────────────────────────────────────┐
│ Repository Management                    │
├──────────────────────────────────────────┤
│ [Add Repository]                         │
├──────────────────────────────────────────┤
│ Name          │ Path          │ Priority │
├───────────────┼───────────────┼──────────┤
│ Main SSD     │ D:\Vars\Main  │   10     │
│ Network      │ \\NAS\Vars    │    5     │
│ External     │ E:\Vars       │    1     │
└──────────────────────────────────────────┘
│ [Edit] [Delete] [Enable/Disable] [Close] │
└──────────────────────────────────────────┘
```

### 4. Installation Target Dialog

Similar to Repository Management but for installation targets.

### 5. VAR Details Dialog

```
┌──────────────────────────────────────────┐
│ Creator.Package.1.var                    │
├───────────┬──────────────────────────────┤
│           │ Creator: Creator Name        │
│           │ Package: Package Name        │
│ Preview   │ Version: 1.0                 │
│ Image     │ License: CC BY               │
│           │                              │
│           │ Description:                 │
│           │ [Full description text...]   │
│           │                              │
│           │ Dependencies:                │
│           │ • Dep1.latest [Found]        │
│           │ • Dep2.1 [Missing]           │
└───────────┴──────────────────────────────┘
│ [Install] [Uninstall] [Open Location]    │
└──────────────────────────────────────────┘
```

### 6. Dependency Graph Viewer

**For visualizing dependencies**:

```
┌──────────────────────────────────────────┐
│ Dependency Graph                         │
├──────────────────────────────────────────┤
│                                          │
│     ┌─────────┐                         │
│     │ Var A   │                         │
│     └────┬────┘                         │
│          │                               │
│     ┌────▼────┐    ┌─────────┐         │
│     │ Var B   ├───►│ Var C   │         │
│     └─────────┘    └─────────┘         │
│                                          │
│ [Missing: Var D] [Red highlight]        │
└──────────────────────────────────────────┘
```

---

## User Workflows

### Workflow 1: Install VAR Package

1. User browses VAR list
2. Selects VAR(s) to install
3. Clicks "Install" or right-click → Install
4. System checks dependencies
5. If dependencies missing → Show dialog:
   ```
   Missing Dependencies:
   • Dep1.latest
   • Dep2.1
   
   [Install Anyway] [Cancel] [Install Dependencies]
   ```
6. User chooses option
7. Progress dialog shows installation progress
8. Installation completes
9. UI updates (installation status changes)

### Workflow 2: Scan Repository

1. User right-clicks repository in left panel
2. Selects "Scan Repository"
3. Progress dialog appears:
   - Shows current file being scanned
   - Progress percentage
   - Files found/added/updated count
4. User can cancel scan
5. When complete, VAR list updates

### Workflow 3: Search VAR Packages

1. User types in search box
2. Real-time filtering as they type
3. Results update instantly
4. Can combine with filters:
   - Repository filter
   - Installation status filter
   - Creator filter
5. Results shown in main list

### Workflow 4: Manage Multiple Repositories

1. User opens Repository Management dialog
2. Clicks "Add Repository"
3. Browser dialog for folder selection
4. System validates path
5. Repository added to list
6. System automatically scans new repository
7. VAR packages appear in main list

---

## Responsive Design Considerations

### Large Lists (10,000+ items)

- Virtual scrolling only renders visible items
- Lazy loading of data as user scrolls
- Debounced search (wait for user to stop typing)
- Progressive loading (show results as they come)

### Long Operations

- All operations run in background
- Progress indication always shown
- Cancellation always available
- Status updates in status bar

### Memory Management

- Dispose resources properly
- Clear caches when not needed
- Limit image cache size
- Unload unused view models

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+F` | Focus search box |
| `Ctrl+I` | Install selected VAR(s) |
| `Ctrl+U` | Uninstall selected VAR(s) |
| `Ctrl+R` | Refresh/Rescan |
| `F5` | Refresh list |
| `Delete` | Delete selected VAR(s) |
| `Enter` | View details |
| `Escape` | Close dialog / Clear selection |
| `Ctrl+A` | Select all |
| `Ctrl+D` | Deselect all |

---

## UI State Management

### View Model States

```csharp
public class VarPackageListViewModel : ViewModelBase
{
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }
    
    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }
    
    // Data
    public ObservableCollection<VarPackageItemViewModel> Items { get; }
    public VarPackageItemViewModel? SelectedItem { get; set; }
}
```

### Loading States

- **Idle**: Ready for user input
- **Loading**: Show loading indicator
- **Processing**: Show progress dialog
- **Error**: Show error message
- **Success**: Brief success notification

---

## Theming & Styling

### Default Theme
- Modern, clean design
- Good contrast for readability
- Consistent spacing and alignment

### Color Scheme
- Primary: Blue (#007ACC)
- Success: Green (#28A745)
- Warning: Orange (#FFC107)
- Error: Red (#DC3545)
- Background: Light Gray (#F5F5F5)

### Icons
- Use Material Design icons or similar
- Consistent icon size and style
- Meaningful icons for actions

---

## Accessibility

### Requirements
- Keyboard navigation support
- Screen reader compatibility
- High contrast mode support
- Font scaling support
- Focus indicators visible

---

## Next Steps

Continue to:
- [Migration Plan](./06-Migration-Plan.md) - Migration from legacy system
- [Development Guide](./07-Development-Guide.md) - Setup and coding standards

