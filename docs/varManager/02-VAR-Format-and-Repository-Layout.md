# 02 — VAR Format & Repository Layout

## The `.var` file

A `.var` is an ordinary **ZIP archive** with a specific name and one required member. varManager treats them as ZIPs (SharpZipLib for reads, 7-Zip for the extract/re-zip round-trip).

### Naming rule (⚠ load-bearing)

`ComplyVarName(varname)` (`Form1.cs:78`) is the canonical validator:

```
name.Split('.')  must have exactly 3 parts
part[2] must match ^[0-9]+$   (all digits)
```

So a valid package identity is **`Creator.Package.Version`**, e.g. `MeshedVR.ExamplePackage.3`, `AshAuryn.Expressions.5`.

Consequences and edge cases:
- Creator or package names containing a `.` break the rule (split length ≠ 3). `A.B.C.1` fails.
- Version must be pure digits. The literal `latest` **fails** `ComplyVarName` — but `latest` **is** accepted as a *dependency reference* meaning "the highest known version of `Creator.Package`" (see [04](./04-Dependency-Resolution-and-Versioning.md)).
- `ComplyVarFile(varfile)` (`Form1.cs:72`) strips the directory and `.var` extension, then delegates to `ComplyVarName`.
- Files failing the rule are quarantined into `___VarnotComplyRule___`.

✎ **Rebuild note:** model identity as a value type `PackageId { Creator, Package, int Version }` with a canonical `ToString()` = `Creator.Package.Version`, plus a separate `VersionRef` that can be a number or `Latest`. Do not scatter `Split('.')` and `$`/`^` string sentinels through the code (the legacy does — see [04](./04-Dependency-Resolution-and-Versioning.md)).

### `meta.json` (required member)

Every valid `.var` must contain a top-level `meta.json`. If it's missing or the ZIP won't open, the file is moved to `___VarnotComplyRule___` and skipped. Structure (sample fields observed):

```jsonc
{
  "licenseType": "CC BY",
  "creatorName": "Ruthven2000",
  "packageName": "LittleRed",
  "standardReferenceVersionOption": "Latest",
  "scriptReferenceVersionOption": "Exact",
  "description": "", "credits": "", "instructions": "", "promotionalLink": "",
  "programVersion": "1.20.77.9",
  "contentList": [                       // flat list of VaM-relative paths this package provides
    "Saves/scene/Current/RedHood",
    "Custom/Clothing/Female/Ruthven2000/Gipsydressopen/Gipsydressopen.vam",
    "Custom/Atom/Person/Morphs/female/AUTO/Face flat.vmi",
    "..."
  ],
  "dependencies": {                      // recursive map keyed by "Creator.Package.Version"
    "via5.Synergy.1":            { "licenseType": "FC",       "dependencies": { } },
    "AcidBubbles.Timeline.217":  { "licenseType": "CC BY-SA", "dependencies": { } },
    "YameteOuji.Shoes.latest":   { "licenseType": "CC BY",    "dependencies": { } }
  }
}
```

Key facts:
- `dependencies` is a **recursive** object; keys are `Creator.Package.Version` where Version is a number **or** `latest`.
- varManager does **not** JSON-parse this for dependencies — it regex-scans the raw text for dependency-shaped keys (see [04](./04-Dependency-Resolution-and-Versioning.md)). It *does* read `metaDate` from the ZIP entry's timestamp.
- `contentList` is not used for dependency resolution; content **types** are derived by scanning the actual ZIP entries (see [03](./03-Indexing-and-Content-Classification.md)).

### VaM content path conventions (relevant to indexing)

Content entries follow VaM's folder layout, which the indexer classifies by regex (full table in [03](./03-Indexing-and-Content-Classification.md)):
- `Saves/scene/*.json` — scenes
- `Saves/Person/appearance/*.json|vac`, `Custom/Atom/Person/appearance|general/*.json|vap` — looks (appearance presets)
- `Custom/Clothing/**`, `Custom/Atom/Person/Clothing/**` — clothing
- `Custom/Hair/**`, `Custom/Atom/Person/Hair/**` — hairstyle
- `Custom/Atom/Person/Morphs/**` — morphs; `.../Pose/**`, `Saves/Person/pose/**` — pose; `.../Skin/**` — skin
- `Custom/Scripts/**.cs|.cslist` — plugins; `Custom/Assets/**.assetbundle` — assets

## The two root paths

From `Properties.Settings` (edited in FormSettings):

