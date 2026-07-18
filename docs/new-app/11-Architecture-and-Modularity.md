# 11 — Software Architecture & Modularity

How the code is organized so it's **common-first, SDK-driven, and modular for maximum reuse**. This is the foundation the build sits on (Phase 0 of the [checklist](./09-Implementation-Checklist.md)).

## Style: Modular monolith + SDK + plugin host

A single deployable desktop app internally composed of **independent feature modules** that communicate only through a **stable SDK** of contracts and events. One process, no microservices — but with the seams of a plugin system, so:
- the same core drives the **GUI, a CLI, and tests** (maximum usability);
- the deferred features (Hub, load-into-VaM) drop in later as **plugins** against the SDK, not core edits;
- each module is **independently testable and replaceable**.

## Projects & dependency direction

```
                 VarVault.Common   (kernel: Result<T>, Error, Guard, IClock — zero deps)
                       ▲
                 VarVault.Sdk      (PUBLIC contracts: IModule, service interfaces,
                       ▲            DTOs, events, IServiceRegistrar) → Common + DI.Abstractions
        ┌──────────────┼───────────────┐
   VarVault.Domain     │        (external plugins reference ONLY Sdk + Common)
   (entities, value    │
    objects, rules)    │
        ▲              │
        │        VarVault.Modules.*        VarVault.Infrastructure
        │        (Repositories, Indexing,  (EF Core/SQLite, filesystem,
        │         Dependencies, Activation, symlink, zip, jobs — implements
        │         Dedup, Encoding, Tiering) Sdk/Domain interfaces)
        │              ▲                         ▲
        └──────────────┴────────────┬────────────┘
                          VarVault.Host      (composition root: discover + register
                                ▲             modules & plugins, build DI container)
                    ┌───────────┴───────────┐
              VarVault.App (Avalonia)   VarVault.Cli (console)
```

**Rules (enforced by an architecture test):**
- `Common` depends on nothing. `Sdk` depends only on `Common` + `Microsoft.Extensions.DependencyInjection.Abstractions`.
- `Domain` depends only on `Common`.
- A **module** depends on `Sdk` + `Domain` + `Common` — **never on another module** (cross-module calls go through Sdk interfaces + events).
- `Infrastructure` implements `Sdk`/`Domain` interfaces; no module depends on `Infrastructure` directly (they get implementations via DI from the Host).
- `Host` wires everything; `App`/`Cli` depend on `Host` + `Sdk` only.

## The SDK (why it exists)

`VarVault.Sdk` is the **one stable boundary**. It contains:
- **`IModule`** — a feature module's registration hook: `void Register(IServiceCollection services, IModuleContext ctx)` (using `IServiceCollection` from `Microsoft.Extensions.DependencyInjection.Abstractions` — already the standard abstraction, so no custom wrapper is needed).
- **Service contracts** — `IRepositoryService`, `IIndexingService`, `IDependencyResolver`, `IActivationService`, … (interfaces only).
- **DTOs / read models** — the shapes crossing module boundaries (no EF entities leak out).
- **Domain events** — `VarIndexed`, `RepositoryRegistered`, `MigrationProposed`, … published via an `IEventBus`; how modules react to each other without coupling.
- **`IVarVaultPlugin`** — external plugins implement this (superset of `IModule`) so third-party/optional features (Hub, load-into-VaM) load from separate assemblies.

Anything a module or plugin needs from the outside world is an interface in the SDK. That is the reuse guarantee.

## Common kernel

`VarVault.Common` holds only what everything shares, no domain knowledge:
- `Result<T>` / `Result` + `Error` (typed, no-throw control flow for expected failures)
- `Guard` (argument validation)
- `IClock` / `SystemClock` (testable time — all UTC, per [decisions D-time])
- small primitives (paged result, progress `IProgressSink`)

## Module contract & lifecycle

Each `VarVault.Modules.X`:
1. Exposes one `public sealed class XModule : IModule`.
2. In `Register`, adds its own services (implementations of SDK interfaces) + subscribes to events.
3. Keeps all types `internal` except the SDK interfaces it implements → nothing leaks.

The **Host** collects modules (built-in via reference, plugins via assembly scan of a `plugins/` folder), calls `Register` on each into one container, then resolves the composed app. Module order independence is enforced (register-then-resolve; no cross-module calls during registration).

## Cross-cutting via Infrastructure (not per-module)

Shared infrastructure (the SQLite `DbContext`, the single-writer job queue, filesystem/symlink service, zip service, logging) lives in `VarVault.Infrastructure` and is registered once by the Host. Modules consume it through SDK interfaces (`IUnitOfWork`, `IFileSystem`, `ISymlinkService`, `IZipReader`, `IJobQueue`) — so a module never binds to EF or Win32 directly and stays unit-testable with fakes.

## Build & tooling foundation

- **Central package management** — `Directory.Packages.props` (one place for all versions).
- **`Directory.Build.props`** — `net10.0`, `LangVersion latest`, `Nullable enable`, `ImplicitUsings enable`, `TreatWarningsAsErrors` (core libs), analyzers on.
- **`global.json`** — pin the .NET 10 SDK band.
- **`.editorconfig`** — style + analyzer severities.
- **Tests** — `tests/VarVault.*.Tests` (xUnit + FluentAssertions), plus **`VarVault.Architecture.Tests`** that asserts the dependency rules above (NetArchTest) so the modular boundaries can't rot.

## How the deferred features stay clean
Hub browsing and load-into-VaM ([sealed OUT](./10-Decisions-Log.md)) become `IVarVaultPlugin` assemblies dropped in `plugins/` — they consume the same SDK the built-in modules do, with zero changes to core. That's the payoff of building the SDK boundary from day one.

---

*Foundation scaffold implementing this lives in `/src` and `/tests`; see the [checklist](./09-Implementation-Checklist.md) Phase 0 for the evidence-gated setup items.*
