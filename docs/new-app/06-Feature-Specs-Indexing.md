# 06 — Feature Specs: Indexing & Content

Detailed specs for the indexing subsystem — how a `.var` on disk becomes catalog rows. Builds on the [data architecture v2](./03-Data-Architecture.md). This is the **exemplar** for the feature-spec series; format:

> **[ID] Name** (priority) — **Purpose · Trigger · Inputs/Preconditions · Algorithm/Workflow · Entities written · Edge cases · Acceptance.**

Overview: indexing runs in a **separate `VarVault.Indexer` process** as a **durable, per-physical-drive-parallel, incremental** pipeline. Discovery streams into a scan ledger; each changed var is opened **once**; raw facts + representative thumbnail commit to `RawStored`; dependency resolution and other derived work are **later, paged, resumable** phases. (Amendments A12–A15, 2026-07-21.)

---

## IDX-1 — Staged repository scan  [core]

**Purpose:** discover and index every `.var` in a repository without blocking the UI, thrashing HDDs, or unbounded RAM.
**Trigger:** repo registered; manual "rescan"; filesystem-watch event; app start (incremental pass) — all via indexer IPC.
**Inputs/Preconditions:** an online, enabled `Repository`; its `MediaType` known; indexer process holding the catalog write lease.

**Algorithm:**
```
scanRepository(repo, cancel, progress):
    run = beginScanRun(repo)                      # durable generation + phase = Discovering
    # cheap discovery — directory entries only, NO file opens; upsert in small batches (no full path set in RAM)
    for entry in enumerateFiles(repo.MountPath, "*.var", recursive,
                             skipDirs = quarantine + link dirs, skipReparsePoints = true):
        upsertDiscovery(run, entry)               # path/size/mtime/quarantine; mark SeenInGeneration
        progress.report(discovered++)
    markVanished(run)                             # SQL: rows not seen in this generation
    # INGEST (one-handle, bounded channels, per-drive DOP from ParallelismPolicy):
    while claimWork(run, lease) as batch:         # Pending/Failed-with-retries; capacity-bounded
        parallelPerDrive(batch, repo.MediaType):
            with oneSeekableHandle(file):
                cd = readCentralDirectory(handle) # signatures + classify + encoding
                meta, rawDeps = streamJson(handle)# caps; no ReadToEnd of unbounded entries
                thumb = extractRepresentativeJpg(handle)  # optional; size/pixel capped
            persistRawBatch(...)                  # Package/VarFile/ContentItem/Dependency(raw)/thumb ref
            mark RawStored; dirtyPackage(id)      # durable dirty row
        wal_checkpoint; progress.report()
    if repo online: pruneVanished(run)
    phase = ResolveDependencies (paged SQL) → RefreshReadModel (chunked) → Usage (optional)
```
- **One seekable handle per changed var** covers central directory, meta, embedded refs, signatures/encoding, and the **representative** thumbnail (A14). Do not reopen for preview.
- **Raw dependency strings** are stored unresolved during ingest; graph resolution is a separate paged phase (A13).
- **Incomplete / non-`RawStored` rows** never authorize dedup, delete, or migration.

**Entities written:** `ScanRun`, ingest-state on `VarFile`, `Package`, `VarFile`, `ContentItem`, `Dependency` (unresolved), `PackageContentCount`, thumbnail shard + `PreviewThumbRef`, later `PackageListItem` / resolution bits.
**Edge cases:** repo goes offline mid-scan → stop, keep partial, resume later; file locked → retry/skip+flag; worker crash → expire leases, resume; symlink/reparse → skipped.
**Acceptance:** working set bounded by channel capacity (not repo size); HDD DOP=1; second scan of unchanged repo opens 0 files; kill worker mid-run and restart → no duplicate rows, dirty packages refresh; catalog browsable as `RawStored` rows accumulate.

---

## IDX-2 — Incremental freshness  [core]

**Purpose:** re-index only what actually changed; never re-read an unchanged library.
**Trigger:** any scan.
**Algorithm:** a VarFile is **fresh** (skip) iff `(SizeBytes, FileMtime)` from the **directory entry** equals the stored values AND `QuickHash` (only if already computed) matches. Freshness decision requires **no file open** (proven necessary by the [perf spike](./05-Perf-Spike-Results.md) — opening 700K files on HDD is ~2 h of seeks).
**Edge cases:** same-size+same-mtime mid-file change (bit-rot, restore-preserved mtime) → `QuickHash` includes central-directory offset/count to catch structural change; a periodic **deep re-hash** of single-copy/irreplaceable files catches silent corruption. FAT/exFAT/network mtime has 2 s granularity → apply a tolerance window for removable repos.
**Acceptance:** second scan of an untouched 70K repo completes in seconds and opens no files.

