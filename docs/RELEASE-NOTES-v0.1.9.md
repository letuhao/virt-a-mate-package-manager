# VarVault v0.1.9 — Release Notes

Windows x64 self-contained build of current `main` (DbContext concurrency crash + indexer UNIQUE fix).

## Highlights since v0.1.8
- **Preset activation no longer crashes on large installs.** Write-queue catalog mutations now run in isolated DI scopes (`EnqueueScopedAsync`), so background usage recompute and link persistence no longer share the app-lifetime `DbContext` with the UI thread.
- **Bulk usage recompute deferred.** After activation, `RecomputePackagesAsync` runs on `IJobQueue` instead of blocking the interactive write path — large presets (thousands of packages) return quickly.
- **Indexer UNIQUE failures fixed.** `ExecuteDeleteAsync` on dense dependency/content lists no longer leaves stale tracked rows that caused `UNIQUE constraint failed: Dependency.VarFileId, DependsOnRefKey` on re-index.

## Tests
- `WriteQueue_scoped_jobs_use_isolated_dbcontext`, `Scoped_write_allows_concurrent_read_on_caller_context` (Host).
- `Reapply_with_tracked_dependencies_does_not_hit_unique_constraint`, `Reapply_with_tracked_content_items_does_not_hit_unique_constraint` (Infrastructure).
- `Activate_does_not_share_dbcontext_with_usage_recompute` (E2E).
- `ThumbnailLoaderTests.Decode_runs_off_the_calling_thread` flake fixed.

## Runtime / install
- **Windows x64**, self-contained — no .NET install required.
- Unzip and run `VarVault.App.exe` (keep `VarVault.Indexer.exe` in the same folder).
- Runs **without administrator** (`asInvoker`). Symlink activation needs Windows **Developer Mode**.
- Catalog: `%LOCALAPPDATA%\VarVault` (SQLite), or your data-dir pointer if configured.

## Known limitations
- Out of scope for v1: Hub, load-into-VaM, MMD, packaging, Temp-link producer.
