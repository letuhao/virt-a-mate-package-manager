# VarVault v0.1.0 — Release Notes

**First usable release.** VarVault manages Virt-a-Mate `.var` packages as tiered, multi-drive object storage with
dependency management, dedup, and CJK-encoding fixes. This build is verified working end-to-end against a real
277-/307-var corpus across two drives plus a live VaM install.

## What works (verified on real data)
- **Multi-repo library** — register repositories on different drives; index `.var` files (creator/package/version,
  content types, sizes, previews, dependencies, encoding). Verified: 300+ vars across two drives.
- **Activation into VaM** — build a preset and materialize per-var NTFS symlinks into the game folder, with a
  loading-profile switch. Verified: 8 symlinks into a live `F:\VaM` install, 0 missing.
- **Deduplication & reclaim** — exact content-signature dedup groups + reclaimable-space accounting. Verified: 31
  groups on the real corpus.
- **Proposals** — analyzer proposes removals/migrations/fixes; nothing runs unattended (you approve).
- **Health / CJK fix** — detects GBK/Shift-JIS mis-encoded entries and offers safe fixes.
- **Missing dependencies** — lists referenced-but-absent packages (most-needed first, capped for speed) with export.
- **Dashboard, Analytics, Tiering, Trash/backup, Activity, Settings** — all screens load real data.

## New in this release (newcomer UX pass — docs 27/28)
- Human-readable sizes everywhere (`3.8 GB`, `427 KB`) — no more raw byte numbers (shared `ByteSize` humanizer).
- First-run **onboarding** auto-opens on an empty install; app now lands on the **Dashboard**.
- Clear **empty-state guidance** on Activity and Presets; repositories are named after their folder.
- Missing-deps screen: contextual header + "most-needed first" + show-all toggle (load ~1.5 s → ~0.35 s).
- HDD-only hint on Tiering; consistent "Add repository" action; Dashboard missing-deps tile relabeled to avoid
  confusion with the Missing screen's total.
- **Responsive Library filter bar** — wraps instead of overlapping on narrow windows.

## Runtime / install
- **Windows x64**, self-contained single file — no .NET install required. Run `VarVault.App.exe`.
- Runs **without administrator** (`asInvoker`, no UAC prompt). Symlink activation uses Windows **Developer Mode**;
  if it's off, activation fails gracefully with a hint (browsing/indexing/dedup all work regardless).
- Catalog is stored per-user under `%LOCALAPPDATA%\VarVault` (SQLite).

## Quality gate
- **608 automated tests pass** (unit, integration, real-SQLite, real-data E2E, architecture rules).
- Real windowed app boot-tested from the Release build and the published single-file artifact.

## Known limitations (non-blocking)
- **Layout is not fully responsive** below ~800 px window width — the Library's side panels crush the table. A
  responsive redesign is planned; use a normal desktop window size for now.
- Missing-dependency downloads are not fetched in-app (export the list and download from the Hub).
- Out of scope for v1 (by design): Hub integration, load-into-VaM, MMD, packaging.
