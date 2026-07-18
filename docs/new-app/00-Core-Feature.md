# New App — Core Feature (working draft)

> This is a **new application**, not a rebuild or migration of the old varManager.
> We **copy strong features** of the old tool (esp. var/dependency management) but **not its architecture** — the old one is a performance hell (Access DB, whole-DataSet-in-memory, 3,700-line God object, synchronous I/O). See `docs/varManager/` for what those features *do*, implemented the wrong way.

## The problem (in the user's words)

- 4 TB+ of `.var` files, growing. VaM **can't load** with all of them present.
- Storage is **heterogeneous & multi-drive**: several repos across several disks — fast SSD (e.g. 7000 MB/s read) + slower SSDs + HDDs — each with different speed and capacity.
- Vars have **different usage frequency**: some hot-path (used constantly), some cold-path (near-archive). Putting cold data on the fast SSD is a waste; hot data on a slow HDD is painful.
- Managing **var identity, type, and dependencies** at this scale is essential — and the old tool did it well but far too slowly.

## Core function

**A tiered, multi-repository object-storage system for VaM vars, with first-class var/dependency management.** Treat each `.var` as an object with rich metadata; rank storage repositories by real performance; classify each var hot/warm/cold from usage; place it on the tier that fits; and let users manage the whole library through its identity/type/dependency structure. S3-style *principles* (objects + metadata + access stats + performance-tiered storage + lifecycle policy), purpose-built for VaM.

---

## Three core pillars

### Pillar 1 — Repository & tier management (storage)
A **repository** is a var-storage location on a drive.
- **Register** multiple repositories across multiple drives.
- **Profile performance & capacity** — read/write speed + free/total space per repo.
- **Rank into tiers + priority** — fastest SSD = Tier 1 (hot path) … HDD = cold tier. Multiple repos can share a tier; priority orders them.
- **Capacity/overflow** — track free space; handle "tier full → spill or evict".

### Pillar 2 — Classification & placement (the analyzer)
Each var is an object with usage metadata.
- **Classify** hot/warm/cold from usage statistics.
- **Place** each object in a repo whose tier matches its class, respecting capacity + priority.
- **Migrate** objects between tiers as classification changes over time.
- **Signals** (to confirm): app-observed activation/load events (strongest), recency & frequency, **dependency centrality** (from Pillar 3), VaM logs, manual pins.

### Pillar 3 — Var metadata & dependency management (the catalog)
The identity/intelligence layer — the old varManager's strongest feature, done right.
- **Identity**: parse `Creator.Package.Version`; track versions of a package. (old spec ch. 02)
- **Type**: classify content — scene / look / clothing / hairstyle / morphs / pose / skin / plugins / assets — with previews and counts. (ch. 03)
- **Dependencies**: parse each var's `meta.json` dependency graph; forward closure (to use X, install these), reverse closure (removing X breaks these), version matching ("requested-or-next-newer, else newest-older"), `latest` resolution, and **missing-dependency detection**. (ch. 04)
- **Health & auto-fix**: detect vars with non-UTF-8 ZIP entry names (Chinese/Japanese/Korean codepages) that **make VaM fail to load them**, and auto-fix them (decode → re-zip as UTF-8) in batch. Essential; reference logic = Boss963 tool + varManager's `ZipHandler` detector. (see [02-Features](./02-Features.md))
- Implemented performantly: indexed DB (not in-memory DataSet), async, precomputed/queryable graph, parallel indexing.

### How the pillars connect
- Pillar 3's **dependency graph feeds Pillar 2** — a var many things depend on is structurally hot (centrality), and activating a scene pulls its dependency closure along.
- Pillar 2's placement decisions land objects in **Pillar 1's** ranked repos.
- **Activation** (does the game see it now — via symlink/staging into `AddonPackages`) is an orthogonal axis that interacts with all three.

| Axis | Question | Driven by |
|---|---|---|
| Repository / tier | Which ranked storage does it live on? | Pillar 1 — repo performance |
| Classification | Hot, warm, or cold? | Pillar 2 — analyzer |
| Placement | Which specific repo (tier + capacity + priority)? | Pillar 2 — policy |
| Metadata / dependencies | What is it, and what does it need / feed? | Pillar 3 — catalog |
| Activation | Does the game see it right now? | User / active set |

---

## Open questions (to settle before listing sub-features)

**Pillar 1**
1. Repo tier — **auto-benchmark** on registration, **declare** manually, or both (measure + override)?
2. Tier model — fixed ladder (1/2/3…) or free-form? How many tiers in reality (hot/cold vs hot/warm/cold/archive)?
3. Overflow — Tier 1 full → **spill** hot data to Tier 2, or **evict** the coldest Tier-1 object down to make room?

**Pillar 2**
4. Usage signals — learn purely from in-app actions, or also mine external evidence (VaM logs, dependency graph) to bootstrap?
5. Automation — auto-migrate on schedule/threshold, or propose + approve? (Moving TBs has real cost.)

**Pillar 3 / cross-cutting**
6. Activation — symlink into `AddonPackages` (file stays on its tier) or stage onto the fast drive first? Does "activate" imply "promote to hot"?
7. Duplicates — same var on multiple repos: detect + dedupe to reclaim space, or leave alone?
8. Scale — how many drives/repos, and roughly how many `.var` files?

---

*Next: confirm this three-pillar core is complete, then list the sub-features that serve each pillar.*