---

## IDX-3 — Per-physical-drive parallel scheduler  [should]

**Purpose:** maximize throughput on NVMe/SSD, avoid head-thrash on HDD.
**Algorithm:** group work by the **physical drive** backing each repo (not per-repo). Concurrency per drive = `{HDD:1, SSD:4, NVMe:8, Network:2}` by `MediaType`; a global cap = CPU-based. Signature/preview I/O (Stage 2) obeys the same per-drive limits.
**Edge cases:** two repos on the same physical disk share one HDD lane; media type unknown → treat as HDD (safe).
**Acceptance:** indexing N NVMe repos scales ~linearly; a single HDD repo never runs concurrent random reads.

---

## IDX-4 — Identity parse & fold key  [core]

**Purpose:** derive stable logical identity from the **verbatim filename** (never `meta.json`).
**Algorithm:**
```
parseIdentity(fileName):                 # fileName without ".var"
    parts = splitLast2Dots(fileName)     # Creator . Package . Version  (Creator/Package may contain dots? no — 3-part rule)
    if not (parts.length==3 and parts[2] matches ^[0-9]+$): return Unrecognized(fileName)
    creator, pkg, verTok = parts
    versionSort = parseToLongClamped(verTok)          # clamp, never overflow
    varName = fileName                                 # verbatim, ".007" preserved
    identityKey = unicodeCaseFold(NFC(varName))        # app-computed, non-ASCII safe
    return Identity(varName, identityKey, creator, pkg, verTok, versionSort)
```
Read `meta.json`'s `creatorName`/`packageName` into `MetaCreator`/`MetaPackage`; set `MetaDivergent` if they differ from the filename. **Identity/dependency matching uses `identityKey` only.**
**Edge cases:** 11+ digit version → clamp `versionSort`, keep `verTok` string authoritative; non-ASCII creator (CJK) → fold key via NFC + Unicode case-fold; non-compliant name → `Package=null`, `VarFile` retained in an "unrecognized" projection with a signature-based identity suggestion.
**Acceptance:** `Creator.Pkg.007` and `creator.pkg.007` map to the **same** `identityKey`; `.7` and `.007` stay distinct; no Int32 overflow ever.

---

## IDX-5 — Content classification  [core]

**Purpose:** tag each var's content types (scene/look/clothing/hairstyle/morphs/pose/skin/plugins/assets) with counts + preview candidates.
**Algorithm:** one pass over central-directory entry paths; classify each by a **precompiled** rule set (path-prefix + extension → type), incrementing per-type counters and marking preset-ness. (Modernizes the legacy per-entry regex — see `docs/varManager/03`.) A sibling `{entry}.jpg` marks a preview candidate for that item. `PrimaryType` = deterministic precedence `scene > look > clothing > hair > morph > pose > skin > plugin > asset`.
**Entities written:** `ContentItem` (per `VarFileId`), `PackageContentCount` (per Package, from canonical).
**Edge cases:** multi-type vars (scene bundling everything) → all types counted, `PrimaryType` by precedence; gender derived from path fragments with a **confidence** score (fragile — never authoritative); assets excluded from preview rows.
**Acceptance:** classification matches the legacy type table for a corpus of real vars; runs without recompiling a regex per entry.

---

## IDX-6 — Content signature & lineage (dedup fingerprints)  [core] ⚙️ flagship

