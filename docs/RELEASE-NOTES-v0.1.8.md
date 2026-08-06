# VarVault v0.1.8 — Release Notes

Windows x64 self-contained build of current `main` (Import false-conflict fix + Import list UX).

## Highlights since v0.1.7
- **Identical vars no longer false-Conflict.** When the catalog `ContentSignature` is null/stale,
  Import re-inspects the on-disk repo copy; matching live signatures reclassify to **Exact → skip**
  instead of forcing a Keep-new / Keep-existing review.
- **Honest conflict advice.** The weak mtime tiebreak no longer claims “contents look equivalent” —
  it says “similar size and entry count” (download date for reference only). True identity stays on
  the Exact lane via `ContentSignature`.
- **Import list ↑/↓ stays in the list.** Arrow keys no longer jump focus into Details / Pagination at
  the edge; selection scrolls into view and keyboard focus is reclaimed. J/K keep the same behavior.
- **Filename column no longer jitters.** The Import table disables horizontal overflow and stretches
  rows so long names ellipsize instead of widening/shrinking the list on every selection change.

## Tests
- `Identical_incoming_with_null_catalog_signature_is_Exact_not_Conflict` (ImportScanFlow E2E).
- Updated `ConflictRecommenderTests` mtime wording assertions.
- ImportViewModel suite green.

## Runtime / install
- **Windows x64**, self-contained — no .NET install required.
- Unzip and run `VarVault.App.exe` (keep `VarVault.Indexer.exe` in the same folder).
- Runs **without administrator** (`asInvoker`). Symlink activation needs Windows **Developer Mode**.
- Catalog: `%LOCALAPPDATA%\VarVault` (SQLite), or your data-dir pointer under `D:\Program Files\VarVault\` if configured.

## Known limitations
- Out of scope for v1: Hub, load-into-VaM, MMD, packaging, Temp-link producer.
