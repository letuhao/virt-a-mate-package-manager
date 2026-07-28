# VarVault v0.1.5 — Release Notes

Windows x64 self-contained build of current `main` (Import post-copy indexing fix).

## Highlights since v0.1.4
- **Fix Import → library / trash originals** — Apply was copying vars into the repo, then indexing via the in-process client while the out-of-process `VarVault.Indexer` owned the catalog. Indexing failed, Apply aborted before trash-originals, and new files never appeared in the library. Import now routes through the same indexer hub as Library / Index.
- **Resilient Apply** — if post-copy indexing still fails, copies and delete-originals still complete; status notes that indexing was deferred.

## Runtime / install
- **Windows x64**, self-contained — no .NET install required.
- Unzip and run `VarVault.App.exe` (keep `VarVault.Indexer.exe` in the same folder).
- Runs **without administrator** (`asInvoker`). Symlink activation needs Windows **Developer Mode**.
- Catalog: `%LOCALAPPDATA%\VarVault` (SQLite).

## Known limitations
- Vars already copied under 0.1.4 but missing from the library: run **Index** (or restart) once.
- Out of scope for v1: Hub, load-into-VaM, MMD, packaging, Temp-link producer.