**Purpose:** compute the fingerprints that make dedup **structural, not byte-level** — so copies that are identical inside but packaged into different-sized zips are detected (the exact case where whole-file checksums fail).
**Trigger:** Stage 2, per new/changed VarFile.
**Algorithm:**
```
computeSignatures(varPath):
    cd = readCentralDirectory(varPath)          # Zip64-aware; no decompression
    triples = []                                 # (rawNameBytes, uncompressedSize, crc32)
    payload = []                                 # same, excluding meta.json
    noPath  = []                                 # (uncompressedSize, crc32) only
    for e in cd.entries:
        if e.isDirectory: continue               # exclude dir entries (tooling-dependent)
        name = e.rawNameBytes                     # RAW BYTES — do NOT decode (mojibake-safe)
        triples.append((name, e.uSize, e.crc32))
        noPath.append((e.uSize, e.crc32))
        if normalize(name) != "meta.json": payload.append((name, e.uSize, e.crc32))
    sortAsMultiset(triples); sortAsMultiset(payload); sortAsMultiset(noPath)
    return {
      ContentSignature:       hash(triples),      # primary dedup key
      PayloadSignature:       hash(payload),      # same content / different meta
      ContentSignatureNoPath: hash(noPath),       # relate original ↔ encoding-fixed (paths differ)
    }
```
- **Raw bytes, not decoded strings** — decoding mojibake would be lossy/non-deterministic on exactly the CJK vars we target (review finding VaM-5).
- **Zip64-aware** central-directory reader (large asset vars).
- **Multiset** (keep duplicate entries), normalized separators, directory entries excluded.
- `ContentSignatureNoPath` links an encoding-fixed var to its broken original (their paths differ, so the primary signature won't match) → set `FixedFromVarFileId`.

**Entities written:** `VarFile.{ContentSignature, PayloadSignature, ContentSignatureNoPath, FixedFromVarFileId}`.
**Edge cases:** CRC-32 is 32-bit → signatures are for **grouping only**; deletion always re-verifies with full `ContentHash` (IDX cross-ref → DEDUP-*). Truncated var with a valid central directory but corrupt payload → flagged by integrity check (IDX-10) before it can be a dedup keeper.
**Acceptance:** two vars with identical inner content but different compression/order/timestamps share a `ContentSignature`; a mojibake var and its correctly-named twin still match; grouping 1.5M VarFiles by signature runs in ~2 ms (spike-proven with the index).

---

## IDX-7 — Preview extraction & thumbnail store  [core]

**Purpose:** cache preview images for the visual gallery.
**Algorithm:** Stage 2, per VarFile: for each preview-candidate `ContentItem`, decompress its sibling `.jpg`, downscale to a single thumbnail resolution, write into a **packed store** (blob file / dedicated thumb DB keyed by PackageId) on the **fastest tier** — not 700K loose files (review Perf-MED12). Pick the package's representative preview by `PrimaryType`; preview-less types (asset/morph/plugin) get a type placeholder.
**Entities written:** `ContentItem.PreviewThumbRef`, `PackageListItem.PreviewThumbRef`.
**Edge cases:** no sibling jpg → placeholder; corrupt jpg → placeholder + flag; decode off the UI thread with scroll-ahead prefetch.
**Acceptance:** gallery scroll stays smooth at 60 fps over 100K thumbnails; no per-tile file-open storm.

---

## IDX-8 — Encoding health detection  [core] ⚙️ flagship

**Purpose:** detect vars whose ZIP entry names are in a legacy CJK codepage without the UTF-8 flag — the ones VaM **fails to load**.
**Trigger:** Stage 2, per VarFile.
**Algorithm:**
```
detectEncoding(cd):
    broken = 0; codepageVotes = {}
    for e in cd.entries:
        if e.utf8Flag: continue                       # bit 11 set → already correct
        raw = e.rawNameBytes
        if isPureAscii(raw): continue                 # ascii is codepage-agnostic
        # try candidates in priority order
        best = null
        for cp in [GBK/GB18030, Shift_JIS, Big5, EUC-KR, UTF-8]:
            s = decode(raw, cp)
            if roundTrips(s, cp, raw) and not hasReplacementOrControl(s):
                best = cp; break                       # first lossless, clean candidate wins
        if best == null: broken++; else codepageVotes[best]++
    if codepageVotes empty and broken==0: return (Ok, null, 0)
    detected = argmax(codepageVotes)
    if broken>0 and codepageVotes nonempty: return (PartiallyBroken, detected, broken)
    if broken>0: return (NeedsFix, null, broken)       # undetectable codepage
    return (NeedsFix, detected, 0)                      # needs re-encode to UTF-8
```
- Superset of the Boss963 tool (which hardcoded GB2312) — auto-detects across codepages, and records **which** one.
- ⚠️ On .NET, register `CodePagesEncodingProvider`; capture **raw entry-name bytes** (don't rely on `Encoding.Default` round-trip, which is UTF-8 on .NET Core).
**Entities written:** `VarFile.{EncodingHealth, DetectedCodepage, BrokenEntryCount}`.
**Edge cases:** mixed-codepage var → per-entry voting, `PartiallyBroken`; low vote confidence → surface for review, never auto-fix; ASCII-only var → `Ok`.
**Acceptance:** correctly classifies a corpus of known-broken CJK vars by codepage; zero false-positives on healthy UTF-8 vars.

---

## IDX-9 — Encoding auto-fix (re-encode to UTF-8)  [core] ⚙️ flagship

**Purpose:** repair a broken-encoding var so VaM loads it — automatically, in batch, safely.
**Trigger:** user "fix" (single/batch/"fix all"); optional fix-on-import (auto/prompt/flag — configurable).
**Preconditions:** `EncodingHealth ∈ {NeedsFix, PartiallyBroken}` with a confident `DetectedCodepage`.
**Algorithm:**
```
fixEncoding(varFile):
    tmpOut = tempPath(".partial")
    open source zip reading entry names via DetectedCodepage (→ correct Unicode names)
    write NEW zip (ZIP + Deflate, entry names UTF-8 with bit-11 set) to tmpOut   # VaM-compatible; never LZMA
    validate(tmpOut): VaM-compat check — ZIP+Deflate, UTF-8 flag, no Zip64/data-descriptors, meta.json present
    if not valid: discard tmpOut; flag for manual review; RETURN (original untouched)
    atomicRename(tmpOut → new fixed VarFile path)      # NEVER overwrite in place
    keep original as first-class retained copy; set fixed.FixedFromVarFileId = original.Id;
        original.SupersededByVarFileId = fixed.Id
    re-index fixed (new ContentSignature); prefer fixed as canonical
```
- **Never overwrites in place**; the original is retained (not transient trash) until the fix is proven good.
- **Auto/batch mode flags-and-queues low-confidence detections for review**, never deletes originals unattended (review Loss-C6).
- Fixed var validated against **VaM's** constraints, not just our own zip reader (review Loss-H7).
- Optional slimming (delete plugins/clothing/hair/assets/pose/morphs, `preloadMorphs=false`) is a **separate, off-by-default** step layered on the extract (from Boss963), never part of the pure encoding fix.
**Entities written:** new `VarFile`, `FixedFromVarFileId`/`SupersededByVarFileId` lineage, updated `Package.CanonicalVarFileId`.
**Edge cases:** re-zip lengthens names past Windows path limit → handle explicitly; `PartiallyBroken` with undetectable entries → fix what's known, flag the rest; multiple copies → fix each, dedup later.
**Acceptance:** a known-broken CJK var loads in VaM after fix; original recoverable; 0 originals destroyed in auto-mode; fixed↔original linked as lineage (not flagged as rival duplicates).

---

## IDX-10 — Integrity & broken detection  [should]

**Purpose:** flag unusable vars.
**Algorithm:** during Stage 1/2 set `IntegrityStatus`: `CorruptZip` (central directory unreadable), `MissingMeta` (no `meta.json`), `BadName` (fails identity parse). Corrupt/truncated payload detected via CRC mismatch on a spot-check or full verify.
**Entities written:** `VarFile.IntegrityStatus`.
**Edge cases:** a corrupt copy must **never** be elected canonical over a healthy one (canonical election ranks `Ok` first); never a dedup keeper.
**Acceptance:** corrupt/incomplete downloads are flagged, surfaced in a health report, and excluded from canonical/keep selection.

---

## IDX-11 — Quarantine-dir & prefs import  [should]

**Purpose:** ingest an existing library laid out by the old varManager without treating quarantined files as live.
**Algorithm:** on repo scan, recognize files under `___VarRedundant___`/`___StaleVars___`/`___OldVersionVars___`/`___DeletedVars___` → set `VarFile.QuarantineKind` (not live). Import `{vampath}\AddonPackagesFilePrefs\**\*.fav/*.hide` → `ContentItemPref`.
**Entities written:** `VarFile.QuarantineKind`, `ContentItemPref`.
**Edge cases:** a quarantined file that's actually the only copy of a needed package → surfaced, not silently ignored.
**Acceptance:** importing an old-varManager library preserves its soft-delete/stale state and per-item favorites/hides.

---

*This is the exemplar. If the format/depth is right, the remaining specs (07–12) follow the same template. Algorithms marked ⚙️ get full pseudocode; simpler features get structured prose.*
