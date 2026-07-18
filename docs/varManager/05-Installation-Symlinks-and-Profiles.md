# 05 — Installation, Symlinks & Profiles

"Installing" a package never copies it. It creates an NTFS **symbolic link** in `AddonPackages` pointing at the real `.var` in the repository. This chapter covers the install primitive, the Win32 link/reparse layer (`Comm.cs`), temporary links for scene loading, AddonPackages profile switching, and the ZIP handling (`ZipHandler.cs`).

## `VarInstall(varName, bTemp = false, operate = 1)` — the atomic install (`Form1.cs:1235`)

Returns `0` = fail, `1` = success, `2` = already installed.

1. Acts only if `operate >= 1` and `vars.FindByvarName(varName)` exists.
2. Link path: `{vampath}\AddonPackages\___VarsLink___\{varName}.var`, or `…\___TempVarLink___\{varName}.var` when `bTemp`.
3. If a `{link}.disabled` marker exists and `operate == 1`, delete it (re-enable).
4. If the link already exists → return `2`.
5. Target = `{varspath}\{varsRow.varPath}\{varName}.var` (the real repo file).
6. `Comm.CreateSymbolicLink(link, target, File)` — an NTFS **file** symlink. On failure show the Win32 error, return `0`.
7. If `operate == 2`, create an empty `{link}.disabled` (install-but-disabled).
8. `Comm.SetSymboLinkFileTime(link, now, now)` — stamps the **link node itself** (not the target).
9. Set `varsRow.installedDate = now("yyyy/MM/dd HH:mm:ss")`, `varsTableAdapter.Update`.

### Install button flow (`buttonInstall_Click`, `:2558`)
Gather selected grid rows not already installed; **cap 500**; confirm; expand with `VarsDependencies` (transitive closure); `VarInstall` each; then `UpdateVarsInstalled()`.

### Install-state tracking
- `GetInstalledVars()` (`:557`): `Dictionary<varName → linkPath>` of symlink `.var` files under `___VarsLink___` plus top-level `AddonPackages`. (Excludes `___MissingVarLink___` and `___TempVarLink___`.)
- `UpdateVarsInstalled()` (`:579`): rebuilds the `installStatus` table — `DeleteAll()` then, for each installed var whose base name exists in `vars`, add `(varName, Installed=true, Disabled = File.Exists(link+".disabled"))`; then refresh the grid. ✎ This delete-all-then-reinsert snapshot is race-prone; a rebuild should upsert a 1:1 child of `vars`.
- `IsVarInstalled(varName)` (`:2522`): reads `installStatus.FindByvarName`.

## Removal flows

- **`UnintallVars(List<string>)` (`:466`)** — compute `ImplicatedVars` cascade → confirm via `FormUninstallVars` → for each: delete the symlink (if present) and clear `installedDate`. **DB rows and previews are kept** (var stays in repo, just not installed).
- **`DeleteVars(List<string>)` (`:511`, soft delete, cap 50)** — `ImplicatedVars` → confirm → delete link → `File.Move` the real repo file into `___DeletedVars___\{name}.var` → `CleanVar(name)` (purge DB + previews).
- **`CleanVars` / `CleanVar` (`:1146`/`:1194`)** — delete matching rows from `dependencies`, `scenes`, `vars`, then `DelePreviewPics`; bulk variant also runs `FixPreview`.
- **`DelePreviewPics(varname)` (`:446`)** — recursively delete `___PreviewPics___\{type}\{varname}` for each of `{scenes, looks, hairstyle, clothing, assets, morphs, skin, pose}`.

## Repair operations

- **`FixRebuildLink()` (`:1521`)** — after the repository moves, recreate broken links: for each install/`___MissingVarLink___` link that is a reparse point, resolve its target via `Comm.ReparsePoint`, look up the var, delete the stale link, recreate it to the current repo path, restore file times.
- **`FixPreview()` / `ReExtractedPreview()` (`:1607`/`:1557`)** — re-extract missing preview JPGs from the source var (opens the ZIP once per missing scene row — a perf hazard).

