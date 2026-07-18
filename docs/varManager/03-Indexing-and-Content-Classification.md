# 03 — Indexing & Content Classification (UpdDB)

`UpdDB` is how a `.var` becomes database rows. It is the most performance-sensitive operation (thousands of ZIPs opened and scanned) and the producer of everything the browser and dependency graph consume.

## `UpdDB()` — the orchestrator (`Form1.cs:1104`)

1. Enumerate `*.var` recursively under `{varspath}\___VarTidied___`. If none → message + return false. **Only the tidied tree is scanned** — loose files must be Tidied first.
2. For each file: record its base name in `existVars`, call `UpdDB(varfile)`, update progress.
3. `varManagerDataSet.AcceptChanges()`.
4. **Prune:** any `vars` row whose `varName` is not in `existVars` (file vanished) → collect, `Distinct()`, `CleanVars(deletevars)` (removes DB rows + preview pics). This is how the DB self-heals when files are deleted outside the app.

The full "UpdDB" background command is a pipeline: `TidyVars()` → `UpdDB()` → re-install anything in `varsForInstall.txt` (via dependency closure) → `UpdateVarsInstalled()` → `RescanPackages()`.

## `UpdDB(string destvarfilename)` — index one var (`Form1.cs:828`)

1. `basename` = filename without extension; `curpath = Comm.MakeRelativePath({varspath}, dir)` → stored as `vars.varPath` (repo-relative).
2. **If `vars.FindByvarName(basename)` already exists:** only update `varPath` if it changed, then return. **Existing vars are never re-scanned** — UpdDB is idempotent and fast on repeat runs.
   - ✎ Consequence: editing a var's *contents* won't be reflected until its DB row is deleted (via `CleanVar`, or the fixVar re-zip path). A rebuild should key freshness on a content hash or file mtime, not mere name presence.
3. **New var:**
   a. Open as ZIP (SharpZipLib `ZipFile`). On failure → move to `___VarnotComplyRule___`, log WARNING, return.
   b. Require a 3-part name; otherwise the whole population block is skipped (row never added).
   c. From `FileInfo`: `filesize = Length`, `varDate = LastWriteTime`, `addedDate = now`, `creatorName = p0`, `packageName = p1`, `version = int.TryParse(p2) else 1`, `varPath = curpath`.
   d. Require a `meta.json` entry; else close ZIP, move to `___VarnotComplyRule___`, WARNING, return. `metaDate = meta.json entry DateTime`.
   e. **Single pass over all ZIP entries**, classifying each by regex (table below), incrementing per-type counters, and extracting sibling preview JPGs.
   f. Write the counts into the `vars` row (`plugins` = `.cslist` count if any, else `.cs` count).
   g. Insert `scenes` rows for previewable content (all types except assets).
   h. Read `meta.json` text, `Getdependencies(json)` → replace this var's `dependencies` rows.
4. Outer try/catch logs any exception with the file name.

## Content-type classification table (⚠ load-bearing)

Each ZIP entry path is tested (case-insensitive, `Singleline`) against these patterns. **They are sequential independent `if`s, not `else if`** — an entry matching two patterns takes the *last-assigned* `typename`. Each `Regex.IsMatch` compiles a fresh regex per entry per var (a major performance sink).

| Entry-path pattern | `typename` | counter | `isPreset` |
|---|---|---|---|
| `saves/scene/….json` | `scenes` | countscene | false |
| `saves/person/appearance/….json\|vac` | `looks` | countlook | `true` if `.json` |
| `custom/atom/person/(general\|appearance)/….json\|vap` | `looks` | countlook | true |
| `custom/clothing/….vam\|vap` | `clothing` | countclothing | false |
| `custom/atom/person/clothing/….vam\|vap` | `clothing` | countclothing | `true` if `.vap` |
| `custom/hair/….vam\|vap` | `hairstyle` | counthair | false |
| `custom/atom/person/hair/….vam\|vap` | `hairstyle` | counthair | `true` if `.vap` |
| `custom/scripts/….cs` **and** `custom/atom/person/scripts/….cs` | (plugins) | countplugincs | — |
| `…scripts/….cslist` (both roots) | (plugins) | countplugincslist | — |
| `custom/assets/….assetbundle` | `assets` | countasset | — |
| `custom/atom/person/morphs/….vmi\|vap` | `morphs` | countmorphs | `true` if `.vap` |
| `custom/atom/person/pose/….vap` | `pose` | countpose | true |
| `saves/person/pose/….json\|vac` | `pose` | countpose | `true` if `.json` |
| `custom/atom/person/skin/….vap` | `skin` | countskin | true |

The nine counters map to the `vars` columns `scenes, looks, clothing, hairstyle, plugins, assets, morphs, pose, skin`. These are **content counts** (how many items of each type the package contains), shown in the grid and used by cleanup heuristics (e.g. OldVersionVars treats "content packages" as `plugins <= 0 || scenes > 0 || looks > 0`).

## Preview image extraction

When an entry classified to a non-empty `typename` has a sibling `{entryPathWithoutExt}.jpg` in the ZIP, it's extracted to:

```
{varspath}\___PreviewPics___\{typename}\{basename}\{typename}{counter:000}_{origJpgNameLower}.jpg
   e.g.  ___PreviewPics___\scenes\Ruthven2000.LittleRed.1\scenes001_redhood.jpg
```

- `counter` is that type's running count (zero-padded to 3 digits).
- Streamed in 2048-byte chunks; skipped if already on disk.
- The extracted filename (or `""` if no sibling JPG) is stored as `scenes.previewPic`.

## `scenes` rows

For every entry classified to `{scenes, looks, clothing, hairstyle, morphs, pose, skin}` (**assets excluded**), a row is added:

```
scenes.AddscenesRow(basename, typename, isPreset, zipEntryPath, previewPicFilename)
```

This table is the source of truth for the preview/scene browser (see [06](./06-Preview-Scene-Analysis-and-Loading.md)) — the app no longer walks the `___PreviewPics___` folders at browse time.

## Dependencies

After the entry pass, the `meta.json` text is read fully and passed to `Getdependencies` (regex scan — see [04](./04-Dependency-Resolution-and-Versioning.md)). Existing `dependencies` rows for this var are deleted and one row per distinct dependency string is inserted.

## Performance concerns (✎ rebuild notes)

- **Fresh regex per entry per var** — dozens of `Regex.IsMatch` calls × thousands of entries × thousands of vars. Precompile the regex set once (or use a single alternation / a switch on path prefixes).
- **Per-var TableAdapter `Update`** for `scenes`, `vars`, `dependencies` — many small Access round-trips. Batch inserts in a transaction.
- **2 KB streaming buffer** for JPG extraction — use a larger buffer or `ZipArchiveEntry.Open().CopyTo`.
- **Whole-file ZIP open per new var** — unavoidable for a first index, but only `meta.json` + entry names + sibling JPGs are needed; use `System.IO.Compression.ZipArchive` and read just those entries rather than any full extraction.
- **Idempotency by name only** — add a content hash/mtime so edited vars re-index.
- **Parallelism** — indexing is embarrassingly parallel across files; the legacy is strictly sequential on one worker.

Continue to [04 — Dependency Resolution & Versioning](./04-Dependency-Resolution-and-Versioning.md).
