# VarVault v0.1.6 — Release Notes

Windows x64 self-contained build of current `main` (catalog/import resilience, Library heal, Trash selection).

## Highlights since v0.1.5
- **Indexer no longer aborts whole jobs** — re-persist clears dependencies before insert (UNIQUE), and `RemoveVarFiles` clears lineage FKs first. Prune/refresh errors no longer kill an otherwise successful scan.
- **Startup catalog reconcile** — before Index All, mark offline mounts offline, prune vanished online files, and heal missing `PackageListItem` rows so Library matches the catalog.
- **Library vs Exact gap** — dirty drain peeks → refreshes read model → then acks; heals packages that exist on disk/`VarFile` but were missing from Library.
- **Import tidy disk guard** — do not soft-trash originals unless the surviving repo copy still exists on disk.
- **Trash UI** — Select page / Select all / Clear; restore uses the selected id snapshot; list reloads after restore; trash query returns the full set (not a 100-row page).

## Runtime / install
- **Windows x64**, self-contained — no .NET install required.
- Unzip and run `VarVault.App.exe` (keep `VarVault.Indexer.exe` in the same folder).
- Runs **without administrator** (`asInvoker`). Symlink activation needs Windows **Developer Mode**.
- Catalog: `%LOCALAPPDATA%\VarVault` (SQLite), or your data-dir pointer under `D:\Program Files\VarVault\` if configured.

## Known limitations
- After upgrade, **restart once** so startup reconcile + the fixed indexer run; then re-try Import / Index if Library still looks stale.
- Out of scope for v1: Hub, load-into-VaM, MMD, packaging, Temp-link producer.
