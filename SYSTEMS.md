# SYSTEMS.md — Decisions, Errors and Learnings

The project's memory. If it matters later, it goes here. Every agent and every human reads this before changing code (see [AGENTS.md](AGENTS.md)).

| Section | What goes in it |
|---|---|
| [1. Decision Log](#1-decision-log) | Architecture and design decisions (ADR-lite). Append-only; supersede rather than edit. |
| [2. Error Log](#2-error-log) | Important bugs, failed approaches, environment problems and their fixes. |
| [3. Assumptions Register](#3-assumptions-register) | Assumptions about ambiguous requirements and their status. |
| [4. AI Corrections Log](#4-ai-corrections-log) | Where AI output was wrong or weak, how it was caught, and what changed. Feeds the README narrative and the video. |
| [5. Open Questions and Risks](#5-open-questions-and-risks) | Unresolved items. |
| [6. Invariants](#6-invariants) | Rules the code must never break. Tests should guard each one. |

**IDs:** decisions `D-NNN`, errors `E-NNN`, AI corrections `AI-NNN`. Never reuse or renumber. Reference IDs in commit messages and code comments where useful (e.g. `# see D-005`).

---

## 1. Decision Log

**Template**

```markdown
### D-NNN: <short title>
- **Date:** YYYY-MM-DD · **Status:** Accepted | Superseded by D-NNN | Proposed
- **Context:** what problem forced a choice
- **Decision:** what we chose
- **Alternatives:** what we rejected and why
- **Consequences:** trade-offs we accept; what it affects
```

### D-001: Scenario D, backend as the fully implemented layer
- **Date:** 2026-10-27 · **Status:** Accepted
- **Context:** The brief asks for one scenario and one fully implemented layer, with the other mocked.
- **Decision:** Scenario D (Unified Document Viewer). The backend is the graded layer. The frontend is a thin demo client (Day 4) with MSW mock mode.
- **Alternatives:** Frontend-first — weaker fit, because the scenario's hard problems (parallel fan-out, partial failure) are server-side.
- **Consequences:** Backend must meet "RESTful API + persistent database" (see D-003). OpenAPI spec and cURL examples serve as the client contract.

### D-002: FastAPI + httpx + asyncio.gather for fan-out
- **Date:** 2026-10-27 · **Status:** Superseded by D-013 (the principle — wrapped calls that never throw, gathered in parallel — still holds)
- **Context:** The backend must call two systems in parallel with per-call timeouts.
- **Decision:** FastAPI (async, auto OpenAPI), httpx AsyncClient per source, `asyncio.gather` over calls that are wrapped so they never raise.
- **Alternatives:** `asyncio.TaskGroup` cancels sibling tasks when one fails, which is the opposite of what partial results need. Go was considered but is slower to iterate within the timebox.
- **Consequences:** The resilience wrapper must convert every exception into a `SourceResult`; an unhandled exception inside `gather` is a bug.

### D-003: Persist last-known-good snapshots and a search audit in PostgreSQL
- **Date:** 2026-10-27 · **Status:** Accepted
- **Context:** The initial plan said "no persistence required", but the brief says a backend submission must use a persistent database (AI-001).
- **Decision:** Postgres (SQLite in tests) stores `source_snapshot` (per VIN+source, used as stale fallback ≤ 24 h) and `search_audit` (hashed VIN, outcome, latency).
- **Alternatives:** Persist nothing (fails the brief); copy all documents into our DB (we would become a second source of truth with sync problems).
- **Consequences:** Gives persistence a real reliability purpose. DB writes are best-effort and off the critical path; a DB outage must not fail a search.

### D-004: 200 with an empty list when a VIN has no documents
- **Date:** 2026-10-27 · **Status:** Accepted
- **Context:** The initial plan returned 404 when neither system had documents.
- **Decision:** `GET /vehicles/{vin}/documents` returns 200 with `documents: []` when both sources answer successfully with nothing.
- **Alternatives:** 404 — ambiguous with a wrong route, makes clients treat a normal outcome as an error, and we cannot know whether the vehicle "exists".
- **Consequences:** The UI shows an empty state, not an error. Only 400 (bad VIN) and 503 (both down, no snapshot) are error statuses.

### D-005: 2-second per-source budget that includes the single retry
- **Date:** 2026-10-27 · **Status:** Accepted
- **Context:** "2 s timeout + one retry" could reach 4 s or more (AI-003).
- **Decision:** Total per-source deadline 2.0 s. Per attempt: connect 0.5 s, read 1.5 s. Retry at most once, only on connect errors or 502/503/504, only if ≥ 0.5 s of budget remains. Never retry after a timeout.
- **Alternatives:** No retry (loses easy recoveries); retry with its own timeout (breaks the latency promise).
- **Consequences:** Worst-case response ≈ 2.2 s. Retries are counted in metrics so they cannot hide a degrading source.

### D-006: Never cache partial or failed responses
- **Date:** 2026-10-27 · **Status:** Accepted
- **Context:** Caching a partial response would hide one system's documents for the whole TTL after a one-second blip (AI-004).
- **Decision:** Only complete responses (all sources `ok`) are cached, for 60 s, keyed `docs:v1:{VIN}`, before filters.
- **Consequences:** During an outage, every request re-tries the failed source. The circuit breaker (D-007) limits that cost.

### D-007: Small in-house async circuit breaker, per source, per process
- **Date:** 2026-10-27 · **Status:** Superseded by D-014
- **Context:** A dead source should not cost every user 2 s.
- **Decision:** Opens after 5 consecutive failures, open for 30 s, half-open allows one probe. Around 80 lines, injectable clock, fully unit-tested.
- **Alternatives:** `pybreaker` (sync-oriented), `aiobreaker` (small community). Shared Redis state (more moving parts than needed now).
- **Consequences:** With N replicas each opens independently. Documented as future work.

### D-008: VIN check digit is not enforced
- **Date:** 2026-10-27 · **Status:** Accepted
- **Context:** ISO 3779 check digit (position 9) is mandatory only in North America. Seed data and the target market (Indian VINs, e.g. `MA3…`) do not satisfy it reliably.
- **Decision:** Validate only length (17) and charset (`[A-HJ-NPR-Z0-9]`), after trimming and upper-casing.
- **Consequences:** Some typos pass validation and return an empty result. Acceptable.

### D-009: Duplicates and conflicts are flagged, never merged
- **Date:** 2026-10-27 · **Status:** Accepted
- **Context:** The same paper may exist in both systems, sometimes with different data.
- **Decision:** Cross-source match on `(type, reference no.)`. Same dates ⇒ `POSSIBLE_DUPLICATE`; different dates ⇒ `CONFLICT`. Both records are always returned with their source.
- **Consequences:** The heuristic may give false positives, but it never hides data.

### D-010: Cache behind a port; in-memory by default, Redis optional
- **Date:** 2026-10-27 · **Status:** Accepted (implementation detail refined by D-015)
- **Context:** Redis is right for multiple replicas, but forcing it makes local runs and tests heavier.
- **Decision:** `Cache` protocol with `MemoryCache` (LRU+TTL) and `RedisCache`. Chosen by `REDIS_URL`. Redis errors are treated as misses.
- **Consequences:** Docker Compose runs Redis; tests use memory or fakeredis.

### D-011: VIN is PII — masked in logs, hashed in the DB, never a metric label
- **Date:** 2026-10-27 · **Status:** Accepted
- **Decision:** Logs show `WMI + ********* + last 4`. The audit table stores salted SHA-256. Metrics never carry VIN labels (also prevents cardinality blow-up).

### D-012: Deterministic sort — date desc, then source, then sourceId
- **Date:** 2026-10-27 · **Status:** Accepted
- **Context:** Equal dates would otherwise produce order that depends on which source answered first.
- **Consequences:** Stable cache contents, stable tests, stable UI.

### D-013: Re-platform to .NET 10 (LTS) / C#
- **Date:** 2026-10-27 · **Status:** Accepted · Supersedes D-002
- **Context:** The role being assessed is .NET/C#. The first plan and a partial scaffold (D1.1–D1.2) were Python/FastAPI (see AI-006).
- **Decision:** ASP.NET Core 10 Minimal APIs; Clean Architecture split into `DocViewer.Domain`, `.Application`, `.Infrastructure`, `.Api`; mocks as separate ASP.NET Core apps; `Task.WhenAll` over adapter calls that never throw. .NET 10 because it is the current LTS (support to Nov 2028).
- **Alternatives:** .NET 8 LTS (older; support ends Nov 2026, during the review window); MVC controllers (more boilerplate for four endpoints).
- **Consequences:** Python scaffold deleted; D1.1 and D1.2 restarted. All business rules, API contract and invariants are unchanged — only the implementation stack moved.

### D-014: Polly v8 via Microsoft.Extensions.Http.Resilience instead of a hand-written breaker
- **Date:** 2026-10-27 · **Status:** Accepted · Supersedes D-007
- **Context:** .NET has a standard, well-tested resilience library that plugs into `IHttpClientFactory` and emits telemetry.
- **Decision:** One `AddResilienceHandler` per source client: total timeout 2 s → retry (max 1, connect errors and 502/503/504 only, not attempt timeouts) → circuit breaker (failure ratio 0.5, min throughput 5, sampling 10 s, break 30 s) → attempt timeout 1.5 s.
- **Alternatives:** Hand-written breaker (more code to own); `AddStandardResilienceHandler` defaults (3 retries, 30 s total — breaks D-005).
- **Consequences:** Polly's breaker is ratio-based, not "5 consecutive failures"; with low traffic the min-throughput of 5 makes it behave almost the same. Must override the default `ShouldHandle`, which retries on timeouts (would break D-005).

### D-015: `IDistributedCache` behind `IDocumentCache`; not `HybridCache`
- **Date:** 2026-10-27 · **Status:** Accepted · Refines D-010
- **Context:** `HybridCache` adds stampede protection and L1/L2 caching, but `GetOrCreateAsync` stores whatever the factory returns. We must decide after the fan-out whether to cache (D-006).
- **Decision:** `IDocumentCache` over `IDistributedCache` (memory by default, Redis when `ConnectionStrings:Redis` is set), plus a per-VIN `SemaphoreSlim` single-flight in `AggregationService`.
- **Alternatives:** `HybridCache` with an exception thrown from the factory to skip caching partials — rejected as control flow by exception.
- **Consequences:** A little more code (single-flight) that we own and test.

### D-016: Test stack — xUnit, Shouldly, NSubstitute, WireMock.Net, WebApplicationFactory
- **Date:** 2026-10-27 · **Status:** Accepted
- **Context:** Need fast unit tests plus realistic HTTP failure simulation (delays, 503, malformed JSON) for adapters and Polly.
- **Decision:** xUnit; Shouldly for assertions; NSubstitute where hand fakes are clumsy; WireMock.Net as a real HTTP server for sources; `WebApplicationFactory<Program>` for API tests; `FakeTimeProvider` for time; coverlet for coverage.
- **Alternatives:** FluentAssertions — version 8 is under a commercial licence; avoided to keep the repo free to use. Moq — past telemetry controversy; NSubstitute is equivalent.

### D-017: Build hygiene — Central Package Management, nullable, warnings as errors
- **Date:** 2026-10-27 · **Status:** Accepted
- **Decision:** `Directory.Packages.props` holds every package version; `Directory.Build.props` sets `Nullable=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`, `ImplicitUsings=enable`. `global.json` pins the SDK major version.
- **Consequences:** AI-generated code that ignores nullability or misuses APIs fails the build instead of slipping through review.

### D-018: Mock seed data uses relative date tokens
- **Date:** 2026-10-27 · **Status:** Accepted
- **Context:** Expiry scenarios ("PUC expiring in 10 days", "insurance expired") would become wrong days after submission if seed dates were fixed.
- **Decision:** Seed JSON may contain `{{today±Nd}}`; `SeedStore` resolves tokens at mock start-up into each mock's own date format (ISO for Sales, `dd/MM/yyyy` for Service).
- **Consequences:** Demos and tests stay correct whenever they run. Mocks use process-local "today" (UTC in Docker); a few hours' skew vs. IST is irrelevant for ±N-day scenarios.

### D-019: Database provider chosen by configuration — SQLite locally, PostgreSQL in Docker
- **Date:** 2026-10-27 · **Status:** Accepted · Refines D-003
- **Context:** Reviewers should be able to `dotnet run` without installing PostgreSQL, but production-like runs need PostgreSQL.
- **Decision:** `Persistence:Provider` = `Sqlite` (default; `EnsureCreated`) or `Postgres` (EF migrations, generated for Npgsql via a design-time factory). Snapshot age is checked in memory after a PK lookup, because SQLite cannot translate `DateTimeOffset` comparisons.
- **Alternatives:** PostgreSQL only (heavier local setup); provider-neutral migrations (EF does not support one set across providers).
- **Consequences:** SQLite and PostgreSQL can behave differently; D5.5 adds Testcontainers PostgreSQL tests to catch that.

### D-020: Single-flight by sharing the in-flight fan-out task, not a per-VIN `SemaphoreSlim`
- **Date:** 2026-10-27 · **Status:** Accepted · Refines D-015
- **Context:** D-015 planned a per-VIN `SemaphoreSlim` so concurrent cache misses do one fan-out. Semaphores need cleanup (one per VIN forever otherwise), and callers queued behind the lock would re-check the cache, then still fan out if the first result was partial (partials are never cached).
- **Decision:** `AggregationService` keeps a `ConcurrentDictionary<Vin, Lazy<Task<AggregatedDocuments>>>`. Concurrent callers await the same task; the entry is removed when it completes. The shared fan-out runs with no caller token (bounded at ~2.2 s by D-005) in its own DI scope; each caller can still stop waiting via `WaitAsync(token)`.
- **Alternatives:** Semaphore + cache re-check (more code, partials still stampede); `HybridCache` (rejected in D-015).
- **Consequences:** Partial results are shared by concurrent callers too (good: one failing source is called once, not N times). The service is a singleton and resolves typed-client adapters per fan-out from a new scope.

### D-021: API response details settled during D2.5
- **Date:** 2026-10-27 · **Status:** Accepted
- **Decision:** (1) `summary` counts the documents in the response, i.e. after filters, so `summary.total == documents.length`. (2) Filters accept wire names case-insensitively, repeated or comma-separated (`?type=PUC,INSURANCE`); numeric enum values are rejected. (3) Source and circuit states are lower snake case (`circuit_open`, `half_open`); all other enums upper snake case. (4) The schema is created at startup in every environment; a database that is down at startup is logged and the API starts anyway (invariant 10). (5) Invalid-VIN searches are audited with outcome `invalid`.
- **Consequences:** DESIGN.md §7 updated to match.

### D-022: Observability stack details (D3.1–D3.5)
- **Date:** 2026-10-27 · **Status:** Accepted
- **Decision:** (1) Serilog via `AddSerilog(..., preserveStaticLogger: true)` with `RenderedCompactJsonFormatter`; the access log records the route template, never the path. (2) `DocViewerMetrics` lives in Application (BCL `System.Diagnostics.Metrics` + `IMeterFactory`), so the aggregator, adapters and cache record into one meter. (3) OpenTelemetry 1.20.1; the Prometheus exporter and EF Core instrumentation are `-beta` packages because they have never shipped stable. (4) Spans pass through `VinRedactionProcessor`, which masks any standalone 17-character alphanumeric token, valid VIN or not. (5) The OTLP exporter is on only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (compose sets it to Jaeger). (6) `docs/openapi.json` is generated at build time; during generation the app runs with a placeholder salt and skips database start-up work (`StartupTasks.IsGeneratingOpenApiDocument`).
- **Alternatives:** prometheus-net (metrics only, no tracing); per-instrumentation `Enrich*` callbacks for redaction (behave differently on .NET 10, where HttpClient emits its own activities).
- **Consequences:** Beta packages may change API between minor versions; pinned in `Directory.Packages.props`. Counter tags are lower snake case (`source="sales"`, `outcome="circuit_open"`).

### D-023: API container healthcheck via a built-in `--healthcheck` mode
- **Date:** 2026-10-27 · **Status:** Accepted
- **Context:** Kumar asked for Docker to report the API's health. The chiselled runtime image has no shell or curl, so a normal `HEALTHCHECK` command cannot run.
- **Decision:** `dotnet DocViewer.Api.dll --healthcheck [path]` exits before the app is built, GETs `http://localhost:$ASPNETCORE_HTTP_PORTS/readyz` (3 s timeout) and exits 0 on 2xx, else 1. Compose: every 10 s, 3 retries, 20 s start period.
- **Alternatives:** non-chiselled image with curl (larger attack surface); probing `/healthz` (would never show a database outage).
- **Consequences:** Each probe starts a short-lived `dotnet` process (~100 ms CPU). Probing `/readyz` means a Postgres outage shows the API as `unhealthy` in Docker while searches still work; Docker Compose does not restart unhealthy containers, so this is informational. Tied to Q3.

### D-024: `/readyz` reports a database outage as `Degraded` (HTTP 200), not 503
- **Date:** 2026-10-28 · **Status:** Accepted · Resolves Q3 · Refines D-023
- **Context:** `/readyz` returned 503 when the database was down, yet searches keep working without it (invariant 10). A load balancer would pull every instance out on a database blip and turn a soft degradation into a full outage.
- **Decision:** (Kumar, 2026-10-28) `DatabaseHealthCheck` returns `Degraded`; ASP.NET Core maps that to 200 with body `Degraded`. Only a dead process fails readiness.
- **Alternatives:** keep 503 (previous design); drop the DB from readiness entirely (would hide the outage from anyone reading the body).
- **Consequences:** The Docker healthcheck (D-023) now stays `healthy` with Postgres stopped; the outage is visible in the `/readyz` body, the logs and the DB error counters instead. Test: `DatabaseDown_ReadyzIsDegraded_AndSearchStillSucceeds`.

### D-025: Demo client in Blazor WebAssembly (C#), not React
- **Date:** 2026-10-28 · **Status:** Accepted · Changes the Day 4 plan (D-001 still holds: the UI is an extra)
- **Context:** Day 4 planned Vite + React + MSW + Vitest. Kumar chose Blazor WebAssembly so the whole repo is C# and needs no Node.js.
- **Decision:** `frontend/DocViewer.Web` (Blazor WASM standalone) references `DocViewer.Domain` only, so VIN hints use the real `Vin.TryParse`. Wire DTOs are small records in the client, guarded by a test that compares them with `docs/openapi.json` (no code generator). Mock mode = an in-process `HttpMessageHandler` (`Api:UseMocks`) that serves every state; MSW's role. Tests: bUnit + xUnit + Shouldly. One plain stylesheet with colour tokens (light and dark), no Tailwind. Served by `nginx:alpine` in compose on :5173. The UI fetches unfiltered results and filters on the client, so the expiry banner always counts the whole vehicle.
- **Alternatives:** React (needs Node, a second language); NSwag/Kiota client generation (one more build-time dependency for five DTOs); Blazor Server (needs a server connection per user).
- **Consequences:** WASM first load is a few MB (17 MB uncompressed; nginx serves the pre-compressed files). Mock mode is chosen at build time (`-p:WasmApplicationEnvironmentName=Mock`, AI-011). Mocks get a CORS policy for the UI origin so the health panel can flip `/__admin/fault` (demo only).

<!-- Add new decisions above this line, continuing from D-026. -->

---

## 2. Error Log

Record anything that cost more than ~15 minutes, would bite someone again, or changed the design.

**Template**

```markdown
### E-NNN: <short symptom>
- **Date:** YYYY-MM-DD · **Task:** D?.? · **Severity:** low | medium | high
- **Symptom:** what was observed (error text, failing test)
- **Root cause:** why it happened
- **Fix:** what changed (files / commit)
- **Prevention:** test, lint rule, or doc that stops it recurring
```

### E-001: Flaky resilience tests (timing and WireMock logging)
- **Date:** 2026-10-27 · **Task:** D2.1 · **Severity:** medium
- **Symptom:** `SlowSource_TimesOut…` saw 0 requests; `TransientFailureThenSuccess…` intermittently returned `Timeout` (3 of 10 full-suite runs failed).
- **Root cause:** (1) WireMock.Net logs a request only after its response is sent, so requests aborted by our timeout are never counted. (2) The first HTTP call in a test process (JIT + connect) can exceed a very short attempt timeout, made worse by other test classes running in parallel with multi-second delays.
- **Fix:** Prove "no retry" by elapsed time instead of request count; test budgets of 1.0 s attempt / 2.0 s total; timing-sensitive tests in a non-parallel xUnit collection (`TimingSensitiveGroup`).
- **Prevention:** 0 failures in 10 consecutive runs after the fix. Rule: never assert on WireMock request counts for aborted requests; keep timing tests isolated.

### E-002: Analyzer CA1711 rejects type names ending in `Flag` / `Collection`
- **Date:** 2026-10-27 · **Task:** D1.3, D2.1 · **Severity:** low
- **Symptom:** Build failed (warnings as errors) on `DocumentFlag` and `TimingSensitiveCollection`.
- **Fix:** Renamed to `DocumentMarker` (JSON field stays `flags`) and `TimingSensitiveGroup`.
- **Prevention:** Working as intended (D-017). Note: a failed build makes `dotnet test --no-build` run stale binaries — always check the build result first.

### E-003: Template files use CRLF; format check failed
- **Date:** 2026-10-27 · **Task:** D1.1 · **Severity:** low
- **Symptom:** `dotnet format --verify-no-changes` reported `ENDOFLINE` on files created by `dotnet new` (and later EF migrations).
- **Fix:** `dotnet format` once; `.gitattributes` with `* text=auto eol=lf` so Windows checkouts with `core.autocrlf` cannot reintroduce CRLF; migrations marked `generated_code = true` in `.editorconfig`.

### E-004: .NET SDK install via winget stalled on a UAC prompt
- **Date:** 2026-10-27 · **Task:** D1.1 · **Severity:** low (environment)
- **Symptom:** `winget install Microsoft.DotNet.SDK.10` sat at "Starting package install…" for 10+ minutes.
- **Fix:** Installed SDK 10.0.403 per-user with Microsoft's `dotnet-install.ps1` into `%LOCALAPPDATA%\Microsoft\dotnet` (no admin). Shells must put that folder on `PATH` and set `DOTNET_ROOT` until the system-wide install completes.

### E-005: False 503 in API tests on the first run after a build
- **Date:** 2026-10-27 · **Task:** D2.5 · **Severity:** low
- **Symptom:** First `dotnet test` after a build: `UnknownVehicle…` got 503 instead of 200 (test took 6 s) and a second test saw no `X-Cache` header. Both passed on every later run (3 class runs + 5 full-suite runs, 0 failures). The same run also failed 10 tests on deleting the temp SQLite file ("being used by another process").
- **Root cause:** (1) Most likely the E-001 cold-start effect: JIT and first connections pushed both source calls past the 1.5 s attempt timeout, so both "timed out". Not reproduced, so this is the best explanation, not a proven one. (2) Fire-and-forget audit writes can still hold the SQLite file when the test host is disposed.
- **Fix:** API tests use a 10 s / 20 s budget (they test the contract; timing is covered by `SourcePipelineTests`); temp-file delete is best effort.
- **Prevention:** Same rule as E-001: tests that are not about timing must not run on production timing budgets.

### E-006: Parallel test hosts shared process-wide logging and tracing state
- **Date:** 2026-10-27 · **Task:** D3.1, D3.3 · **Severity:** medium
- **Symptom:** Logging tests saw a neighbour's access-log line and none of their own events; a span test threw "Collection was modified" in 2 of 3 runs.
- **Root cause:** `AddSerilog` replaces and logs through the static `Log.Logger`, and the request-logging middleware falls back to it, so the last-built host captured every host's events. `ActivitySource` listeners are process-wide, so each test host's exporter also receives other hosts' spans while a test enumerates them.
- **Fix:** `preserveStaticLogger: true` and an explicit `RequestLoggingOptions.Logger` (product code: no global state); a locked `SpanCollection` in tests and filtering by the test's own trace ids.
- **Prevention:** 5/5 clean runs. Rule: tests that capture telemetry must filter by their own trace id.

### E-007: Raw VIN leaked through log scope and span URL tags
- **Date:** 2026-10-27 · **Task:** D3.1, D3.3 · **Severity:** high (privacy, invariant 7)
- **Symptom:** The new invariant-7 tests failed: every log event during a search carried `RequestPath=/api/v1/vehicles/MA3EWB81S00100001/documents`, and spans carried it in `url.path` / `url.full`. A VIN with a typo (`…0000O`) was not masked by the first redaction regex.
- **Root cause:** ASP.NET Core opens a logging scope with `RequestPath` for every request and Serilog copies scope properties onto each event; HTTP instrumentation records URLs. The regex accepted only valid VIN characters.
- **Fix:** `DropRequestPathEnricher`; access log uses the route template; `VinRedactionProcessor` masks any standalone 17-character token.
- **Prevention:** `RawVin_NeverAppearsInAnyLogEvent`, `ExportedSpans_NeverContainRawVin`, `Metrics_…WithoutVinLabels`, `VinRedactionTests`.

### E-008: `dotnet build` hung with no output for 10 minutes
- **Date:** 2026-10-27 · **Task:** D3.3 · **Severity:** low (environment)
- **Symptom:** A build + test command produced no output until the 10-minute tool timeout; only idle MSBuild node processes remained.
- **Root cause:** Not determined; most likely a stuck MSBuild node-reuse server after many consecutive builds.
- **Fix:** `dotnet build-server shutdown`, then the same build finished normally. Long commands now run with `timeout` and `--blame-hang-timeout` so a hang names itself.

### E-009: Docker images failed to build, then the API crash-looped
- **Date:** 2026-10-27 · **Task:** D3.4 · **Severity:** medium
- **Symptom:** (1) `dotnet publish` in Docker failed with NETSDK1152 "multiple publish output files with the same relative path: app.dll". (2) After fixing that, the API container restarted forever: `BusinessRulesOptions.TimeZone: Unknown timezone 'Asia/Kolkata'`.
- **Root cause:** (1) The agent passed `-p:AssemblyName=app` to give every image the same entry point; a command-line property is global, so the referenced `Mocks.Common` library was also renamed to `app.dll`. (2) The plain chiselled runtime image has no tzdata.
- **Fix:** One final Dockerfile stage per app with its own `ENTRYPOINT`, chosen by compose `target:`; runtime image `aspnet:10.0-noble-chiseled-extra` (adds tzdata and ICU).
- **Prevention:** Options validation on start did its job: a wrong timezone fails fast instead of computing expiry in UTC. CI builds the images (`docker` job).

### E-010: One 503 for a search right after `docker compose up --build`, with Postgres stopped
- **Date:** 2026-10-27 · **Task:** D-023 verification · **Severity:** medium (not reproduced)
- **Symptom:** While testing the healthcheck, the first search after the stack was built and started returned 503 while Postgres was stopped. Two reruns (warm, and freshly recreated API) returned 200 within 0.2–0.7 s per source.
- **Root cause:** Unproven; the response body was not captured. Most likely both source calls exceeded the 1.5 s attempt timeout on a cold, busy host (E-005), and with the database down there was no snapshot, so 503 is the designed answer (DESIGN §6.3). Postgres itself is not on the search path (confirmed: searches return 200 with it stopped).
- **Fix:** None yet. Possible hardening: warm up the source connections at start-up so the first user request is not the cold one.
- **Prevention:** When a check fails unexpectedly, capture the full response before rerunning (`curl -i` to a file).

### E-011: One API test failed on the first full run of the session, then passed 5 times
- **Date:** 2026-10-28 · **Task:** session start · **Severity:** low (not reproduced)
- **Symptom:** First `dotnet test` after a cold build: `DocViewer.Api.Tests` 1 failed / 44 passed, and the project took 24 s instead of 5–10 s. The test's name was not captured (the output filter only printed totals). 3 isolated runs and 2 full-suite runs with TRX logging afterwards: 0 failures.
- **Root cause:** Unknown. Same shape as E-005/E-010 (cold JIT plus all test projects in parallel).
- **Fix:** None. Session-start runs now use `--logger trx` so a failing name is never lost.
- **Prevention:** If it recurs, the TRX file names the test; then fix the cause, not the assertion (AGENTS.md §9.7).
- **Update (same day):** reproduced and fixed, see E-013.

### E-012: API container turned `unhealthy` with Postgres stopped, although `/readyz` answered 200
- **Date:** 2026-10-28 · **Task:** D-024 verification in Docker · **Severity:** medium
- **Symptom:** After the `Degraded` change, `docker compose stop postgres` still flipped the API to `unhealthy` (probe exit codes `0 1 1 1`). `/readyz` answered `Degraded` but took 3.8–4.0 s; the probe gives up at 3 s (D-023).
- **Root cause:** Connecting to a stopped compose service takes ~4 s to fail, and that wait does not stop when the health check's token is cancelled, so neither the registration `timeout` nor cancellation helped (tried first; still 3.9 s). The unit test used a closed port on 127.0.0.1, which is refused instantly, so it never saw a slow failure.
- **Fix:** `DatabaseHealthCheck.ProbeAsync` stops waiting after 1 s itself (`WaitAsync`), reports `Degraded`, and disposes the context only when the abandoned connect finishes. Re-verified in Docker: `/readyz` in ~1.0 s, container `healthy` for 5 probes with Postgres down, `Healthy` again after restart.
- **Prevention:** `DatabaseHealthCheckTests` (a connect that never answers). Rule: test outages as hangs, not only as refusals.

### E-013: First test run after a build: probe test failed, and its sibling passed for the wrong reason (cause of E-011)
- **Date:** 2026-10-28 · **Task:** D3.7 clean-copy check · **Severity:** medium
- **Symptom:** On a clean copy, `dotnet build --no-incremental` then `dotnet test` failed `ContainerHealthCheckTests.Probe_ExitCodeFollowsStatus(200 → 0)` 2 of 2 times; later runs passed.
- **Root cause:** Instrumenting the probe (scratch copy only) showed every request to the fresh WireMock server took > 3 s on the cold run (JIT while six test projects start in parallel). The test used the production 3 s timeout, so 200 timed out as "unhealthy", and the 503 case passed only because it also timed out.
- **Fix:** Status-mapping tests use a 30 s timeout and assert WireMock really answered with that status. Two cold rebuild + test runs: 306/306.
- **Prevention:** Same rule as E-001/E-005, now applied to the healthcheck tests: only timing tests run on timing budgets.

### E-014: Load test misses the cache-hit p95 target at 50 users (133 ms vs 20 ms)
- **Date:** 2026-10-28 · **Task:** D5.4 · **Severity:** medium (open)
- **Symptom:** `search_hit_ms p(95)=132.51ms` (threshold 20 ms) over 2 min at 50 VUs; median 5.5 ms, max 1.8 s. Single user: p95 7.8 ms. Miss p95 251 ms (target 300 ms) and 0 % errors passed.
- **Root cause:** Not proven. Evidence against the API code: thread-pool queue length 0 throughout, GC pauses ~8 ms per second. Evidence for the machine: Docker's 6-CPU VM was shared by an unrelated `minikube` container at ~200 % CPU, k6 itself (17 %), the API (60 %) and Postgres (20 %, one audit row per search, cache hits included). Thread count ramped 5 → 19 at the start, which may explain early outliers.
- **Fix:** None. The threshold was **not** relaxed (AGENTS.md §6).
- **Next:** Re-run with minikube stopped. If p95 is still > 20 ms, profile the hit path (audit write, JSON serialisation, logging) and consider `ThreadPool.SetMinThreads` only with measurements.

<!-- Add new errors above this line, continuing from E-015. -->

---

## 3. Assumptions Register

Mirrors DESIGN.md §3. Change the status here if an assumption is revisited.

| ID | Assumption | Status |
|---|---|---|
| A1 | Documents are metadata records, not files | Active |
| A2 | VIN is the only search key; 17 chars, no I/O/Q, case-insensitive | Active |
| A3 | VIN check digit not enforced | Active (D-008) |
| A4 | Cross-source duplicates are shown per source and flagged | Active (D-009) |
| A5 | 2 s per-source budget is acceptable | Active (D-005) |
| A6 | Sources are read-only, may be slow, flaky or malformed | Active |
| A7 | 60 s staleness on cache hit; ≤ 24 h when served as labelled fallback | Active |
| A8 | Auth out of scope | Active |
| A9 | Empty result ⇒ 200 with empty list | Active (D-004) |
| A10 | Dates are local (IST); expiry evaluated in that zone | Active |

---

## 4. AI Corrections Log

Evidence for the brief's "AI Engineering & Verification" criterion. Record **every** meaningful case where AI output was wrong, risky or weaker than it looked — design or code.

**Template**

```markdown
### AI-NNN: <what the AI proposed>
- **Date / phase:** YYYY-MM-DD · design | implementation | testing | docs
- **AI output:** what it said or generated
- **Problem:** why it was wrong or risky
- **How it was caught:** review against brief / failing test / reading docs / manual run
- **Correction:** what was done instead (link D-/E- entries)
```

### AI-001: "Persistence: none required"
- **Date / phase:** 2026-10-27 · design
- **AI output:** The first-pass plan stated no persistence was needed because data lives in the source systems.
- **Problem:** The brief says a backend submission must "use a persistent database". This would have failed a stated requirement.
- **How it was caught:** Line-by-line review of the plan against the brief's Part 2.
- **Correction:** D-003 — snapshots + audit in Postgres, which also enables stale fallback.

### AI-002: 404 when no documents exist
- **Date / phase:** 2026-10-27 · design
- **AI output:** "404: No documents found in either system."
- **Problem:** Treats a normal outcome as an error; ambiguous with a wrong route.
- **How it was caught:** Reviewing REST semantics of the response matrix.
- **Correction:** D-004.

### AI-003: Timeout and retry budgets that do not add up
- **Date / phase:** 2026-10-27 · design
- **AI output:** "Timeout per call (2 s), one retry."
- **Problem:** A timed-out call followed by a retry can take 4 s+, breaking the latency assumption.
- **How it was caught:** Walking the worst-case timeline by hand.
- **Correction:** D-005 — one 2 s budget including the retry; no retry after timeout.

### AI-004: Cache without a rule for partial results
- **Date / phase:** 2026-10-27 · design
- **AI output:** "Stores the result in the cache" after any response, including partial ones.
- **Problem:** A momentary failure would be served from cache for the full TTL.
- **How it was caught:** Tracing the failure-path matrix through the cache step.
- **Correction:** D-006.

### AI-005: VIN rule silent on the check digit
- **Date / phase:** 2026-10-27 · design
- **AI output:** "17 characters, no I, O, Q."
- **Problem:** Implementers (or a later AI session) might "helpfully" add check-digit validation, which rejects many Indian VINs.
- **How it was caught:** Domain review (seed VINs are Maruti `MA3…`).
- **Correction:** D-008, recorded explicitly so no agent adds it.

### AI-006: Agent picked the tech stack without asking about the target role
- **Date / phase:** 2026-10-27 · implementation
- **AI output:** Claude Code inferred Python/FastAPI from my GitHub repos, wrote the design around it and started scaffolding (D1.1–D1.2 done in Python).
- **Problem:** The role is .NET/C#. The stack is a requirement-level decision that the agent should have confirmed, not inferred.
- **How it was caught:** I reviewed the scaffold mid-step and stopped the agent.
- **Correction:** D-013. Python scaffold deleted; design re-mapped to idiomatic .NET (D-014, D-015, D-016, D-017) rather than translated line by line. AGENTS.md §8 now lists "choosing or changing the tech stack" as ask-first.

### AI-007: Agent-written tests that tested the wrong thing
- **Date / phase:** 2026-10-27 · testing
- **AI output:** Claude Code wrote (a) a "no retry" test that counted WireMock requests, (b) resilience tests with a 0.5 s attempt budget, (c) a test expecting `JsonSerializer.Deserialize<Vin>("null")` to throw, and (d) added `VinJsonConverter` without tests, dropping Domain coverage from 99 % to 94 % (below the 95 % bar).
- **Problem:** (a) and (c) asserted behaviour the libraries don't have; (b) produced a flaky suite; (d) silently broke an acceptance criterion of a task already marked done.
- **How it was caught:** Failing tests were investigated before any fix (detailed logs, repeated runs: 3 of 10 failing); coverage was re-measured at the end of the session rather than trusted.
- **Correction:** E-001 fixes; the null test now documents real `System.Text.Json` behaviour; converter tests added (Domain back to 99 %). Lesson recorded in AGENTS.md §9: re-run coverage after every task, and investigate a failure before editing the test.

### AI-008: Agent treated a log grep as proof that VINs were not logged
- **Date / phase:** 2026-10-27 · testing
- **AI output:** During the D2.6 manual run, Claude Code grepped the API's console log, found 0 raw VINs and reported invariant 7 as verified.
- **Problem:** The default console formatter does not print log scopes. Every event actually carried the raw VIN in a `RequestPath` scope property, and spans carried it in URL tags (E-007). The grep checked the formatter's output, not the data that would be shipped.
- **How it was caught:** D3.1's automated test rendered events with the production JSON formatter and failed immediately.
- **Correction:** Invariant 7 is now proven by tests over the exact exported shape (log JSON, span tags, Prometheus text). Lesson: verify privacy against what leaves the process, not against a human-readable view.

### AI-009: Agent wrote README facts before checking them
- **Date / phase:** 2026-10-28 · docs
- **AI output:** While drafting the README (D3.7), Claude Code listed the Prometheus metrics as `docviewer_search_*`, `docviewer_source_*`, `docviewer_cache_*`. There is no `docviewer_search_*` metric; the real names are `docviewer_source_requests_total`, `docviewer_partial_responses_total`, `docviewer_stale_served_total`, etc.
- **Problem:** A reviewer following the README would look for metrics that do not exist. Plausible-looking names are the easiest hallucination to miss.
- **How it was caught:** The agent checked every concrete name in the draft against the running API (`GET /metrics`) and the source before closing the task.
- **Correction:** README lists the real names. Every output example in the README was captured from a real run in the same session, not written by hand.

### AI-010: Agent-written tests that passed without testing the failure they named
- **Date / phase:** 2026-10-28 · testing
- **AI output:** (a) `Probe_ExitCodeFollowsStatus(503 → 1)` (previous session) passed even when the probe timed out instead of reading the 503. (b) This session's `DatabaseDown_ReadyzIsDegraded…` used a refused port, so the real outage (a hang) still broke the container healthcheck. (c) A first attempt to simulate a hanging database with a SQLite connection interceptor passed for "Healthy": SQLite's `CanConnect` only checks the file and never opens a connection.
- **Problem:** Green tests that would stay green with the bug present.
- **How it was caught:** (a) cold-run failure investigated with instrumentation instead of rerunning until green; (b) the real Docker scenario; (c) the new test failed against code that should have passed it, and the agent checked why before changing anything.
- **Correction:** E-012, E-013. Tests now assert the evidence (WireMock's logged status; a connect task that never completes).

### AI-011: Agent assumed a launch profile sets the Blazor WebAssembly environment
- **Date / phase:** 2026-10-28 · implementation
- **AI output:** A `mock` launch profile with `ASPNETCORE_ENVIRONMENT=Mock`, documented as the way to start mock mode.
- **Problem:** In .NET 10 the WebAssembly environment is fixed at build time by `WasmApplicationEnvironmentName` (default `Development`); the profile did nothing. It was also first "verified" against port 5173, which the Docker `web` container was serving.
- **How it was caught:** No `Blazor-Environment` header, then the installed SDK's `Microsoft.NET.Sdk.WebAssembly.Browser.targets`, then the boot config (`"applicationEnvironment": "Mock"`) of a build with the property.
- **Correction:** Profile removed; docs use `-p:WasmApplicationEnvironmentName=Mock`. Lesson: check which server answered before trusting a check.

<!-- Add new AI corrections above this line, continuing from AI-012. -->

---

## 5. Open Questions and Risks

| # | Item | Owner | Status |
|---|---|---|---|
| Q1 | Do we need pagination for very long histories, or is a single list fine for the demo? | Kumar | Open — backlog |
| Q2 | Should stale fallback be disabled by config for audit-sensitive dealers? | Kumar | Open |
| Q3 | `/readyz` returns 503 when the database is down (DESIGN §7.1), but searches still work without it (invariant 10). Should readiness report DB loss as *degraded* (200) so a DB blip does not pull every instance from the load balancer? | Kumar | Closed 2026-10-28 — *degraded* (200), D-024 |
| R1 | Per-process breaker state across replicas (D-007) | — | Accepted risk |
| R2 | Duplicate heuristic false positives (D-009) | — | Accepted risk, tested |

---

## 6. Invariants

Each of these must have at least one test. If you change one, write a new decision entry first.

1. A single source failure **never** produces a non-200 response (D-013, DESIGN §6.3).
2. Every document in a response has a `source` field.
3. `partial == true` ⇔ at least one source status is not `ok`.
4. Partial responses are never cached (D-006).
5. Total request time is bounded by ~2.2 s regardless of source behaviour (D-005).
6. Invalid VINs cause zero downstream calls.
7. Raw VINs never appear in logs or metric labels (D-011).
8. Output ordering is deterministic (D-012).
9. Source-specific field names appear only inside `DocViewer.Infrastructure/Sources/` and `mocks/`.
10. Redis or Postgres outages degrade features, never fail a search (D-003, D-010).
11. `DocViewer.Domain` references no other project and no I/O packages; `DocViewer.Application` references only Domain (D-013).
