# 06 — Preview, Scene Analysis & Loading

This chapter covers the content-browsing half of varManager: the preview-image pipeline, scene decomposition into atoms, FormAnalysis preset extraction, PrepareSaves packaging prep, and the `loadscene.json` hand-off that actually loads content into a running VaM.

## Preview image pipeline

### Producer
Preview JPGs are extracted during indexing (see [03](./03-Indexing-and-Content-Classification.md)): sibling `{entry}.jpg` files land in `{varspath}\___PreviewPics___\{type}\{varName}\{type}{counter:000}_{name}.jpg`, and each previewable entry becomes a `scenes` row (`varName, atomType, isPreset, scenePath, previewPic`). The `scenes` table — **not** the filesystem — is the browse-time source of truth.

### In-memory model — `Previewpic` struct (`Form1.cs:1902`)
Immutable readonly struct: `Varname, Atomtype, Picpath, Installed, ScenePath, IsPreset`. Two lists: `previewpics` (for the current selection) and `previewpicsfilter` (after filtering).

### Selection → display
- `UpdatePreviewPics()` (`:1926`) — on grid selection change, rebuild `previewpics` from the `scenes` rows of every selected var, then `PreviewInitType()`.
- `PreviewInitType()` (`:1962`, `mut`-guarded) — apply filters: "loadable only" keeps `IsPreset || Atomtype=="scenes"`; a type combo keeps one `Atomtype`. Sets `listViewPreviewPics.VirtualListSize` and invalidates.
- `listViewPreviewPics_RetrieveVirtualItem` (`:2054`) — the **virtual-mode** renderer, called per visible row:
  - Resolve image path `___PreviewPics___\{Atomtype}\{Varname}\{Picpath}`; missing → log "run fix preview" + fall back to `vam.png`.
  - **Bounded LRU image cache** in `imageListPreviewPics`: load on demand; when it exceeds **20 images**, `RemoveAt(0)`.
  - SubItems carry data by position — **⚠ load-bearing contract**: `[1]`=Varname, `[2]`=image path, `[3]`=Installed, `[4]`=Atomtype, `[5]`=ScenePath, `[6]`=IsPreset. Three handlers read these back.
- `Worker_DoWork` (`:2106`) / `listViewPreviewPics_Click` — populate the big detail panel: preview image, Install/Uninstall button (colored by state), and (for loadable items) a Load button plus the `jsonLoadScene` payload.

✎ **Rebuild notes:** virtual/​virtualized lists are mandatory (thousands of items); keep a bounded image cache. The **pagination system is entirely dead code** (commented out) — the app was refactored from paged eager-loading to virtual mode; don't port pagination. Bind the `Previewpic` struct directly instead of the stringly-typed SubItem indices.

## Scene analysis — decomposing a scene into atoms

Loading a scene into the Analysis tool first **explodes** its JSON into a per-atom cache tree, then FormAnalysis lets the user pull pieces out.

### `SaveNameSplit(saveName)` (`:3102`)
Splits `"Creator.Pkg.Ver:/path/inside/var"` on the literal `":/"` → `(varName, entryName)`. No `":/"` (a loose file) → `("save", saveName)`; the sentinel `"save"` means "a loose file under `vampath`, not inside a var".

