# 07 — UI Coverage Map (dead-end audit)

Every feature from [02-Features](./02-Features.md) mapped to a **UI home** (screen · tab · modal · component). Goal: no feature without a surface, no button that goes nowhere, no data/business flow that dead-ends. This is the build checklist for the full HTML prototype ([mockups/prototype.html](./mockups/prototype.html)).

**Status key:** ✅ built · 🔨 to build in prototype · ▫ component within a built screen.

## App shell
Far-left **icon nav** switches top-level screens; each screen owns its sub-layout. Global: command palette (Ctrl-K search), theme toggle, activity log dock, status bar. Modals overlay any screen.

**Screens:** Library · Repositories · Loading Presets · Tiering & Migration · Duplicates & Reclaim · Health & Fix · Missing Dependencies · Analytics · Trash & Backup · Activity History · Settings. Plus first-run **Onboarding wizard**.

---

## Pillar 1 — Repositories & tiering

| Feature | UI home | Status |
|---|---|---|
| Register / enable / remove repository | **Repositories** screen · list + `Add repository` **modal** | 🔨 |
| Detect drive media type | Repositories · repo card badge (NVMe/SSD/HDD) | 🔨 |
| Benchmark read/write → tier | Add-repo modal · **benchmark step**; repo card shows MB/s | 🔨 |
| Manual tier override | Repositories · repo card tier selector | 🔨 |
| Online/offline handling | Repositories · offline state; Library rail shows greyed repo | ✅ rail / 🔨 screen |
| Live capacity + min-free reserve | Repositories · capacity bars + reserve field; Library rail bars | ✅ / 🔨 |
| Capacity alerts | Status bar + **Analytics** alert; toast | 🔨 |
| Add-a-drive rebalance | Add-repo modal · "rebalance onto this drive?" → Migration proposal | 🔨 |
| Drive health (SMART) | Repositories · repo card health chip | 🔨 |

## Pillar 2 — Classification, placement, migration

| Feature | UI home | Status |
|---|---|---|
| Usage tracking (signals) | implicit; surfaced in **Analytics** | 🔨 |
| Hot/warm/cold classification | **Tiering & Migration** · class distribution + per-var temp dot | 🔨 / ✅ dot |
| Dependency-centrality / foundational | Var detail "foundational" tag; Tiering view | ✅ tag |
| Manual pin hot / force cold | Var detail · pin control; Tiering bulk action | 🔨 |
| Lifecycle rules (time/access) | Tiering · **Rules tab** (editor) | 🔨 |
| Placement policy (class→tier) | Tiering · policy panel | 🔨 |
| Migration **planning + preview** | **Migration Proposal modal** (what moves where, size, ETA, dry-run) | 🔨 |
| Automation mode (propose/auto) | Settings · automation; Tiering banner | 🔨 |
| Safe move progress | Activity log + Migration modal progress | 🔨 |
| Rebalance on full tier | Tiering · proposal | 🔨 |
| Reports: wasting fast / slow where hurts | **Analytics** · report cards | 🔨 |
| "Where did my space go" + reclaim wizard | **Duplicates & Reclaim** screen | 🔨 |
| Analytics dashboard | **Analytics** screen | 🔨 |
| Policy simulation / dry-run | Tiering · "simulate" | 🔨 |

## Pillar 3 — Catalog, dependencies, discovery

| Feature | UI home | Status |
|---|---|---|
| Scan & index (staged) | Onboarding + Repositories `Rescan`; **log dock** progress | ✅ log / 🔨 |
| Identity parse / unrecognized bucket | Library · "Unrecognized" smart view | 🔨 |
| Incremental re-index | Repositories `Rescan`; automatic | ▫ |
| Content-type counts | **Library table** count columns | ✅ |
| Preview images / thumbnails | Library gallery + detail preview strip | ✅ |
| Rich metadata | Library **detail panel** + Var Detail modal | ✅ / 🔨 |
| Dependency graph (fwd/rev) | Detail deps list; **Var Detail modal** graph tab | ✅ / 🔨 |
| Version resolution / substitution | Detail deps (substituted state) | ✅ |
| Missing-dependency detection | **Missing Dependencies** screen | 🔨 |
| Logical duplicate detection | **Duplicates & Reclaim** · groups | 🔨 |
| Near-duplicate detection | Duplicates · near-dup tab | 🔨 |
| Intake classification | Duplicates · **Import intake** view | 🔨 |
| Single-copy / irreplaceable flag | ✅ badge everywhere; gates delete confirm | ✅ |
| Encoding health check | **Health & Fix** screen | 🔨 |
| Broken/corrupt detection | Health & Fix · integrity tab | 🔨 |
| Full-text search | ✅ top-bar search + command palette | ✅ |
| Fast sort/filter/facets | ✅ facet bar + sort | ✅ |
| Favorites / pins | ✅ badge + detail action | ✅ |
| Tags / labels | Library · **Tags** rail section + tag modal | 🔨 |
| Collections / groups | Library · Collections rail; Presets overlap | 🔨 |
| Recently used / added | ✅ smart views + sort | ✅ |
| Visual gallery (primary) | ✅ Library gallery | ✅ |

