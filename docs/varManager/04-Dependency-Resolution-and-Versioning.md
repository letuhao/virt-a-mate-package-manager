# 04 — Dependency Resolution & Versioning

This is the algorithmic heart of varManager and the part most worth getting right in a rebuild. It answers: *"to use package X, what else must be installed?"* (forward closure) and *"if I remove X, what breaks?"* (reverse closure), plus *"which local version satisfies a dependency reference?"* (version matching).

## Dependency extraction — `Getdependencies(string jsonstring)` (`Form1.cs:276`)

Dependencies are found by **regex over raw text**, not JSON parsing (the SimpleJSON path is commented out). This is deliberate: scene/`.vap`/`meta.json` files can be huge or malformed, and references appear as JSON *keys* anywhere in the document.

```
Pattern (IgnoreCase, Singleline):
"  (  ([^\r\n":.]{1,60})  \.  ([^\r\n":.]{1,80})  \.  (\d+|latest)  )  "?  \s*  :
```

Decoded: a `"` then group1 = `Creator.Package.Version` where
- Creator = 1–60 chars excluding `"`, `:`, `.`, CR, LF
- Package = 1–80 chars, same exclusions
- Version = digits **or** the literal `latest`

followed by optional `"`, whitespace, then `:` — i.e. it matches JSON keys of the form `"Creator.Package.N":`.

Post-processing:
- If a matched group contains `/`, keep only the substring **after** the first `/` (strips a `licenseType/`-style prefix on embedded references).
- `Distinct()` the results.