## Temporary links for scene loading

Scene loading installs missing dependencies as **temporary** symlinks that VaM reads once, then get cleaned up.

- `InstallTemp(string[] varNames, ref bool rescan)` (`:3298`) — transitive-close the requested names (`VarsDependencies`), subtract already-installed, ensure `___TempVarLink___`, `VarInstall(name, bTemp:true)` each; set `rescan = true` if anything new was linked.
- `AddDeleteTemp()` (`:3255`) — ensures `___TempVarLink___`, returns the lowercased names of the symlink files currently there (cleanup candidates).
- `DeleteTempThread(object)` (`:3269`) — background thread: sleep 2 s; **while `loadscene.json` still exists** (VaM hasn't consumed it) keep waiting; once it's gone, sleep another 20 s, then best-effort delete each temp link. (Temp links live only long enough for VaM to load the scene.)
- `RescanPackages()` (`:3033`) — if a `vam` process is running, write `{vampath}\Custom\PluginData\feelfar\loadscene.json` = `{"rescan":"true"}` to trigger VaM's in-game rescan.

See [06](./06-Preview-Scene-Analysis-and-Loading.md) for how these fit into the full `GenLoadscenetxt` load hand-off.

## AddonPackages profiles (the "switch" feature)

`AddonPackages` is managed as a **directory symlink** into `{vampath}\___AddonPacksSwitch ___\{profile}`. Each profile is an independent installed-package set; `default` always exists.

- **Bootstrap** (`Form1_Load`, `:634–697`): create `___AddonPacksSwitch ___`; if a real `AddonPackages` directory exists (not a reparse point), merge its contents into `…\default` (`Comm.DirectoryMoveAll`), rename the leftover to `AddonPackages_{timestamp}`, and create the directory symlink to `…\default`. Validate/repair the reparse target. Select the profile matching the current link target.
- **Switch** (`varpacksSwitch(sw)`, `:2993`): ensure `…\{sw}` exists; if the current link target ≠ target profile, delete the link and recreate it pointing at `…\{sw}`; then `UpdateVarsInstalled()` + `RescanPackages()`.
- **CRUD**: Add (`FormSwitchAdd`), Delete (guarded — never `default`, confirm, `Directory.Delete(true)`), Rename (`FormSwitchRename`, `Directory.MoveTo`).

## The Win32 link / reparse layer — `Comm.cs`

`Comm` declares the primitives; the **call sites and the hardlink-vs-symlink decision live in Form1** (which always uses symlinks — `CreateHardLink` is declared but unused).

### P/Invoke surface
`CreateHardLink`, `CreateSymbolicLink` (flags `File=0`, `Directory=1`, `AllowUnprivilegedCreate=2`), `DeviceIoControl` (`FSCTL_GET_REPARSE_POINT`), `CreateFile`, `OpenProcessToken`/`LookupPrivilegeValue`/`AdjustTokenPrivileges` (to enable `SeBackupPrivilege`), `SetFileInformationByHandle`, `CloseHandle`.

### `ReparsePoint(path)` — resolve a link's target
Enable `SeBackupPrivilege` → `CreateFile` with `FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS` (opens the link itself, and permits directories) → `DeviceIoControl(FSCTL_GET_REPARSE_POINT)` → parse `REPARSE_DATA_BUFFER` for symlink vs mount-point vs junction; resolve **relative** symlink targets against the link's own directory; strip `\??\` prefixes. `ERROR_ACCESS_DENIED (5)` → returns empty; other errors → `Win32Exception`.

### `SetSymboLinkFileTime(path, create, lastWrite)` — stamp the link node
Opens the link with `FILE_FLAG_OPEN_REPARSE_POINT` and `SetFileInformationByHandle(FileBasicInfo)` so the **symlink's own** timestamps mirror the source var's dates (VaM sorts by date; installed links must look like the real files).

### Other helpers
`LocateFile` (`explorer.exe /select`), `ValidFileName` (strip `Path.GetInvalidFileNameChars()`), `DirectoryMoveAll` (recursive merge-move — fragile naive string-replace for the destination path), `MakeRelativePath` (relative symlink targets via `Uri.MakeRelativeUri`, so links survive library relocation).

### ✎ Rebuild note — cross-platform
Replace the entire P/Invoke layer with `File.CreateSymbolicLink` / `Directory.CreateSymbolicLink` and `FileSystemInfo.ResolveLinkTarget` (.NET 6+). Hard links still need P/Invoke (`CreateHardLink`) or POSIX `link()`. "Set timestamp on the link itself" has no portable equivalent (POSIX times follow the link). Fix the 32-bit-centric `handle.ToInt32() >= 0` validity check. Requires Windows **Developer Mode** (or elevation) to create symlinks unprivileged.

## ZIP handling — `ZipHandler.cs` (static)

VAR files are plain ZIPs. Two strategies coexist: SharpZipLib (managed, encoding-aware, used for reads/verification) and external **7-Zip** (`C:\Program Files\7-Zip\7z.exe`, the production extract/re-zip path).

### `ExtractAndRezip(zipFilePath)` — in-place VAR normalization
Used by the "fixVar" grid action to rebuild a corrupt/oddly-encoded var:
1. Validate exists; derive a unique extract folder (`name`, `name_1`, …).
2. `File.Move` original → `name_{yyyyMMddHHmmss}.ext` (kept as backup).
3. `ExtractZipFileBy7Z(backup, folder)`.
4. `CreateZipFile7Z(folder, originalName)`.
5. Delete the folder, delete the backup.
- **Rollback on failure:** move the backup back if the target is missing; delete the extract folder; rethrow.

### `ExtractZipFileBy7Z` (primary extractor)
`7z.exe x "<zip>" -o"<out>" -y -aot` (`-aot` = auto-rename existing on collision). Redirect stdout/stderr, `UseShellExecute=false`, `CreateNoWindow`. Throw on non-zero exit. Chosen over SharpZipLib for robust CJK filename handling and speed.

### `CreateZipFile7Z` (primary compressor)
`7z.exe a -tzip "<out>" "<src>\*" -mx5 -mm=Deflate`. ⚠ **Must be ZIP + Deflate + level 5 for VaM compatibility — never LZMA.**

### Encoding detection (for CJK entry names)
- `DecodeFileName`: if the entry's general-purpose **bit 11 (UTF-8/EFS)** is set → force UTF-8. Otherwise round-trip: `raw = Encoding.Default.GetBytes(entry.Name)`, then `candidate.GetString(raw)`.
- Candidate order: UTF-8, UTF-16LE, UTF-16BE, **GBK**, **Shift_JIS**, **EUC-KR**, **Big5**, Windows-1252, Default (whole-archive detector also tries ISO-2022-JP).
- Per-entry mode: first candidate that round-trips byte-identically **and** yields no control/`?`/`�` chars wins. Whole-archive mode: the candidate minimizing total penalty across all entries.
- ✎ **Rebuild hazard:** on .NET Core/5+ `Encoding.Default` is UTF-8 (not the OS ANSI codepage), which **breaks the round-trip trick**. Register `CodePagesEncodingProvider` and capture the raw entry-name **bytes** directly instead of round-tripping through a string.

### Other notes
- **No single-entry extraction** in the legacy — every method extracts the whole archive. A rebuild that only needs `meta.json`/previews should open just those entries via `ZipArchive`.
- Verification is **size-only** (no CRC/hash) — silent corruption of equal-length files is undetectable.
- `StandardOutput.ReadToEnd()` then `WaitForExit()` can deadlock on very large archives (only one pipe drained) — drain both streams async.
- Logs to `Console`, not `SimpleLogger`.

Continue to [06 — Preview, Scene Analysis & Loading](./06-Preview-Scene-Analysis-and-Loading.md).