| Setting | Meaning | Example |
|---|---|---|
| `varspath` | **Repository root** — where real `.var` files physically live, plus generated sidecar dirs. | `d:\vars` |
| `vampath` | **VaM install** — must contain `VaM.exe`; hosts `AddonPackages\`, `Saves\`, `Custom\`. | `d:\virt_a_mate` |

⚠ **Hard rule** (`Form1_Load`): `varspath` may **not** equal `{vampath}\AddonPackages` (case-insensitive full-path compare). Otherwise the settings dialog is forced. The repository and the install must be distinct.

## The `___XXX___` special directories (⚠ load-bearing)

These literal folder names are semantic markers on disk. Existing user libraries are laid out this way; a rebuild must preserve them or migrate deliberately. Declared as static fields (`Form1.cs:36–48`).

### Under `{varspath}` (the repository)

| Constant | Folder name | Purpose |
|---|---|---|
| `tidiedDirName` | `___VarTidied___` | **Canonical organized repository.** Layout: `___VarTidied___\{creator}\{creator.package.version}.var`. This is the **only** tree `UpdDB()` scans. |
| `redundantDirName` | `___VarRedundant___` | Duplicate-filename vars moved aside during tidy. |
| `notComplyRuleDirName` | `___VarnotComplyRule___` | Vars failing the naming rule or with an unreadable ZIP / missing `meta.json`. |
| `previewpicsDirName` | `___PreviewPics___` | Extracted preview JPGs. Layout: `___PreviewPics___\{atomType}\{varName}\{jpg}`. |
| `staleVarsDirName` | `___StaleVars___` | Old, unreferenced versions moved out by StaleVars. |
| `oldVersionVarsDirName` | `___OldVersionVars___` | Superseded versions moved out by OldVersionVars. |
| `deleVarsDirName` | `___DeletedVars___` | Soft-delete quarantine (DeleteVars moves files here instead of deleting). |

### Under `{vampath}`

| Constant | Folder name | Purpose |
|---|---|---|
| `addonPacksSwitch` | `___AddonPacksSwitch ___` | Root of AddonPackages **profiles**. Each subfolder is a named profile (`default` always exists). `AddonPackages` itself becomes a **directory symlink** to `{addonPacksSwitch}\{profile}`. **⚠ Note the trailing space before the final `___`** — it is part of the literal name. |

### Under `{vampath}\AddonPackages` (inside the active profile)

| Constant | Folder name | Purpose |
|---|---|---|
| `installLinkDirName` | `___VarsLink___` | Permanent install symlinks live here (may be nested under user "move" subfolders). |
| `missingVarLinkDirName` | `___MissingVarLink___` | Alias symlinks created by the missing-dependency resolver. |
| `tempVarLinkDirName` | `___TempVarLink___` | Temporary symlinks created for scene loading, auto-deleted after VaM consumes `loadscene.json`. |

### Sidecar files (not directories)

- `{link}.disabled` — an empty marker next to an install link meaning "installed but disabled".
- `{vampath}\AddonPackagesFilePrefs\**\*.hide` / `*.fav` (and `*.vap.fav`, `*.json.fav`) — per-scene hide/favorite state, stored **on disk, not in the DB** (see [07](./07-Database-Schema.md) §HideFav).
- `varsForInstall.txt` (app working dir) — remembers which packages were loose/active in AddonPackages so they can be re-linked after a Tidy+UpdDB cycle.

## `TidyVars` — organizing loose files into the repository

`TidyVars()` (`Form1.cs:95`) has a **gather phase** then a **move phase**.

### Gather phase — `TidyVars()`
1. `GetVarspathVars()` — all real `.var` files anywhere under `{varspath}` **excluding** the `___XXX___` dirs and reparse points (symlinks). These are loose/untidied vars.
2. `GetAddonpackagesVars()` — all real (non-symlink) `.var` files sitting loose in `AddonPackages` (outside the three link dirs). These are packages the user dropped directly into VaM.
3. Update `varsForInstall.txt`: load existing lines, add every loose AddonPackages var that passes `ComplyVarFile`, `Distinct()`, rewrite. (Remembers "re-install these after tidy".)
4. Concatenate both lists and call the move phase.

### Move phase — `TidyVars(List<string> vars)` (`Form1.cs:117`)
Ensures `___VarTidied___`, `___VarRedundant___`, `___VarnotComplyRule___` exist. For each file:
- **If compliant name:** target = `___VarTidied___\{creator}\{originalFileName}` (creator folder created if missing).
  - If the target already exists → conflict: move the source into `___VarRedundant___` with a `name(1).var`, `name(2).var`… uniqueness counter, log ERROR.
  - Else `File.Move` source → target.
- **If non-compliant name:** move into `___VarnotComplyRule___` with the same `name(n)` uniqueness scheme, log ERROR.
- Progress reported per file.

Notes:
- Uses `File.Move` (fails across volumes; the generic catch logs the message).
- Inputs already exclude symlinks (the enumerators filter reparse points), so tidy never moves a link.

### Enumeration helpers
- `GetVarspathVars()` (`Form1.cs:221`): recursive `*.var` under `{varspath}`, excluding any path containing a `___XXX___` repository dir, excluding reparse points.
- `ExistAddonpackagesVar()` (`:241`) / `GetAddonpackagesVars()` (`:259`): real `.var` under `AddonPackages` outside `___VarsLink___` / `___MissingVarLink___` / `___TempVarLink___`, excluding symlinks. Used to warn "unorganized var files present — run UPD_DB first".

✎ **Rebuild note:** every enumerator is a full recursive directory walk plus a `FileInfo` allocation per file to test the reparse-point attribute — expensive on large libraries. Cache directory scans; watch the filesystem instead of re-walking.

Continue to [03 — Indexing & Content Classification](./03-Indexing-and-Content-Classification.md).
