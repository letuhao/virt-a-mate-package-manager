# VarVault v0.1.4 — Release Notes

Windows x64 self-contained build of current `main` (VaM-load defect scan, duplicate-entry repair, and integrity fingerprinting).

## Highlights since v0.1.3
- **VaM load tab (Health)** — live scan for packages that break `FileManager.RegisterPackage` (duplicate zip entry keys, zip open failures, missing meta). Scan installed (newest first) or all catalog; Reveal / Detail actions.
- **Duplicate-entry repair** — keep-larger policy writes a `.dedup.var` sibling; original retained with lineage. Interactive per-collision picker on Fix (choose survivor per normalized path).
- **Import + fix duplicates** — corrupt/duplicate-entry imports can apply encoding fix then dedup in one pass.
- **CRC spot-check (IDX-10)** — inspect samples entry payload CRC vs central directory; mismatch flags `CorruptZip`.
- **ContentSignature canonicalize (Corr-M1)** — path bytes normalized (`\`→`/`, strip `./`, ASCII case-fold) before hashing; sorted multiset unchanged. **Reindex** after upgrade so dedup keys refresh.
- **Integrity** — `DuplicateEntries` status persisted; scan upgrades mis-tagged `CorruptZip` when CD confirms collisions.

## Runtime / install
- **Windows x64**, self-contained — no .NET install required.
- Unzip and run `VarVault.App.exe` (keep `VarVault.Indexer.exe` in the same folder).
- Runs **without administrator** (`asInvoker`). Symlink activation needs Windows **Developer Mode**.
- Catalog: `%LOCALAPPDATA%\VarVault` (SQLite).

## Known limitations
- Exact duplicate `FullName` entries (not just case/separator twins) may still require a raw central-directory rewrite if `ZipArchive` cannot open the source.
- Narrow windows (&lt; ~800 px): Library side panels still crush the table.
- Out of scope for v1: Hub, load-into-VaM, MMD, packaging, Temp-link producer.
