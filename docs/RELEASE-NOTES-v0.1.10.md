# VarVault v0.1.10 — Release Notes

Windows x64 self-contained build of current `main` (large-preset activation scale + installed-dot UI refresh).

## Highlights since v0.1.9
- **Large preset activation scales to 70K+ catalogs.** Batched hottest-copy resolution (~8 queries for 4K packages vs thousands), chunked usage record/recompute (500-package batches), debounced usage-feed coordinator, and set-based active-profile read-model refresh with a partial index on `PackageListItem.IsActive`.
- **No more stuck “Activation usage recompute” job.** Usage scoring runs on the bulk write-queue path (fire-and-forget, coalesced) instead of monopolizing the interactive queue via `IJobQueue`.
- **Green installed dot updates immediately.** Library install/uninstall and preset activation now refresh loaded rows in place — no manual page refresh or navigation required.

## Tests
- `UsageRecordManyTests` (500-package recompute, 1200-id chunked recompute).
- `ProfilePackageLinkTests.Refresh_at_1k_rows_only_flips_active_set`.
- `ActivationUsageFeedTests`, full `Activation` E2E suite.
- `LibraryInstallRefreshTests.Install_selected_refreshes_loaded_row_installed_state_without_full_refresh`.

## Runtime / install
- **Windows x64**, self-contained — no .NET install required.
- Unzip and run `VarVault.App.exe` (keep `VarVault.Indexer.exe` in the same folder).
- Runs **without administrator** (`asInvoker`). Symlink activation needs Windows **Developer Mode**.
- Catalog: `%LOCALAPPDATA%\VarVault` (SQLite), or your data-dir pointer if configured.
- **New migration** on first launch: partial index `IX_PackageListItem_IsActive` (automatic via EF).

## Known limitations
- Out of scope for v1: Hub, load-into-VaM, MMD, packaging, Temp-link producer.
