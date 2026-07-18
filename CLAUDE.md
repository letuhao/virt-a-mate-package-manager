# CLAUDE.md — VarVault

Project instructions for AI coding sessions. Read this first, then the design docs in [`docs/new-app/`](docs/new-app/README.md).

## What this is
**VarVault** — a .NET 10 desktop app that manages Virt-a-Mate `.var` packages as **tiered, multi-drive object storage** (hot/warm/cold placement by usage) with first-class dependency management, dedup, and CJK-encoding auto-fix. Design is complete and sealed; the code is being built in thin vertical slices.

## Architecture — modular monolith + SDK
A single app composed of independent **modules** that talk only through the **SDK** (contracts + events). See [docs/new-app/11-Architecture-and-Modularity.md](docs/new-app/11-Architecture-and-Modularity.md).

```
Common ← Sdk ← Domain ← Modules.* / Infrastructure ← Host ← Cli/App
```
**Dependency rule (enforced by `VarVault.Architecture.Tests` — do not break):**
- `Common` depends on nothing. `Sdk` on Common + DI.Abstractions only. `Domain` on Common only.
- A **module never references another module** — cross-module work goes through SDK interfaces + `IEventBus`.
- Modules never bind to `Infrastructure` or EF/Win32 directly — they consume SDK interfaces resolved via DI.

### Project map
| Project | Role |
|---|---|
| `VarVault.Common` | kernel: `Result<T>`, `Error`, `Guard`, `IClock`, `AsyncLock`, `ProgressReport`/`IProgressSink` |
| `VarVault.Sdk` | contracts: `IModule`/`IModuleContext`/`IPlugin`, `IEventBus`/`IEventHandler<T>`, `IJobQueue`, `IWriteQueue`, `IUiDispatcher`, `IUnitOfWork` |
| `VarVault.Domain` | value objects/entities: `PackageId`, `VersionRef` |
| `VarVault.Infrastructure` | impls: job/write queues, dispatcher, `VarVaultDbContext` (SQLite+WAL), persistence registration |
| `VarVault.Modules.*` | feature modules (`IModule`): Repositories, Indexing, … |
| `VarVault.Host` | composition root: `VarVaultHost.Build`, `Bootstrap`, `EventBus`, `LoggingSetup` |
| `VarVault.Cli` | headless entry / smoke |

## Build, test, run
Solution is **`VarVault.slnx`** (new XML format) — use bare commands, not `VarVault.sln`:
```
dotnet build            # 0 errors expected
dotnet test             # all green expected
dotnet run --project src/VarVault.Cli
```
- **.NET 10** (`global.json` pins 10.0.302). **Central package management** — add versions in `Directory.Packages.props`, reference without `Version=` in csproj.

## Conventions (must follow)
- **Errors:** expected failures return `Result`/`Result<T>` (never exceptions). Exceptions are for programmer error / truly exceptional cases. Validate args with `Guard`.
- **Async:** async all the way; every I/O/long method takes a `CancellationToken` (last param). In **library code use `ConfigureAwait(false)`**. Don't block on async (`.Result`/`.Wait()`). Prefer `Task`; `ValueTask` only for hot allocation-sensitive paths.
- **Threading:** UI thread work → `IUiDispatcher`. Background work → `IJobQueue` (cancellable + progress). **All catalog writes → `IWriteQueue`** (single-writer; SQLite is single-writer). `AsyncLock` for async-safe mutual exclusion.
- **Events:** cross-module reactions via `IEventBus` + DI-registered `IEventHandler<T>`; UI/local via `bus.Subscribe`. Events are `IDomainEvent` records, past-tense (`VarIndexed`).
- **Logging:** inject `ILogger<T>` (Microsoft.Extensions.Logging; Serilog backend). Structured messages (`"Indexed {Count} vars"`, not string concat). No `Console.WriteLine` outside the CLI.
- **DI:** a module's `Register` adds its services; keep module types `internal` except the SDK interfaces they implement. Register interfaces, not concretes.
- **Persistence:** EF Core + SQLite. Reads via query services; writes only inside an `IWriteQueue` action via `IUnitOfWork`. Baseline pragmas WAL + `synchronous=NORMAL` + `foreign_keys=ON`; `synchronous=FULL` per destructive filesystem transaction.
- **Identity/CJK:** the library is heavily CJK — match by the **fold key** (NFC + case-fold), never raw strings. `PackageId` keeps the verbatim version token (`.007` ≠ `.7`).
- **Style:** file-scoped namespaces, `nullable enable`, primary constructors where natural. Naming: `IThing`, `ThingService`, `ThingModule`, events past-tense.

## Testing
- **xUnit** (no FluentAssertions — v8 licensing; use `Assert`). Test method names use `Underscore_case`.
- Test behavior through **SDK interfaces**, not internals. Add/keep an architecture test when adding a project.
- Every checklist item ([09](docs/new-app/09-Implementation-Checklist.md)) is checked `[x]` **only** with concrete evidence (test name / benchmark / run output).

## Docs
- Design: [docs/new-app/](docs/new-app/README.md) (00–11). **Decisions are sealed in [10-Decisions-Log](docs/new-app/10-Decisions-Log.md)** — check there before re-opening a question.
- Engineering standards: [12-Engineering-Standards](docs/new-app/12-Engineering-Standards.md) · UI/UX: [13-UI-UX-Standards](docs/new-app/13-UI-UX-Standards.md).
- Legacy reference (what to copy featurewise): [docs/varManager/](docs/varManager/README.md).

## Load-bearing (⚠ don't change casually)
VAR naming `Creator.Package.Version`; the `___XXX___` dirs; install = symlink; `ContentSignature` = raw-bytes zip signature; the deletion predicate (online + hash-verified + same identity); migration durability (copy→verify→rename→delete). Out of scope for v1 (sealed): Hub, load-into-VaM, MMD, packaging.

## Git
Work on a branch off `main`; end commit messages with the Co-Authored-By trailer. Don't commit `bin/`/`obj/` (gitignored).
