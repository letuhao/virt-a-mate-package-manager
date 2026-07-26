# VarVault v0.1.3 — Release Notes

Windows x64 self-contained build of current `main` (tiering debt payoff, usage feeds, and Import profile tidy).

## Highlights since v0.1.2
- **Pin / Force cold** — VarDetail can Pin hot, Force cold, or return to Auto placement; overrides persist in the catalog.
- **Hot threshold days** — Settings Tiers `HotThresholdDays` drives the primary usage window (and scoring recency horizon).
- **Safer migrates** — FreeSpaceLedger reserves before copy; live free space + soft capacity so sequential moves do not oversubscribe a drive.
- **Add repository** — parse reserve (`MinFreeBytes`), preferred tier, and optional rebalance onto the new repo (queued as a job).
- **VaM log usage** — Settings Tiers can Preview / Import / Browse VaM logs into the usage feed (Missing Analyze stays missing-deps only).
- **Idle auto-rebalance** — optional Automation checkbox; skips Removable/Network media.
- **Import trash originals** — Quick Import after Apply trashes loose profile `.var`s correctly for Exact skips and paths under `___AddonPacksSwitch`; never symlink farms.
- Preset / rescue / Library UI hygiene from the same workstream.

## Runtime / install
- **Windows x64**, self-contained — no .NET install required.
- Unzip and run `VarVault.App.exe` (keep `VarVault.Indexer.exe` in the same folder).
- Runs **without administrator** (`asInvoker`). Symlink activation needs Windows **Developer Mode**.
- Catalog: `%LOCALAPPDATA%\VarVault` (SQLite).

## Known limitations
- Narrow windows (&lt; ~800 px): Library side panels still crush the table.
- Missing-deps are listed/exported; Hub download is out of scope for v1.
- Out of scope for v1: Hub, load-into-VaM, MMD, packaging, Temp-link producer.
