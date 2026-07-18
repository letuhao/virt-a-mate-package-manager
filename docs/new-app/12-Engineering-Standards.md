# 12 — Engineering Standards

The rules the code follows. Backed by the SDK primitives in `VarVault.Common`/`VarVault.Sdk` (already built). UI-specific rules are in [13-UI-UX-Standards](./13-UI-UX-Standards.md). Summary lives in [/CLAUDE.md](../../CLAUDE.md).

## 1. Layering & modularity
- Dependency direction: `Common ← Sdk ← Domain ← Modules/Infrastructure ← Host ← Cli/App`. Enforced by `VarVault.Architecture.Tests` (NetArchTest).
- **A module never references another module.** Cross-module work = SDK service interface or `IEventBus`.
- Modules keep types `internal` except the SDK interfaces they implement. Register interfaces in DI, never concretes.
- Infrastructure implements SDK/Domain interfaces; no module binds to EF/Win32/Infrastructure directly.

## 2. Error handling
- **Expected, recoverable failures → `Result` / `Result<T>`** (from `VarVault.Common`). Never throw for control flow.
  ```csharp
  public Result<PackageId> Parse(string name) =>
      valid ? PackageId... : Result.Failure<PackageId>("packageid.format", "…");
  ```
- **Exceptions** are for programmer error and truly exceptional conditions (I/O faults you can't handle locally). Validate arguments with `Guard`.
- Error codes are stable dotted slugs (`"packageid.version"`), messages are user-actionable.
- Never swallow exceptions silently; log with context or wrap into a `Result`.

## 3. Async & cancellation
- **Async all the way.** Any method doing I/O or non-trivial work is `async Task`/`Task<T>` and takes a `CancellationToken` as the **last** parameter (`cancellationToken`, default `default`).
- **Library code uses `ConfigureAwait(false)`** on every await. (App/UI code doesn't need it.)
- Never block on async (`.Result`, `.Wait()`, `.GetAwaiter().GetResult()`) — deadlock/threadpool-starvation risk.
- Prefer `Task`; use `ValueTask` only on measured hot paths (e.g. `AsyncLock.AcquireAsync`).
- Honor cancellation promptly; treat `OperationCanceledException` as a normal outcome (don't log as error).

## 4. Threading model
- **UI thread:** services never touch UI objects; marshal via **`IUiDispatcher`** (`Post`/`InvokeAsync`).
- **Background work:** long/cancellable operations go through **`IJobQueue`** (returns a `JobHandle` with progress + `Cancel()`); report progress via the `IProgressSink` in `JobContext`.
- **Catalog writes:** **all** mutations go through **`IWriteQueue`** — one serialized consumer, so SQLite's single-writer model holds and interactive writes (`WritePriority.Interactive`) beat bulk. Reads never use the write queue.
- Async mutual exclusion: `AsyncLock` (not `lock` when the section awaits). Per-physical-drive parallelism is set by the scheduler (HDD=1, NVMe=high).
- Backpressure: bounded `System.Threading.Channels` where producers can outrun consumers.

## 5. Events / message bus
- Cross-module notifications are `IDomainEvent` records, **past-tense** (`VarIndexed`, `MigrationProposed`), immutable.
- React by registering `IEventHandler<TEvent>` in a module's `Register`; the `EventBus` dispatches to all handlers on `PublishAsync`. UI/local reactions use `bus.Subscribe(Action)`.
- Handlers are self-contained and idempotent where possible; they must not throw for expected conditions.
- The bus is **in-process** (not durable) — never use it for anything that must survive a crash (that's the DB + job state).

## 6. Logging
- Inject `ILogger<T>` (Microsoft.Extensions.Logging; Serilog backend via `LoggingSetup`). Console + daily rolling file under the data dir.
- **Structured logging** — message templates with named holes, never string concatenation:
  ```csharp
  _log.LogInformation("Indexed {Count} vars from {Repo} in {Ms}ms", count, repo, ms);
  ```
- Levels: `Trace/Debug` dev detail · `Information` lifecycle/milestones · `Warning` recoverable/anomalies · `Error` failures needing attention. No PII beyond var names/paths.
- No `Console.WriteLine` outside the CLI front-end.

## 7. Dependency injection
- One container, built by `VarVaultHost.Build` with `ValidateOnBuild` + `ValidateScopes`.
- Lifetimes: **singleton** for stateless services + cross-cutting infra (queues, clock, bus); **scoped** for `DbContext`/`IUnitOfWork` (a scope per unit of work); **transient** for cheap stateless helpers.
- Never resolve scoped services from the root provider; open a scope. Don't use the service locator pattern outside composition.

## 8. Persistence
- **EF Core + SQLite**, one file, pinned to stable local storage. Reads via query services/`PackageListItem`; **writes only inside an `IWriteQueue` action** committing through `IUnitOfWork`.
- Baseline pragmas (`SqlitePragmas.Baseline`): `WAL`, `synchronous=NORMAL`, `foreign_keys=ON`, `busy_timeout`. **`synchronous=FULL`** is applied per destructive-filesystem transaction (migration/delete), not globally.
- Entities configured via `IEntityTypeConfiguration<T>` in Infrastructure (auto-applied). Migrations own schema evolution; prefer additive nullable columns; big migrations run in the background.
- No EF entities cross module boundaries — expose **DTOs**/read-model rows through SDK interfaces.

## 9. Identity & text (heavily-CJK library)
- Match packages/dependencies by the **fold key** (NFC + full Unicode case-fold), never raw strings. `PackageId` preserves the verbatim version token (`.007` ≠ `.7`); identity comes from the **filename**, never `meta.json`.
- ZIP entry names for signatures/encoding use **raw bytes** (decoding mojibake is lossy). Register `CodePagesEncodingProvider` before reading legacy codepages.

## 10. Naming & style
- File-scoped namespaces; `nullable enable`; `ImplicitUsings`; primary constructors where natural; `sealed` by default.
- `IThing` interfaces, `ThingService` impls, `ThingModule` modules, `IThingHandler`/`ThingEvent` (past-tense) for events. Async methods end `Async`.
- `.editorconfig` is authoritative for formatting; keep analyzers green (fix or justify suppressions).

## 11. Testing
- **xUnit** + `Assert` (no FluentAssertions — v8 licensing). Test names `Method_scenario_expectation` with underscores (CA1707 disabled under `tests/`).
- Test **behavior via SDK interfaces**, not internals. Keep unit tests fast and deterministic (avoid real timing where possible; the few timing tests poll with generous bounds).
- Add an architecture-boundary assertion when introducing a project. Integration tests use real SQLite (temp files), cleaned up.
- **Evidence gate:** an item in [09-Implementation-Checklist](./09-Implementation-Checklist.md) is `[x]` only with a named test / benchmark / run output.

---

*Sources informing these standards:* [Channels — .NET (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels) · [Avalonia MVVM patterns](https://docs.avaloniaui.net/docs/how-to/mvvm-how-to).
