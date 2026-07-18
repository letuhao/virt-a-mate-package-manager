# 14 — Testing & Observability Standards

How VarVault stays easy to test, debug, and QC. Backed by **`VarVault.TestKit`** (shared harness) and **`VarVault.Common/Diagnostics/Telemetry`** (monitoring), both already built and self-tested.

## 1. The test pyramid
| Level | What it covers | Where | Speed |
|---|---|---|---|
| **Unit** | pure logic, value objects, single services with fakes | `*.Tests` (Common, Domain) | ms |
| **Integration** | real wiring: DI host, SQLite DB, queues, EF | `Host.Tests`, `Infrastructure.Tests` | 10s–100s ms |
| **E2E** | a composed host driving a whole flow end-to-end | `VarVault.E2E.Tests` | ~sec |
| **UI-E2E** | view-model + control behavior under Avalonia's dispatcher | (added with the app) | ~sec |

Write the most tests at the bottom; a few high-value E2E flows at the top. Every module/service ships with tests **in the same slice** — the [checklist](./09-Implementation-Checklist.md) `[x]` needs test evidence.

## 2. Categories (QC filtering)
Every test class carries a category trait: `[Trait("Category", TestCategories.Unit|Integration|E2E)]`. Run a level in isolation:
```
dotnet test                                  # everything
dotnet test --filter "Category=Unit"         # fast inner loop
dotnet test --filter "Category=E2E"          # end-to-end flows
```

## 3. VarVault.TestKit (the shared probe)
Reference it from any test project. It provides:
- **`TestHost.Create(withPersistence, configure, extraModules)`** — a fully-composed host with deterministic seams: `FakeClock`, a temp data dir, captured logs, optional real SQLite persistence (auto-created), and extra test modules. `app.Get<T>()` resolves services.
- **`FakeClock`** — controllable UTC time (`Advance`/`Set`); the only sanctioned time source in tests.
- **`SqliteTestDatabase`** — a real temp-file SQLite `VarVaultDbContext` with baseline pragmas, for integration tests without the full host.
- **`TempDirectory`** — self-deleting temp folder for filesystem tests.
- **`CapturingLoggerProvider`** — records every `LogEntry` so tests assert on what was logged (installed by `TestHost`).
- **`PackageIds`** (object mother) and other builders for domain types.
- **`TestCategories`** constants.

The **`configure` seam** on `VarVaultHost.Build` lets tests override any service (fake clock, in-memory adapters, extra probes) — this is *the* testability hook; keep it working.

## 4. Determinism & doubles
- **No wall-clock coupling.** Inject `IClock` and use `FakeClock`. Avoid `Task.Delay`-based assumptions; where async completion is observed, **poll with a generous bound**, don't sleep-then-assert.
- **Prefer real implementations and hand-written fakes over mocking frameworks.** Test through **SDK interfaces**, not internals. A fake is a small deterministic implementation (`FakeClock`); reach for it before a mock.
- Integration tests use **real SQLite** (temp files, cleaned up), not the EF in-memory provider (it doesn't honor SQL/relational behavior).
- Tests are independent and parallel-safe: no shared mutable static state that races (reset counters; unique temp paths per test).

## 5. Observability / monitoring (the debug & QC surface)
Monitoring is built into the app so both production and tests can **see** what's happening.

### Tracing + metrics — `Telemetry` (single reused `ActivitySource` + `Meter`, name `"VarVault"`)
- **Traces:** operations start an `Activity` (`Telemetry.StartActivity`) — write queue, each job. Zero cost when nobody listens; an `ActivityListener`, OpenTelemetry exporter, or test can subscribe.
- **Metrics (instruments):** `varvault.writes.completed/failed`, `varvault.write.duration` (histogram, ms), `varvault.jobs.started/completed/failed`, and observable gauges `varvault.writequeue.depth`, `varvault.jobs.active`.
- **Assert metrics in tests** with `MetricCollector<T>` (Microsoft.Extensions.Diagnostics.Testing) over the instrument:
  ```csharp
  using var m = new MetricCollector<long>(Telemetry.JobsCompleted);
  … // run work
  Assert.True(m.GetMeasurementSnapshot().Count >= 1);
  ```

### Health checks — `HealthCheckService`
- `write-queue` (liveness: drains a no-op within 2s), `database` (readiness: reachable). Register more per module as they arrive. A "System status" surface (UI/CLI) calls `CheckHealthAsync()`; the E2E test asserts `Healthy`.

### Logging as a probe
- Structured `ILogger<T>` (Serilog). Tests capture and assert entries via `CapturingLoggerProvider`. A verified example: the host's startup line.

## 6. UI testing (when the Avalonia app lands)
- Test view-models as plain unit tests (no Avalonia needed) — fast, most coverage.
- Control/layout/input behavior: **Avalonia.Headless.XUnit** — `[AvaloniaTestApplication]` once per project, `[AvaloniaFact]` per test (sets up the UI thread), `Dispatcher.UIThread.RunJobs()` to flush async, simulated input via `Window` extensions. Use `[AvaloniaTestIsolation]` to reuse the app instance for speed.

## 7. Coverage & CI expectations
- Coverlet collects coverage (`--collect:"XPlat Code Coverage"`). Target meaningful coverage of Core/Application logic; don't chase 100% on trivial code.
- The build gate: `dotnet build` clean + `dotnet test` green + the **architecture-boundary tests** pass. A slice isn't "done" until its checklist items are `[x]` with test/benchmark evidence.

---

*Sources:* [Avalonia Headless XUnit](https://docs.avaloniaui.net/docs/testing/headless-xunit) · [.NET metrics instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation) · [Health checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks) · [Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels).
