# 10 — UI Feature Catalog

Every window in the varManager WinForms app and what it does. `Form1` is the hub; all dialogs reach back into `Form1`'s public methods for business logic.

## Form1 — main window (`Form1.cs`, 3,743 lines)

The single "God object". Its surface, grouped by feature (deep detail in chapters [02](./02-VAR-Format-and-Repository-Layout.md)–[06](./06-Preview-Scene-Analysis-and-Loading.md)):

- **VAR grid** — `varsView`-bound `DataGridView` (var name, creator, counts, size, installed/disabled), with DgvFilterPopup column filters, a creator combo, a name-text filter, and an "installed only" toggle.
- **Preview panel** — virtual-mode `ListView` of preview thumbnails for the selected var(s), a large detail image, and per-item Install/Uninstall/Load/Analysis/Locate actions.
- **Toolbar / buttons** — Settings, UpdDB (Tidy + index + reinstall), Install/Uninstall/Delete/Move selected, Missing-depends (all/filtered/log), Fix (rebuild links / previews / saves-dependencies), Stale/Old-version cleanup, Export-installed / Install-from-txt, Clear-cache, Scenes manager, Hub, Prepare-saves, AddonPackages profile switch combo (+ add/rename/delete).
- **Background pipeline** — one `BackgroundWorker` dispatched by string command; a `Mutex` serializes it; progress + log via `BeginInvoke`.

## Dialogs

### FormHub + HubItem — hub.virtamate.com browser/downloader
Browse/search hosted resources, compare against the local library, and generate download-link lists for missing deps and updates. Full API in [08](./08-Hub-Integration-API.md).

### FormScenes — installed scene / preset browser & launcher
The visual content browser. `AllInstalledVars` aggregates everything loadable into `scenesView`:
1. Fill `vars`, `installStatus`, `scenes` (`FillByLoadable`).
2. Build a hide/fav map from `{vampath}\AddonPackagesFilePrefs\**` (`.fav`→+1, `.hide`→−1, key `varName:scenePath`).
3. Join DB scenes + install status + hide/fav → `scenesView` tagged `installed`/`not Installed`.
4. Add `___MissingVarLink___` scenes (resolve link target) tagged `missingLink`.
5. Scan loose **Save files** on disk (`Saves\scene`, `Saves\Person\...`, `Custom\...`) by atomType, tagged `Save`, varName `(save).`.

Filtering is cascaded (category+location → hide/normal/fav → text → creator), then three **virtual-mode** ListViews (Hide/Normal/Fav) with lazy thumbnails (ImageList capped at 40, `vam.png` fallback). Actions: hide/fav via drag-drop + sidecar file writes (`SetHideFav`), column-width presets, per-item Load (`form1.LoadScene`) with merge/person-order/ignore-gender/for-male options, Analysis (`form1.Analysisscene`, scenes/looks only), Locate, Clear-cache. Uses `Application.DoEvents` to cancel its fill worker (an anti-pattern — see [Criticism](./Criticism-Document.md)).

### FormAnalysis — scene decomposition & preset extractor
The deepest feature: pull individual pieces (morphs/hair/clothing/skin/breast/glute/pose/animation/plugins) out of an extracted scene, or rebuild a scene from chosen atoms, writing VaM `.vap`/`.bin` presets. Full detail in [06 §FormAnalysis](./06-Preview-Scene-Analysis-and-Loading.md). ⚠ Its storable-id allow-lists are load-bearing and must be copied verbatim.

### FormMissingVars — missing-dependency resolver via symlink aliasing
For a missing dependency, create a **fake symlink** named after the missing var pointing at a var the user *does* own, satisfying VaM's dependency check without downloading. Alias links live in `{vampath}\AddonPackages\___MissingVarLink___\`. Handles `$` version-mismatches (IgnoreVersion toggle) and `.latest` (wildcard `Creator.Pkg.*.var`, highest). `Createlink()` deletes stale links then `Comm.CreateSymbolicLink` to the chosen destination (rewriting `.latest` names to the real version), copying timestamps. Save/Load TXT exports `missing|dest` mappings; a Google button opens a web search.

### FormUninstallVars — uninstall/delete confirmation + preview gallery
Confirmation dialog (two modes: uninstall / `delete`) shown before Form1 does the actual link deletion / file move. Value-add: a **preview-image gallery** (BackgroundWorker-loaded thumbnails, paginated 100/page, type filter) so the user sees what they're about to remove, plus a dependency list. Returns OK/Cancel only — no deletion logic here.

### FormVarDetail — single-var dependency inspector
Popup showing one var's dependency picture: what it needs (red=missing, yellow=version-substituted), what needs it (green=installed), and which loose saves reference it. Actions: Google-search missing, select-in-list, locate-file, filter-by-creator.

### FormVarsMove — organize installed vars into subfolders
Trivial input dialog: pick a destination subfolder name under `___VarsLink___\` to move selected install links into (VaM treats AddonPackages subfolders as groupings). Only collects `MovetoDirName`; Form1 does the move.

### FormStaleVars — stale-var cleanup option
One checkbox `RemoveOldVersion` gating Form1's cleanup: unchecked → `StaleVars` (only versions not depended upon), checked → `OldVersionVars` (all superseded versions). Grouping logic lives in Form1.

### FormSwitchAdd / FormSwitchRename — AddonPackages profile management
Tiny name/rename dialogs for AddonPackages "switch" profiles (stored under `___AddonPacksSwitch ___`). Reject empty/duplicate names; `default` can't be renamed/deleted. Physical folder create/move happens in Form1.

### FormSettings — configuration
Edit `varspath` (repository) and `vampath` (VaM install, validated by `VaM.exe`), plus the six preset-type booleans (Morphs/Hair/Clothing/Skin/Breast/Glute) that drive FormAnalysis. Forced open on first run / invalid paths.

### Form2 — empty/unused placeholder (only `InitializeComponent`).

## Reusable UI components (library projects)
- **DgvFilterPopup** — Excel-style per-column filter popups for the main VAR `DataGridView`.
- **DragNDrop** — drag-and-drop `ListView` support (moving items between Hide/Normal/Fav lists in FormScenes).
- **ThreeStateTreeview** (in-project) — tri-state checkbox tree for FormAnalysis (parent/child cascade).

✎ **Rebuild note:** in an MVVM rebuild these become data-bound views + view-models; the filter/drag/tri-state behaviors are built into modern grid/tree controls (Avalonia/WPF), so the custom controls are unnecessary.

Continue to [11 — Build, Dependencies & External Tools](./11-Build-Dependencies-and-External-Tools.md).