### `ReadSaveName(saveName, characterGender, analysis=false)` (`:3465`)
Central JSON loader + cache builder:
1. Split. If inside a var, open the var ZIP and read the entry text; else read the loose JSON from `{vampath}\{saveName}`.
2. **Gender inference** when input is `"unknown"`: default `"male"`; if the JSON text or path contains `/Female/` → `"female"`.
3. `Getdependencies(jsonscene)` → dependency list.
4. Build cache folder `.\Cache\{validVarName}\{validEntryName}\`; write `depend.txt` (one dependency per line) and `gender.txt`.
5. If `analysis`: rewrite `"SELF:/` → `"{varName}:/` (so self-references resolve), then `AnalysisAtoms(jsonscene, sceneFolder, true)`.

### `AnalysisAtoms(jsonscene, sceneFolder, isperson)` (`:3527`)
Recursive decomposition into `.bin` files (which are plain JSON despite the extension):
- **No `atoms` key** (single-atom JSON): save the whole node to `…\atoms\Person\{atomID}.bin` (if person) or `…\{atomID}.bin`.
- **Full scene**: save all top-level keys except `atoms` to `posinfo.bin` (scene camera/render settings). For each atom in `atoms[]`:
  - `atomtype = atom["type"]`; base atoms `{CoreControl, PlayerNavigationPanel, VRController, WindowCamera}` get a `(base)` prefix.
  - `SubScene` → recurse. Else save `…\atoms\{atomtype}\{atomID}.bin` and record parent→children relationships.
  - Write `parentAtom.txt` (tab: `parent<TAB>child1,child2,…`) for cascading tree checks.

### Gender & atom-id helpers
- `GetAtomID` (`:3445`) — for a Person atom, read `storables[id==geometry].character`, return `(Gender){atomId}`.
- `GetCharacterGender(character)` (`:3425`) — lowercased name: starts with `male/lee/jarlee/julian/jarjulian` → `Male`; `futa` → `Futa`; else `Female`. ⚠ Only works when the person atom is "On" (else DAZCharacter is null). Fragile string heuristic.

## FormAnalysis — preset extraction (the hardest feature to replicate)

`Form1.Analysisscene(jsonLS)` (`:3394`) builds the cache (via `ReadSaveName(..., analysis:true)`) then opens **FormAnalysis**, which slices a Person atom's `storables` array into targeted VaM `.vap`/`.bin` preset files and rebuilds scenes from chosen atoms.

- **Tri-state checkbox TreeView** of the cache `atoms` folder; `parentAtom.txt` drives cascading checks. Person bins list in `listBoxAtom` with auto-detected gender.
- **Preset extraction** (`SavePreset` and siblings) — slices `storables` by id allow-lists (⚠ these lists are load-bearing; copy verbatim):
  - `geometry` → morphs, conditionally clothing/hair arrays; collect clothing/hair `internalId`s to also grab their material storables (`id.StartsWith(internalId)`).
  - Skin ids: `skin, textures, teeth, tongue, mouth, *Eyelashes, lacrimals, sclera, irises`.
  - Breast ids: `BreastControl, BreastPhysicsMesh`. Glute ids: `GluteControl, LowerPhysicsMesh`. (Breast/Glute Female-only.)
  - Emits combined appearance `.vap` plus separate morph/breast/glute presets and "reset" helper presets (naked-clothing, bald-hair, default-eye-color). Every `"SELF:/"` reference is rewritten to `"{varName}:/"`.
  - **Pose** (`SavePosePreset`): geometry morphs + a fixed control-id allow-list. **Animation** (`SaveAnimationPreset`): all `*Animation` storables + control storables + the `MotionAnimationMaster` block from CoreControl (saved as `.bin`). **Plugin** (`SavePluginPreset`): the `PluginManager` storable + plugin-id-prefixed storables.
  - Which presets are extracted is gated by the six **`presetMorphs/Hair/Clothing/Skin/Breast/Glute`** settings (defaults: Morphs/Hair/Skin/Breast = true; Clothing/Glute = false). ⚠ **Accuracy note:** these settings are *not* referenced in `Form1.cs` — they drive **FormAnalysis** checkboxes only, not `LoadScene`. Model them as FormAnalysis defaults.
- **Scene rebuild** (`AddToScene`): auto-check base atoms, copy each checked atom `.bin` into `Custom\PluginData\feelfar\`, register each as `atom`/`atomSubscene`, then `GenLoadscenetxt`.
- Each saved preset is registered via `AddPresetResouce(type, saveName)` into a resource list carrying `{type, saveName, characterGender, ignoreGender, personOrder}`.

## Scene loading — the `loadscene.json` hand-off (⚠ load-bearing)

### `buttonLoad_Click` (`:3116`) → `LoadScene(jc, merge, ignoreGender, characterGender, personOrder)` (`:3144`)
Gathers UI state (`merge`, `ignoreGender`, `characterGender` = male/female/unknown, `personOrder` from the radio group), ensures the cache exists (`ReadSaveName`), reads `depend.txt`/`gender.txt` (the cached gender **overrides** the passed one), then `GenLoadscenetxt(...)`.

### `GenLoadscenetxt(...)` (`:3188`) — write the IPC file
1. Deep-clone the JSON.
2. For each resource, inject defaults if absent: `merge`, `characterGender`, `ignoreGender`, `personOrder`. If any resource `type=="scenes"`, snapshot current temp links via `AddDeleteTemp()`.
3. `InstallTemp(dependVars, ref rescan)` — install missing dependency vars as **temporary** symlinks in `___TempVarLink___`; set `rescan`. Remove just-installed temps from the delete list so they survive the load.
4. Overwrite `{vampath}\Custom\PluginData\feelfar\loadscene.json` (tab-indented). **This is the hand-off** — the in-game "feelfar" plugin polls it, performs the load with gender/person-order matching, then deletes it.
5. If temp files remain to clean, spawn `DeleteTempThread` (waits for `loadscene.json` to disappear, +20 s, then deletes temp links).

### `loadscene.json` root shape (from varManager)
```jsonc
{
  "rescan": "true|false",
  "resources": [
    { "type": "scenes|looks|clothing|hairstyle|morphs|pose|skin|atom|atomSubscene|...",
      "saveName": "Creator.Pkg.Ver:/path/inside/var",   // or a loose path for Saves
      "merge": "false", "characterGender": "female",
      "ignoreGender": "true", "personOrder": "1" }
  ]
}
```
The full resource vocabulary (including MMD types) is defined by the LoadScene plugin — see [09](./09-MMDLoader-and-LoadScene.md) for the complete schema. `RescanPackages()` writes a minimal `{"rescan":"true"}` to the same file to trigger a package rescan after install/uninstall/switch.

## PrepareSaves — package-prep dependency crawler (`PrepareSaves.cs`)

A modal tool that computes the **full file set** a scene/appearance/preset needs, so it can be bundled into a new var.

- **TreeView** of three roots: `Saves\scene\*.json`, `Saves\Person\appearance\*.json`, `Custom\Atom\Person\Appearance\*.vap` (all checked by default).
- `dependFiles(...)` — recursive reference resolver: two regexes (a `Creator.Pkg.(N|latest):filepath` var-ref and a `Custom/...` loose-file ref) scan each JSON; refs ending `.vap`/`.json` are themselves crawled next round; terminal assets (textures, meshes, `.assetbundle`, `.vam`) go to the output set. Recurse until no new JSON. `ReadJsonfile` reads a var entry (via `VarExistName` + `getVarFilePath` + ZIP) or a loose file.
- Output: distinct+sorted file list; copy-to-clipboard. `buttonOutput_Click` validates the target folder is empty — the actual copy/packaging is a **stub/TODO** in this build.

## Export / import installed lists

- `buttonExpInsted_Click` (`:2950`) — write all installed `varName`s (from `installStatus`) to a text file, one per line.
- `buttonInstFormTxt_Click` (`:2963`) — read a text file and `VarInstall` each line verbatim (**no** dependency expansion), then `UpdateVarsInstalled` + `RescanPackages`.

Continue to [07 — Database Schema](./07-Database-Schema.md).
