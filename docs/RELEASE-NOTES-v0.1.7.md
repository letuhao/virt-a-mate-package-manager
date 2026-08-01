# VarVault v0.1.7 — Release Notes

Windows x64 self-contained build of current `main` (import→index correctness, Library refresh after Import).

## Highlights since v0.1.6
- **Import no longer "succeeds" against a pre-copy scan.** When a second index request arrived while the
  worker was busy, the old code coalesced it into the *already running* job — Import waited on a scan that
  started before the files were copied, so packages landed on disk with no `PackageListItem` (Library ghosts).
  The indexer now **queues one follow-up job** (merging scope and `forceFull`) and returns that follow-up's
  job id; the follow-up starts automatically when the active job finishes.
- **Waits are job-scoped.** New `IndexerAwait` helper: Import and Onboarding wait for **their own job id**
  reaching a terminal state, instead of accepting any `Completed`/`Idle` status that happens to fly by.
  Cancelled/failed jobs surface as cancellation/failure rather than a silent success.
- **Import Apply refreshes the UI.** The apply job is named "Applying import", so the shell's
  index-finished reload never fired. `ImportJobRunner` now raises an `AfterCompleted` callback on the UI
  thread and the shell re-loads **Library + Dashboard** (spec §219).
- **Onboarding first scan is a full scan** (`forceFull: true`), so an incremental skip can't leave a
  freshly registered repository half-indexed.
- **Queue depth is honest.** `GetStatus`/`Ping` report a queued follow-up as depth 1, and
  `CancelJob`/`Shutdown` drop the pending follow-up instead of starting it after cancellation.

## Tests
- New `IndexerFollowUpQueueTests` cover: a Start while busy returns a **distinct** follow-up job id with
  `QueueDepth = 1` and runs after the active job (with `forceFull` preserved); and
  `WaitForRepositoryIndexAsync` does **not** return when an unrelated job reaches `Completed`.
- Full suite: **879 passed, 0 failed** (12 skipped) on `dotnet test -c Release`.

## Runtime / install
- **Windows x64**, self-contained — no .NET install required.
- Unzip and run `VarVault.App.exe` (keep `VarVault.Indexer.exe` in the same folder).
- Runs **without administrator** (`asInvoker`). Symlink activation needs Windows **Developer Mode**.
- Catalog: `%LOCALAPPDATA%\VarVault` (SQLite), or your data-dir pointer under `D:\Program Files\VarVault\` if configured.

## Known limitations
- After upgrade, **restart once** so the startup reconcile and the fixed indexer run; then re-try
  Import / Index if Library still looks stale from a v0.1.6 session.
- Out of scope for v1: Hub, load-into-VaM, MMD, packaging, Temp-link producer.
