# VarVault v0.1.2 — Release Notes

Windows x64 self-contained build of current `main` (after Edit meta, out-of-process indexer, and Import activate modes).
Tags `v0.1.0` / `v0.1.1` already existed locally on older commits; this release is the next public cut.

## Highlights since v0.1.1
- **Edit meta GUI** — rewrite `meta.json` in a `.var` (deps + common fields), trash the previous file, refresh catalog; paged/filterable deps list for large meta.
- **VarDetail usability** — fixed-height dialog with scrollable body; Dependencies as Direct / Reverse / Saves compact rows (not heavy card stacks).
- **Import activate modes** — Off / copied → `Imported` preset / activate into the **active loading session**.
- **Out-of-process indexer** — `VarVault.Indexer.exe` must sit beside `VarVault.App.exe` (included in the zip).
- Library/import QoL: Quick Import tidy, installed-deps repair, encoding jobs, alias/symlink resolve UX, and related fixes.

## Runtime / install
- **Windows x64**, self-contained — no .NET install required.
- Unzip and run `VarVault.App.exe` (keep `VarVault.Indexer.exe` in the same folder).
- Runs **without administrator** (`asInvoker`). Symlink activation needs Windows **Developer Mode**.
- Catalog: `%LOCALAPPDATA%\VarVault` (SQLite).

## Known limitations
- Narrow windows (&lt; ~800 px): Library side panels still crush the table.
- Missing-deps are listed/exported; Hub download is out of scope for v1.
- Out of scope for v1: Hub, load-into-VaM, MMD, packaging.