## Var Health & Auto-Fix (encoding)

| Feature | UI home | Status |
|---|---|---|
| Auto-detect broken-encoding | **Health & Fix** · report grouped by codepage | 🔨 |
| Auto-fix (batch) | Health & Fix · **Fix review modal** (confidence, before/after) | 🔨 |
| Safe output / retain original | Fix modal · options | 🔨 |
| Health report | Health & Fix · main table | 🔨 |
| Fix-on-import setting | Settings | 🔨 |
| Optional slimming | Fix modal · advanced (off by default) | 🔨 |

## Activation & loading presets

| Feature | UI home | Status |
|---|---|---|
| Rescue baseline | **Rescue modal** ("game won't launch") from Onboarding/Presets | 🔨 |
| Activate / deactivate var | ✅ detail action; Library ops bar | ✅ |
| Directory-swap profiles | ✅ rail profile + **Presets** screen switch | ✅ / 🔨 |
| Dependency-aware activation | Preset editor · dependency preview | 🔨 |
| Loading presets (CRUD, diff) | **Loading Presets** screen + **Preset Editor modal** | 🔨 |
| Auto-activate deps on load | Preset editor · "will pull in N" preview | 🔨 |
| Persistent missing-var aliasing | **Alias Resolver modal** (from Missing Deps + Preset) | 🔨 |
| Import/export presets (txt) | Preset editor · import/export | 🔨 |
| Temp activation / reconcile | automatic; Activity log; Settings toggle | ▫ |

## Operations (bulk & row)

| Feature | UI home | Status |
|---|---|---|
| Install / Uninstall / Delete selected | ✅ ops bar | ✅ |
| Move links to subfolder | ✅ ops bar → **Move modal** | ✅ / 🔨 |
| Export installed / Install from txt | ✅ ops bar | ✅ |
| Per-row Detail | ✅ → Var Detail modal | ✅ / 🔨 |
| Fix Var (rebuild/re-zip) | ✅ column → Fix modal | ✅ / 🔨 |
| Rebuild symlinks / Fix previews / Stale | ✅ Maintenance rail → confirm/progress | ✅ / 🔨 |
| Confirm destructive (→ trash, verify, single-copy warn) | **Confirm modal** | 🔨 |

## Cross-cutting / safety / ops

| Feature | UI home | Status |
|---|---|---|
| First-run onboarding | **Onboarding wizard** (stepper) | 🔨 |
| Background jobs + progress + cancel | ✅ log dock + job tray | ✅ / 🔨 |
| Never hard-delete / trash / restore | **Trash & Backup** screen | 🔨 |
| Verified/crash-safe moves | Migration modal states; Activity log | 🔨 |
| Dry-run everything | Migration + Fix + Reclaim previews | 🔨 |
| Portable catalog / DB backup-restore | **Trash & Backup** · backup tab; Settings | 🔨 |
| Operation history / audit | **Activity History** screen | 🔨 |
| Remembered views | implicit (persist); Settings | ▫ |
| Bulk operations | ✅ ops bar + select-all | ✅ |
| Import from old varManager | Onboarding + Settings · import | 🔨 |
| Settings (VaM path, policies, thresholds) | **Settings** screen (tabs) | 🔨 |

## Out of scope (no UI — deliberately)
Hub browsing · load-scene-into-VaM · scene/preset extraction · MMD loader · var packaging. *(Pending user's ruling on Hub + load-into-VaM.)*

---

## Dead-ends this audit surfaced (must resolve in the prototype)
1. **Migration/placement had no review surface** → add the **Migration Proposal modal** (propose-not-auto has to be *seen*).
2. **Aliasing had no persistent editor** → **Alias Resolver modal** with save-scope (global/preset) — the feature's whole point.
3. **Analytics/reports (Pillar 2) had nowhere to live** → **Analytics screen**.
4. **Trash/backup/restore** (core safety) had no UI → **Trash & Backup screen**.
5. **Onboarding** (the first thing a user hits) didn't exist → **Onboarding wizard**.
6. **Rescue baseline** (the #1 crisis) had no trigger → **Rescue modal**.
7. **Tiering rules/policy** (Pillar 2's controls) had no editor → **Tiering screen** with Rules/Policy tabs.
8. **Confirm-destructive** flow (verify, single-copy gate) was implicit → explicit **Confirm modal**.

---

*Next: build [mockups/prototype.html](./mockups/prototype.html) — the app shell with all screens navigable + the modals/wizards above, so every row here has a clickable home.*