Properties (⚠ and ✎):
- ⚠ Matches dependency-shaped keys **anywhere** in the text, not only inside a `"dependencies"` object. This is intended and reused for scenes and `.vap` files (see [06](./06-Preview-Scene-Analysis-and-Loading.md)).
- Creator/package names containing `.` or `:` won't match; names longer than 60/80 chars are silently dropped.
- `latest` is captured as a valid version token.
- ✎ Rebuild: preserve the *tolerance* (scan any JSON, don't choke on malformed input) but implement it as a robust JSON reader that collects dependency-key-shaped strings, with the same `Creator.Package.(N|latest)` grammar.

## Version resolution

### `Varislatest(varname)` (`Form1.cs:311`)
Is this the newest local version of its package? `version >= vars.Where(creator & package).Max(version)`. Non-conforming names → `true`. (⚠ `.Max()` throws on an empty group, but callers only pass names known to exist.)

### `VarCountVersion(varname)` (`:327`)
Count of local rows with the same creator+package. `0` for non-conforming names.

### `VarExistName(varname)` (`:795`) — resolve a dependency reference to a local var
The core resolver. Returns one of three shapes:
- a real local `varName` — exact or `latest` match found;
- `"missing"` — nothing suitable locally;
- `resolvedName + "$"` — a **version substitution**: the exact requested version was absent, a different one was chosen. The trailing `$` is a sentinel flag.

Algorithm:
1. Split to 3 parts; on failure return `"missing"`.
2. If version == `latest` (case-insensitive): pick the highest local version of that package; none → `"missing"`.
3. Else exact `FindByvarName(varname)`: found → return it; not found → `GetClosestMatchingPackageVersion(...)`; if not `"missing"`, return `closest + "$"`.

### `GetClosestMatchingPackageVersion(creator, package, requestVersion)` (`:822`)
Order candidate versions ascending; return the **first** whose `version >= requestVersion` (smallest version ≥ requested). If none is ≥, return the **highest available** (newest older one). Package absent → `"missing"`. Rule of thumb: *prefer the requested version or the next newer; otherwise take the newest older*.

✎ **Rebuild note:** `$` (version-substituted) and `^` (processed — below) are load-bearing **string sentinels** smuggled inside `varName`s. Model them as explicit fields (`ResolvedVar { PackageId Id; bool VersionSubstituted; }`) instead.

## Forward closure — `VarsDependencies` (what must be installed)

`VarsDependencies(string)` (`:1842`) returns the direct `dependencies.dependency` rows for one var.

`VarsDependencies(List<string>)` (`:1851`) computes the **transitive closure** of everything that must be installed, using a `^`-suffix "processed" marker as a work-set convention:
1. Partition input: names ending in `^` → already processed (strip `^`, add to `varsProcessed`); others → resolve each via `VarExistName` (strip any `$`); keep non-`"missing"` resolved names.
2. `varnameexist = Distinct().Except(varsProcessed)`.
3. Collect the direct dependencies of each newly-seen name.
4. `varsProcessed += varnameexist`; subtract processed from the new deps.
5. If new deps remain → re-mark all processed names with `^` and recurse.
6. Else → strip `^`, `Distinct()`, return the full resolved set.

This is the set installed by the Install button, `InstallTemp` (scene loading), and the missing-dependency / save-dependency fixers.

## Reverse closure — `ImplicatedVar` / `ImplicatedVars` (what breaks if removed)

`ImplicatedVar(varname)` (`:339`) returns vars that **depend on** `varname`, but **only when it is safe to touch** — guarded by `VarCountVersion(varname) <= 1` (act only if this is the sole local version of its package; if multiple versions exist, return empty so removal doesn't cascade).
- If `Varislatest(varname)`: match dependency rows where `dependency == varname` **or** `dependency == {creator.package}.latest` (so `latest` references count against the newest).
- Else: match only exact `dependency == varname`.

`ImplicatedVars(List<string>)` (`:412`) is the recursive reverse closure (same `^`-marker scheme) — the target vars **plus** everything that transitively depends on them. Used by Uninstall and Delete to compute the cascade shown for confirmation.

Related lookups:
- `DependentVars(string)` (`:363`) — **unguarded** direct dependents (no `VarCountVersion<=1` gate). Used by the Var Detail dialog.
- `DependentSaved(string)` (`:385`) — dependents from the **`savedepens`** table (loose scene/save files that reference this var), via `savedepensTableAdapter.FillByDepens`. Honors the `.latest` alias.
- `GetDependents(string)` (`:2601`) — union of `dependencies.varName` and `savedepens.savepath` where `dependency == name` (simple direct reverse lookup used by other forms).

## Missing-dependency detection

All variants share a pattern: gather a candidate dependency set → resolve each via `VarExistName` → bucket into installed / missing / version-substituted → optionally auto-install → show `FormMissingVars` for the truly missing.

| Method | Candidate set | Auto-installs? |
|---|---|---|
| `MissingDepends()` (`:1381`) | dependencies of **installed** vars | Yes — installs resolvable ones; `$` results reported as "missing version" |
| `FilteredMissingDepends()` (`:1419`) | dependencies of vars **visible in the grid** | No (install branch commented out) — report only |
| `AllMissingDepends()` → `MissingDependencies()` (`:1476`/`:1489`) | **all** dependency rows | No — report only |

`MissingDependencies()` returns the missing list (with `$` markers for version substitutions); FormHub also calls it to build hub download lists (see [08](./08-Hub-Integration-API.md)).

`LogAnalysis()` (`:2776`): parses VaM's `output_log.txt` (LocalLow `MeshedVR\VaM`) with regex `Missing addon package {dep} that package {pkg}`, auto-installs any resolvable deps, reports the rest. Requires VaM closed.

`FixSavseDependencies()` (`:1626`) indexes the *user's own* saves' dependencies into `savedepens` and installs what's missing — see [06](./06-Preview-Scene-Analysis-and-Loading.md).

## Worked example

Installing scene var `A.Scene.1` whose `meta.json` lists `B.Cloth.2` and `C.Hair.latest`:
1. `VarsDependencies(["A.Scene.1"])` resolves each:
   - `A.Scene.1` → exact local hit.
   - `B.Cloth.2` → exact miss; `GetClosestMatchingPackageVersion(B,Cloth,2)` finds local `B.Cloth.3` ≥ 2 → returns `B.Cloth.3$` (substituted).
   - `C.Hair.latest` → highest local `C.Hair.7`.
2. Recurse into each resolved var's own dependencies until the work-set is empty.
3. The Install step symlinks `A.Scene.1`, `B.Cloth.3`, `C.Hair.7` (and any transitive deps) into `___VarsLink___`.
4. If `B.Cloth` had **no** local version ≥ 2 and none at all → `"missing"`, surfaced in FormMissingVars, which can alias it (create a fake link) or generate a hub download link.

Continue to [05 — Installation, Symlinks & Profiles](./05-Installation-Symlinks-and-Profiles.md).
