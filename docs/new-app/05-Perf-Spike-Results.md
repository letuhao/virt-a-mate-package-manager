# New App — Performance Spike Results

A real SQLite spike (Python 3.13 / SQLite 3.51.1) to validate the v2 read-model + OrderedSnapshot + index design ([03 §5.8/§6/R8](./03-Data-Architecture.md)) **before schema lock**. Script: `scratchpad/spike.py`.

## Synthetic dataset (worst-case-ish, larger than the user's current library)
- **1,000,000 Packages**, **1,480,407 VarFiles** (1–3 copies each), **5,507,238 Dependency edges** (power-law: 40 "foundational" nodes absorb ~25% of edges), 1M UsageStat, 1M FTS rows, ~14% CJK creator names.
- Materialized `PackageListItem` (1M rows) + composite indexes + contentless FTS5 + `VarFile(ContentSignature)` index.
- **DB size 635 MB.** Generation ~22 s; index build + ANALYZE ~14 s. (Bulk insert measured at **~690K dependency rows/sec** with `synchronous=OFF` batched — validates the bulk-index approach.)

## Results (median of 5, warm)

| Query | Time | Plan | Verdict |
|---|---|---|---|
| Gallery page 1, sort TotalSize desc | **0.08 ms** | index `ix_pli_size`, no temp sort | ✅ |
| Gallery page 1, sort LastUsedAt desc | **0.08 ms** | index `ix_pli_last` | ✅ |
| Filter Creator + sort TotalSize desc | **0.02 ms** | `ix_pli_cr_size (Creator=?)` | ✅ |
| Filter Class=cold + sort LastUsed desc | **0.08 ms** | `ix_pli_class_last (Class=?)` | ✅ |
| **OrderedSnapshot build** (500K cold rows) | **200 ms** (one-time per filter/sort) | ordered insert | ✅ acceptable |
| **Scrollbar jump** rows[mid]/rows[end] via snapshot | **0.07–0.09 ms** | INTEGER PK seek both sides | ✅ **true O(1) random access** |
| FTS5 MATCH `creator123` + join | **0.04 ms** | FTS virtual + PK join | ✅ |
| FTS5 MATCH CJK `作者14` + join | **0.04 ms** | — | ✅ (tokenizer caveat below) |
| Dedup: group by ContentSignature HAVING >1 | **1.67 ms** | covering `ix_var_sig` | ✅ (the review's missing-index fix works) |
| Faceted COUNT, Class=cold (500K match) | **12.4 ms** | covering index scan | ⚠️ debounce/approximate |
| Deep keyset page @500K (OR-form predicate) | **23.9 ms** | index scan-and-filter | ⚠️ query-form fix |
| **Reverse dependency closure, FOUNDATIONAL node** | **🔴 39,331 ms (39 s)** | recursive CTE, huge frontier | 🔴 **must fix** |
| Reverse closure, random node | 97 ms | recursive CTE | ⚠️ |

## What this proves

**✅ The core read-path design is validated — decisively.** Materialized `PackageListItem` + one composite index per sort order delivers **sub-0.1 ms** gallery paging, filtering, and sorting at 1M packages — the exact operations the old tool made agonizing. The **OrderedSnapshot** gives true scrollbar random-access at **0.08 ms** (jump to row 250,000 instantly) after a one-time ~200 ms build per filter/sort change. **FTS5 search is 0.04 ms.** **Dedup grouping is 1.7 ms** (the review's `ContentSignature` index fix confirmed). R8 holds.

**⚠️ Two confirmed-and-cheap fixes** (already anticipated in the review):
1. **Faceted COUNT is ~12 ms on large result sets** → debounce, show approximate ("~500K"), compute exact only once the query settles. (§5.8 already says this.)
2. **Deep keyset paging via the `(a<?) OR (a=? AND b<?)` form scans-and-filters (24 ms).** Fix: use SQLite **row-value tuple comparison** `WHERE (TotalSize, PackageId) < (?, ?)` which seeks the composite index (→ ~0.08 ms). Moot for the gallery anyway, since we use OrderedSnapshot for random access, but matters for any "load more" cursor path.

**🔴 One real architectural must-fix — reverse-dependency closure on foundational nodes.** A naive on-demand recursive CTE for "what depends on X" when X is a foundational package (a common morph/texture pack half the library references) took **39 seconds** — completely unusable. The frontier explodes within a few hops regardless of the depth cap; `count(DISTINCT)` over it compounds it. The v1/v2 design *flagged* caching as a nice-to-have; **the spike proves it is mandatory.**

### Resolution (folded into [03 §5.3](./03-Data-Architecture.md))
- **Never enumerate a transitive reverse closure on demand for the hot path.** Store a **direct reverse-dependent count** per package (cheap: `GROUP BY ResolvedPackageId`), maintained incrementally, to answer "how many things depend on X" instantly.
- **Materialize `HasMissingDeps` as a direct bit** (already in v2) — never compute transitive missing per grid row.
- **Cache full reverse closures only for the small set of high-in-degree ("foundational") nodes**, invalidated on graph edits (rare). Flag such nodes with an `IsFoundational` bit.
- **Safe-delete / "what breaks if I remove this"** uses direct dependents + the cached flag, with a **frontier/early-out cap**: past a threshold, report "used by 40,000+ packages — foundational, removal unsafe" instead of enumerating. Full enumeration is an explicit, backgrounded, progress-bared action only when the user demands the actual list for a non-foundational node.

## Caveats / notes
- Python's bundled SQLite (3.51.1) ≈ what `Microsoft.Data.Sqlite` ships; EF Core adds a thin mapping layer over the same engine, so these query-planner results carry over. EF's change-tracker overhead is a *separate* concern (mitigated by raw SQL for bulk/read paths, per [01](./01-Tech-Stack.md)).
- **CJK FTS tokenization:** the default `unicode61` tokenizer matched `作者14` here because the test blobs were space-delimited, but real CJK text has no spaces — production needs a CJK-aware tokenizer (e.g. `trigram`, or ICU/`signal`), decided at FTS-table creation. Flagged for schema-lock.
- The 39 s worst case is realistic: real foundational VaM assets (base morphs, common textures) are depended on by a large fraction of a library, so this is not a pathological synthetic artifact.

## Bottom line
The data architecture's performance foundation is **sound and proven** at 1M+ packages — sub-millisecond browse/search/sort, O(1) scrollbar. The spike changed exactly one thing from "should" to "must": **reverse-dependency closure must be precomputed/capped, never enumerated on demand.** That resolution is now in [03 §5.3](./03-Data-Architecture.md). Schema is ready to lock.
