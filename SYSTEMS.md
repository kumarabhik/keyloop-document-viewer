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
- **Date:** 2026-10-27 · **Status:** Accepted · Supersedes D-007 · Breaker minimum throughput superseded by D-027
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

### D-026: Contract fuzzing in CI; the spec states the canonical input, the API stays lenient
- **Date:** 2026-10-28 · **Status:** Accepted · Backlog item chosen by Kumar
- **Context:** Schemathesis generates requests from `docs/openapi.json` and checks the answers. Its first run showed the spec allowed any string as a VIN, so a rejected `0` looked like a bug.
- **Decision:** (1) `DocumentParametersTransformer` adds the canonical VIN pattern `^[A-HJ-NPR-Z0-9]{17}$` and the filter enums (built from the Domain enums the API parses). (2) `OptionalWhenNullSchemaTransformer` drops `WhenWritingNull` properties from `required` (E-016). (3) CI job `contract-fuzz`: compose stack + `schemathesis/schemathesis:4.31.1`, all checks except `negative_data_rejection`, 90 s, JUnit report artifact.
- **Alternatives:** make the API strict (breaks D-021 leniency: lower case, spaces, comma lists, empty filter); encode the leniency as case-insensitive regexes (unreadable contract); fuzz without the negative-check exclusion (it flags only `?source=`, verified with a dedicated run).
- **Consequences:** Clients see what to send; the API accepts a little more. Rejection of bad input stays covered by our own tests (invariant 6). Fuzzing is random: a CI failure prints a seed and a `curl` to reproduce.

### D-027: Breaker minimum throughput 5 → 3
- **Date:** 2026-10-30 · **Status:** Accepted (Kumar) · Supersedes the min-throughput part of D-014 · Resolves R4
- **Context:** D-014 claimed the ratio breaker with min throughput 5 "behaves almost the same" as "5 consecutive failures" at low traffic. Measured 2026-10-29: 8 timeouts ~3.6 s apart left it `closed`; every user paid ~1.5 s (AI-014).
- **Decision:** `Resilience:BreakerMinimumThroughput = 3`; ratio 0.5, sampling 10 s and break 30 s unchanged.
- **Alternatives:** also lengthen sampling to 30 s (judges a recovered source on older history); switch to a consecutive-failures breaker (custom code, which D-014 avoided); leave it (R4 stays).
- **Consequences:** Same experiment after the change: the breaker opened by the 3rd search; later searches answered in 0.01–0.04 s from the saved copy. Below 3 calls per 10 s it still never opens; each search then stays within invariant 5 anyway. Two failures out of three now trip it, so a brief blip can open it for 30 s; the half-open probe recovers it.

### D-028: Per-IP rate limit on searches, 429 with Retry-After
- **Date:** 2026-10-30 · **Status:** Accepted (Kumar) · Resolves R5 · Adds a status code to the contract
- **Context:** No rate limit and no auth (A8): anyone could try VIN after VIN and read every match.
- **Decision:** ASP.NET Core's built-in limiter (no new package), fixed 1-minute window per client IP, `RateLimit:PermitsPerMinute` = 60 (validated), no queue (a queued search would break invariant 5). Documents endpoint only; health, metrics and OpenAPI are not limited. Over the limit ⇒ 429 problem (`…/too-many-requests`) with `Retry-After` and `traceId`; no source is called. Invalid-VIN requests count too. The UI shows "Too many searches… try again in N seconds". The CI fuzz job and the k6 load test raise the limit explicitly (`RATE_LIMIT_PER_MINUTE`).
- **Alternatives:** sliding window or token bucket (smoother, harder to explain in a demo); per-API-key limits (needs auth, out of scope A8).
- **Consequences:** Behind a reverse proxy every client shares the proxy's address until forwarded headers are configured (documented in `SearchRateLimiting`). With N replicas the limit is per replica. Verified in Docker: 65 searches ⇒ 60 × 200, 5 × 429, `Retry-After: 60`.

### D-029: Production refuses the published demo salts; compose defaults to a `Demo` environment
- **Date:** 2026-10-30 · **Status:** Accepted (Kumar: "refuse to start in Production") · Resolves R6
- **Context:** Compose fell back to the public `local-demo-salt-change-me` even with `ASPNETCORE_ENVIRONMENT=Production`; with a known salt the audit hashes can be reversed for a known VIN range.
- **Decision:** `ProductionSaltValidator` (an `IValidateOptions<PersistenceOptions>` in the Api) fails start-up in Production for the three salts published in this repo, or any salt containing `change-me`. Compose's default environment is `Demo`, not `Development` as first proposed: in .NET, Development also turns on the developer exception page, which would show stack traces to any client of the demo stack. Only Scalar is gated on Development, so Demo loses nothing else.
- **Alternatives:** warn only (a forgotten setting still ships); default compose to Development (exception page, see above); generate a random salt per start (audit hashes would no longer match across restarts).
- **Consequences:** `docker compose up` works with no `.env` as before; `.env.example` copied unchanged with `Production` fails fast with a message naming `VIN_HASH_SALT`. Verified in Docker both ways. Schema set-up and the snapshot warm-up run before options validation, so a refused start logs one extra warning first.

### D-030: Optional .NET Aspire AppHost for local runs
- **Date:** 2026-10-30 · **Status:** Accepted (Kumar: "the strongest backlog item")
- **Context:** Running the full stack meant Docker Compose plus three browser tabs for logs, traces and metrics.
- **Decision:** `aspire/DocViewer.AppHost` (Aspire 13.6.1: `Aspire.AppHost.Sdk`, `Aspire.Hosting.PostgreSQL`, `Aspire.Hosting.Redis`; newer 13.6.x were under two weeks old). Same ports as compose via the projects' launch profiles; Postgres `16-alpine` and Redis `7-alpine` pinned to match compose and Testcontainers (Aspire's defaults were 18.3 and 8.6); the E-018/E-019 client timeouts appended to Aspire's connection strings; API waits for both containers and both mocks; `/readyz` is its health check. The API pushes metrics over OTLP only when `OTEL_METRICS_EXPORTER=otlp` (the AppHost sets it; compose's Jaeger takes traces only). The `http` launch profile is first and sets `ASPIRE_ALLOW_UNSECURED_TRANSPORT`, so no dev certificate is needed.
- **Alternatives:** make Aspire the only way to run (reviewers without Docker or the Aspire tooling would lose the simple path); `AddServiceDefaults` project (would duplicate our telemetry setup); Serilog OTLP sink for structured logs in the dashboard (one more package; console logs already show there).
- **Consequences:** Docker Compose stays the documented primary path and the image build is unchanged. Compose and the AppHost cannot run at the same time (same ports). `DocViewer.AppHost.Tests` starts everything (~30 s) and fails if wiring breaks (checked by removing the Postgres connection string ⇒ `Degraded`).

### D-031: Opt-in keyset pagination on the documents endpoint
- **Date:** 2026-10-30 · **Status:** Accepted (Kumar) · Resolves Q1 · Refines D-021 (1)
- **Context:** Long histories (seed vehicle 8: 45 documents) came back as one list. Q1 asked whether paging is needed.
- **Decision:** Optional `?limit=1..100` and `?cursor=`. Without `limit` the response is unchanged apart from a new `nextCursor: null`. The cursor is opaque base64url of `v1|date|source|sourceId`, the last document's `DocumentPosition`; the D-012 order now lives in that one Domain type, used by both the merger and the pager. Filters apply before paging; `summary` counts the whole filtered result (identical to D-021 whenever `limit` is not used). Bad `limit`/`cursor` ⇒ 400 `invalid-page` before any source call. The Blazor UI keeps loading full lists, because its expiry alert counts the whole vehicle (D-025).
- **Alternatives:** offset paging (a document added between pages shifts every later page; the test `NewDocumentBetweenPages…` shows the keyset does not); always paginate (breaks existing clients).
- **Consequences:** Each page is its own search, usually a cache hit within 60 s. The fuzzer cannot know valid cursors, so `infra/schemathesis/schemathesis.toml` sends one valid cursor; parsing is covered by `PaginationTests`. The fuzzer also found the generated `limit` schema accepted `"-64"` (integer-or-string); `limit` is now integer-only, pinned by a contract test.

### D-032: Breaker state shared across replicas through Redis (full failure window)
- **Date:** 2026-10-31 · **Status:** Accepted (Kumar chose "share the full failure window" over "share only the open state") · Refines D-014 (rules unchanged) · Resolves R1
- **Context:** Polly's breaker is per process (D-007/D-014): with N replicas a dead source needed 3 failures on each replica, and every replica paid the timeouts.
- **Decision:** `SharedCircuitBreakerStrategy` (a custom Polly v8 strategy in place of `AddCircuitBreaker`) keeps D-014/D-027's rules (ratio 0.5, min 3 calls, 10 s window in ten buckets, 30 s break, one half-open probe, Polly's HTTP failure predicate) and keeps its state in `ICircuitStateStore`: in memory without Redis; with `ConnectionStrings:Redis`, Redis keys under one hash tag per source (`:open`, `:window` updated by one Lua script, `:probe` via `SET NX PX`). `FailoverCircuitStateStore` gives each Redis call 50 ms (`Resilience:SharedStateTimeoutMilliseconds`) and, on any failure, uses local state for 10 s (`SharedStateRetryAfterSeconds`). `/sources/health` reads the store. The connection is opened at start-up (E-022). Uses StackExchange.Redis, already present through the Redis cache package; tests add `Testcontainers.Redis` 4.16.0.
- **Alternatives:** share only "open" (Kumar's pick was the fuller one); Polly's breaker plus a manual-control sync loop (two sources of truth); per-call `INCR` keys per bucket (several keys per call, not cluster-friendly).
- **Consequences:** Up to three Redis round trips per source call (~1 ms each in Docker), all inside the 2 s source budget, so invariant 5 is unchanged. Replicas' clocks must roughly agree (buckets are epoch-based). A Redis outage makes the breaker per process again, never fails a call (invariant 10). Verified in Docker with two API containers: 3 failing searches on one ⇒ the other, which made no search, reports `open` and short-circuits.

### D-033: Background refresh of partial results through an in-process queue
- **Date:** 2026-10-31 · **Status:** Accepted (Kumar chose the in-process `Channel` over Redis Streams)
- **Context:** Partial results are never cached (D-006), so after an outage every user pays for the fan-out again, and stale snapshots age while nobody searches.
- **Decision:** A partial fan-out queues its VIN in `RefreshScheduler` (Application; `System.Threading.Channels`, de-duplicated per VIN, 1 000 VINs max, released after 5 s, then 15 s, 45 s). `BackgroundRefresher` (2 at a time, hosted by the Api's `RefreshWorker`) calls `AggregationService.RefreshAsync`: the same single-flight fan-out, no cache read, no audit row. A complete result is cached and snapshotted by the normal path; still partial after 3 attempts ⇒ give up. `docviewer.refresh.jobs{outcome}`. Partial/stale counters moved from the fan-out to the response, so a refresh is not counted as a response. Options `Refresh:*`, validated on start.
- **Alternatives:** Redis Streams (durable, shared; more code, and needs Redis to work); refreshing only the failed source and merging it with the other's snapshot (would label snapshot data as live).
- **Consequences:** No new package. Jobs are per replica and lost on restart (the next partial search queues the VIN again). The healthy source is called up to 3 more times per partial VIN. API contract tests push the delay to 600 s so their request counts hold; `BackgroundRefreshApiTests` runs the real worker. Verified in Docker: Service hanging ⇒ partial (`stale`); healed; 8 s later, with no search in between, the next search was a complete `HIT`.

### D-034: Optional OIDC bearer auth with per-document dealer scoping
- **Date:** 2026-10-31 · **Status:** Accepted (Kumar: "off by default, document scope") · Changes A8 · Adds 401/403 and `dealerCode` to the contract
- **Context:** No auth (A8): anyone who reached the API could read any vehicle's documents (R5 only slowed that down).
- **Decision:** `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.14 (new package, approved). `Auth:Enabled` (default false); when on: JWT bearer from `Auth:Authority` for `Auth:Audience`, claim names kept as issued, policy requires a `dealer_id` claim on all of `/api/v1` (401/403 problems), `refresh=true` needs role `admin`; health, metrics and OpenAPI stay anonymous; validated on start (Authority and Audience required). Documents carry `DealerCode` (Sales: vehicle `dealerCode`, the selling dealer; Service: record `dealer_code`, the workshop's dealer); seed data names one everywhere. `DealerScope` (Application) keeps only the caller's dealers' documents, fail closed for documents without a code, scopes `sources[].count` and recomputes duplicate/conflict marks on the visible set (`DocumentMerger.ClearCrossSourceMarks`), since `duplicateOf` names the other document. Applied per response after the cache, which stays unscoped and shared. OpenAPI: optional bearer scheme (`security: [{}, {Bearer: []}]`, the spec is generated with auth off).
- **Alternatives:** vehicle scope (Kumar picked document scope); on by default (needs an issuer for every demo and CI run); scope inside the adapters (would split the cache per dealer and hide duplicates the API should still know about).
- **Consequences:** A document the source sends without a dealer code is invisible to every scoped caller, including old snapshots taken before this change (they refresh within a search). The demo UI does not sign in. Rate limits stay per IP. Verified by 16 API tests through the real JwtBearer handler with locally signed RS256 tokens (no network); not verified against a real identity provider.

<!-- Add new decisions above this line, continuing from D-035. -->

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
- **Date:** 2026-10-28 · **Task:** D5.4 · **Severity:** medium · **Status:** resolved 2026-10-30 (machine contention)
- **Symptom:** `search_hit_ms p(95)=132.51ms` (threshold 20 ms) over 2 min at 50 VUs; median 5.5 ms, max 1.8 s. Single user: p95 7.8 ms. Miss p95 251 ms (target 300 ms) and 0 % errors passed.
- **Root cause:** Not proven. Evidence against the API code: thread-pool queue length 0 throughout, GC pauses ~8 ms per second. Evidence for the machine: Docker's 6-CPU VM was shared by an unrelated `minikube` container at ~200 % CPU, k6 itself (17 %), the API (60 %) and Postgres (20 %, one audit row per search, cache hits included). Thread count ramped 5 → 19 at the start, which may explain early outliers.
- **Fix:** None. The threshold was **not** relaxed (AGENTS.md §6).
- **Next:** Re-run with minikube stopped. If p95 is still > 20 ms, profile the hit path (audit write, JSON serialisation, logging) and consider `ThreadPool.SetMinThreads` only with measurements.
- **Resolution (2026-10-30):** Kumar re-ran it with minikube stopped: hit p95 **5.52 ms**, miss p95 8.21 ms, 0 of 11 970 requests failed, breaker-open p95 6.85 ms. The hypothesis held: the code was not the cause. A first re-run attempt was spoiled because the agent's instructions started k6 while the API was still restarting ("connection refused"); the script now waits for `/readyz` and the README uses `--wait`. The threshold was never relaxed.

### E-015: SQLite and PostgreSQL store the audit JSON differently (found by D5.5)
- **Date:** 2026-10-28 · **Task:** D5.5 · **Severity:** low (test-only)
- **Symptom:** On PostgreSQL, `search_audit.source_status` reads back as `{"Sales": "Ok", "Service": "Timeout"}`; the SQLite suite asserts the exact text `"Service":"Timeout"`, which fails on Postgres (checked with a temporary assertion, then removed).
- **Root cause:** `jsonb` stores parsed JSON and re-serialises it on read (spacing, and key order is not guaranteed); SQLite `TEXT` returns the bytes written.
- **Fix:** Postgres tests compare the parsed JSON. Production code only writes this column and never compares its text, so behaviour is unaffected. Everything else matched: migrations apply with nothing pending, both JSON columns are `jsonb`, `timestamptz` round-trips the exact `DateTimeOffset`, array order and non-ASCII text survive, upsert keeps one row, the 24 h age limit is inclusive.
- **Prevention:** `PostgresPersistenceTests` (6 tests, Testcontainers, `postgres:16-alpine`) run in CI; skipped with a reason where Docker is missing. Rule: assert on parsed JSON, never on JSON text, for anything stored in `jsonb`.

### E-016: OpenAPI schema required `sources[].error`, which healthy responses leave out
- **Date:** 2026-10-28 · **Task:** contract fuzzing (D-026) · **Severity:** medium (contract)
- **Symptom:** Schemathesis: "Response violates schema — 'error' is a required property" on an ordinary 200 (`{"name":"SALES","status":"ok",…,"asOf":null}`).
- **Root cause:** `SourceStatusResponse.Error` is serialised with `JsonIgnoreCondition.WhenWritingNull` (intended, DESIGN §7.2), but the OpenAPI generator marks every positional-record parameter as required and ignores that condition. A client generated from the spec could reject every healthy response. The 238+ existing tests never compared responses with the schema.
- **Fix:** `OptionalWhenNullSchemaTransformer` (generic: any `WhenWritingNull`/`WhenWritingDefault` property). Re-run: 1 390 generated requests, 0 failures.
- **Prevention:** `OpenApiContractTests` and the `contract-fuzz` CI job.

### E-017: The UI container logged raw VINs from deep links
- **Date:** 2026-10-28 · **Task:** D5.6 security review · **Severity:** high (privacy, invariant 7)
- **Symptom:** `docker logs docviewer-web-1` contained `"GET /?vin=MA3EWB81S00100007 HTTP/1.1"`.
- **Root cause:** nginx's default `combined` log format records the whole request line, query string included, and the UI's deep links carry the VIN there (D4.2). Invariant 7's tests cover the API's logs, spans and metrics, but not the new nginx container added in D4.6.
- **Fix:** `log_format no_query` (method, `$uri`, protocol, status, bytes; never `$request` or `$args`) in `docker/nginx-web.conf`. Re-checked: after searches with a valid VIN, an invalid one and a deep link, 0 raw-VIN lines across all six containers' logs and `/metrics`.
- **Prevention:** The D5.6 check (grep every container's logs after real requests) is the rule for any new container. Not automated yet: a compose-level privacy check in CI is a good next step.

### E-018: A slow or stopped Postgres pushes a fallback search to ~12 s (breaks invariant 5)
- **Date:** 2026-10-29 · **Task:** design-risk review (Kumar) · **Severity:** high · **Status:** fixed 2026-10-29
- **Symptom:** Service hanging + Postgres stopped: `?refresh=true` searches took 11.8 s and 11.5 s (HTTP 200). With Postgres up, one fallback took 3.45 s, also over ~2.2 s.
- **Root cause:** Only source calls have a budget (D-005). The stale fallback reads the snapshot *after* the source has used its 2 s, with `CancellationToken.None` (`AggregationService.cs:173`), and no database timeout is configured (Npgsql defaults: 15 s connect, 30 s command). Invariant 5's tests (`SourcePipelineTests.cs:80`, `:96`) cover the source pipeline only; the aggregation fakes answer instantly.
- **Fix:** `LatencyBudgetOptions.SnapshotReadTimeoutMs` (300 ms, validated 10–2000). `AggregationService.WithFallbackAsync` stops waiting with `WaitAsync(budget, TimeProvider)` and keeps the original failure; logs `snapshot.read_timed_out`. Compose adds `Timeout=3;Command Timeout=5` to the Postgres connection string. Re-measured in Docker with Postgres stopped and Service hanging: 1.84 s (was 11.8 s); with Postgres up the stale fallback still works (1.6–1.9 s).
- **Prevention:** `Search_SnapshotReadHangs_GivesUpAfterItsBudget_AndReportsTheSourceAsFailed` (invariant 5; red before the fix), options validation tests.

### E-019: A stopped Redis makes every search take ~11–12 s
- **Date:** 2026-10-29 · **Task:** design-risk review (Kumar) · **Severity:** high · **Status:** fixed 2026-10-30
- **Symptom:** Redis stopped: 4 normal searches took 11.0–12.0 s each (HTTP 200, `X-Cache: MISS`); 8 `cache.failed` warnings. With Redis up: 0.09–0.23 s.
- **Root cause:** The cache read (`AggregationService.cs:48`) and write (`:133`, awaited inside the fan-out) wait for StackExchange.Redis to give up (~5–6 s each; client defaults, nothing configured). `DistributedDocumentCache` turns the failure into a miss, so invariant 10 holds, but invariant 5 does not.
- **Fix:** `LatencyBudgetOptions.CacheTimeoutMs` (150 ms) on each cache read and write in `AggregationService`; a write that runs out of time finishes on its own. The write stayed inline (not moved to the background as first proposed), so a repeat search right after a complete one is still a `HIT`. Budgeted calls start on the thread pool (`OffThread`): the first Redis call connected *synchronously* for ~1.5 s before returning its task, which `WaitAsync` alone never saw. Compose: Redis `connectTimeout=2000,syncTimeout=1000,asyncTimeout=1000,abortConnect=false`. Re-measured in Docker: Redis stopped 0.32–0.63 s (was 11–12 s); first search on a fresh container 1.81 s (was 3.39 s).
- **Prevention:** `Search_CacheHangs_ReadAndWriteEachGiveUpAfterTheirBudget` (red before the fix; fails again with the budget removed). See E-020 for two tests that had to change.

### E-020: The budget fixes exposed two timing-dependent tests
- **Date:** 2026-10-30 · **Task:** E-018/E-019 fixes · **Severity:** medium (tests)
- **Symptom:** (1) `Search_ConcurrentMissesForOneVin_ShareOneFanOut` saw 2 fan-outs instead of 1. (2) `SourceFailsAfterEarlierSuccess_ServesLabelledStaleSnapshot` failed in ~25 % of runs with `circuit_open` instead of `http_500`. On the commit before the fixes (`c654c72`, separate worktree): 0 of 10 failed.
- **Root cause:** (1) The fake cache used to answer synchronously, so all five searches reached the shared fan-out before the test opened the gate; cache calls now hop to the thread pool (as real Redis calls always did). Single-flight (D-020) is unchanged. (2) The test waited for the background snapshot save by *searching*; each failing search feeds the breaker. A diagnostic run showed the Service snapshot committed ~0.7 s late behind other SQLite writes (one hit the documented insert race), so four failed polls opened the breaker first. Separately, the first snapshot read after start-up exceeded the new 300 ms budget in 2 of 10 runs (cold EF query + connection): a real side effect, not only a test problem.
- **Fix:** (1) The test opens the gate only after all five searches have recorded their cache miss (recorded immediately before joining the fan-out). (2) The test waits on `ISnapshotRepository` directly, then makes one failing search. Product: `StartupTasks.WarmUpSnapshotReadAsync` runs one lookup at start-up (best effort, 5 s cap); timeouts in the isolated runs went from 2 to 0. Both tests still fail when the behaviour they guard is removed (checked by mutation). Full suite 336/336 twice; the two tests 0/40 and 0/20.
- **Prevention:** A test must not wait for background work by repeating the request it is testing when that request has side effects (breaker, cache).
- **Update 2026-10-30:** CI failed on `f496b6e`: a third test with the same assumption, `Search_CallerCancels_StopsWaitingWithoutCancellingSharedFanOut` (2 fan-outs instead of 1), passed every local run but not on the faster CI runner. The agent had repeated only the tests it had changed, not their siblings using the same gate pattern. Same fix (wait for both cache misses before cancelling); Application suite 0/60 locally; still fails with single-flight removed. Rule: after a timing change, re-run *every* test that uses the pattern, and let CI have the last word.

### E-021: Aspire 13.6.1 pulled a MessagePack version with a known vulnerability
- **Date:** 2026-10-30 · **Task:** Aspire AppHost (D-030) · **Severity:** medium (supply chain)
- **Symptom:** `dotnet restore` failed: `NU1902: Package 'MessagePack' 2.5.302 has a known moderate severity vulnerability, GHSA-qhrr-8q5h-9q3h` (warnings are errors, D-017).
- **Root cause:** Aspire.Hosting 13.6.1 depends on MessagePack 2.5.302; the advisory (nested arrays can exceed buffer limits, published 2026-10-15) is fixed in 2.5.303. Aspire 13.6.4 depends on 2.5.307 but was only 6 days old.
- **Fix:** Pinned `MessagePack` 2.5.307 (released 2026-10-03, the version newer Aspire ships) as a direct dependency of the AppHost, with a comment to drop it when Aspire is upgraded. Not suppressed.
- **Prevention:** NuGet audit as an error did its job. Rule: never suppress NU190x; pin the patched transitive version instead.

### E-022: The first shared-breaker call missed its 50 ms budget, so a replica silently stopped sharing
- **Date:** 2026-10-31 · **Task:** shared breaker (D-032) · **Severity:** medium
- **Symptom:** Two API containers, Sales failing: three searches on one replica left one failure in Redis, and the other replica still reported `closed`. No test had failed.
- **Root cause:** The first Redis call opens the connection (~0.6 s in Docker). The 50 ms budget expired, the store fell back to local state for 10 s, and the abandoned call landed later as the one failure. Same cold-start shape as E-019/E-020; tests used a 1 s budget on purpose (E-001), so they never saw it.
- **Fix:** `WarmUpSharedCircuitStateAsync` at start-up (best effort, 5 s cap, logs `startup.shared_circuit_warmup_failed`). Re-measured: 3 failures in Redis, second replica `open` and short-circuiting, no fallback warnings.
- **Prevention:** The two-container check is in the README's verification notes for D-032. Rule (again): a budget that is tight enough to matter must be checked on the real stack, cold.

### E-023: `SecondCall_IsServedFromCache` failed once on CI (MISS instead of HIT), passed on re-run
- **Date:** 2026-10-31 · **Task:** shared breaker commit `6ed058a` · **Severity:** low (not reproduced)
- **Symptom:** CI's test job failed on that one assertion; re-running the failed job passed. Locally: 10/10 on a clean worktree of the commit and 70/70 project runs with two full suites running at once.
- **Root cause:** Unproven. Most likely the E-019 cache budget: on a busy runner the first cache write in a new test host (JIT of the serializer and cache path) took over 150 ms, so the second request arrived before the abandoned write finished. Nothing in D-032 touches the cache path.
- **Fix:** None to the product or the budget without evidence. The assertion now prints the API's own cache and search log lines, so a recurrence names its cause.
- **Prevention:** Agent's own mistakes on the way, logged honestly: two local "reproduction" runs used stale binaries because the build had failed (untracked files from the next task; then a diagnostic edit that did not compile) — the E-002 trap. Runs are now chained on `Build succeeded`.

<!-- Add new errors above this line, continuing from E-024. -->

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
| A8 | Auth optional, off by default | Changed 2026-10-31 (D-034) |
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

### AI-012: Agent treated "generated, so it cannot drift" as "correct"
- **Date / phase:** 2026-10-28 · testing
- **AI output:** Since D3.5 the docs said the spec "is a build output so it never drifts" (DESIGN §14), and the agent treated `docs/openapi.json` as the verified contract.
- **Problem:** Never drifting from the code is not the same as describing the code correctly. The generated schema required a field the API omits (E-016) and did not state the VIN format at all.
- **How it was caught:** Contract fuzzing (Kumar's backlog choice) on its first and second runs.
- **Correction:** D-026: transformers fix the spec, tests pin the findings, fuzzing runs in CI. Lesson: check generated artefacts against real behaviour, not only against their generator.

### AI-013: Review of the agent's own Day 4–5 code found real bugs, and one wrong rule
- **Date / phase:** 2026-10-28 · review (D5.6)
- **AI output:** Code from the previous two sessions, all tests green: the Blazor client, health panel, mock mode, healthcheck probe, k6 script, Postgres test skip logic, and an AGENTS.md rule about Razor quotes.
- **Problem:** A `/code-review` pass found, and the agent then confirmed in the code: expiry day counts from the browser's time zone while the server uses IST; a superseded search could overwrite a newer one; an `HttpClient` timeout escaped the "never throws" client; the fault dropdown could show a mode the mock did not have; mock mode could not show `circuit_open`, `half_open` or cache `HIT` (breaking the agent's own AGENTS rule); the k6 parser silently mis-timed phases; the probe built port 80 from an empty variable. The agent's AGENTS rule "no nested quotes in Razor attributes" was over-broad: only lambdas without `@( )` break. The agent's own security pass then found E-017.
- **How it was caught:** Independent review of the diff since the first submission, then each finding checked against the code before acting; privacy checked on real container output.
- **Correction:** All fixed with tests (331 total) except the healthcheck target, which is Kumar's decision (Q4). Lesson: green tests written by the same agent are not a review.

### AI-014: Agent stated guarantees that held only for the parts it had tested
- **Date / phase:** 2026-10-29 · design review (prompted by Kumar's risk list)
- **AI output:** (a) Invariant 5, "total request time is bounded by ~2.2 s regardless of source behaviour", marked as tested. (b) D-014: "with low traffic the min-throughput of 5 makes it behave almost the same" as "open after 5 consecutive failures".
- **Problem:** (a) The bound held for the sources, not for the cache and snapshot calls on the same path: Redis or Postgres down made searches take ~12 s (E-018, E-019). (b) At under 5 calls per 10 s the breaker never opens: 8 timeouts in a row at ~3.6 s spacing left it `closed`, so every user paid ~1.5 s.
- **How it was caught:** Kumar asked for each risk to be checked against the code; the agent then measured each one on the running stack instead of reasoning about it.
- **Correction:** E-018, E-019, R4 recorded; fixes proposed, waiting for Kumar. Lesson: a latency guarantee must be tested end to end, with every dependency on the path made slow.

### AI-015: Agent reported a test as passing with a check that could not see failures
- **Date / phase:** 2026-10-30 · testing (E-019)
- **AI output:** "failed: 0 / 10" for `SourceFailsAfterEarlierSuccess…`, run with `--logger "console;verbosity=normal"` and a grep for `Failed!`.
- **Problem:** That logger setting prints "Test Run Failed." instead of `Failed!`, so the check reported 0 whatever happened. The real rate was ~25 %. The agent's first fix (budget with `WaitAsync` only) also missed work the Redis client does synchronously before returning a task.
- **How it was caught:** A second check printed the actual per-run result lines; the cold-start request was read from the API's own log, not inferred.
- **Correction:** E-019 and E-020. Lesson: before trusting "0 failures", make the check fail once on purpose; the mutation runs in this fix do exactly that.

### AI-016: Agent updated the contract docs but not the design sections, and missed a budget arithmetic
- **Date / phase:** 2026-10-30 · docs
- **AI output:** For D-024…D-031 the agent updated SYSTEMS.md, the README and the API parts of DESIGN.md (§7, §10 breaker row, §16, §17), and reported the work as documented.
- **Problem:** DESIGN's data flow (§6.2), resilience (§10), caching (§11), observability (§12) and security (§13) sections still described the old behaviour: no dependency budgets, snapshots saved only when every source succeeded (never true in the code), fallback after merge, a `SemaphoreSlim` single-flight (replaced in D-020), "min 5 calls" in the breaker diagram, rate limiting as "future", pagination as future work. Adding the budgets also pushed the theoretical worst case to ~2.45 s, above invariant 5's ~2.2 s, which the agent had not raised.
- **How it was caught:** Kumar asked whether DESIGN and SYSTEMS were updated; the agent searched DESIGN for every recent decision ID and change instead of answering from memory.
- **Correction:** DESIGN 1.1 rewrites those sections; the worst case is stated as ~2.45 s and raised as Q5. Lesson: after a change, search the design for every statement it makes false, not only the sections named after the change.

### AI-017: Agent assumed a missing Redis key cannot parse as a number, and wrote two junk lines it then had to catch
- **Date / phase:** 2026-10-31 · implementation (D-032)
- **AI output:** (a) `value.TryParse(out long untilMs) ? … : null` for the open-until key. (b) A `Rejected` helper built around a meaningless `Outcome.FromResult(null!) is var _` expression, and a test line calling a non-existent `.ToAsync()`. (c) A fake-time test that advanced the clock in a loop until a task finished.
- **Problem:** (a) `RedisValue.Null.TryParse(out long)` succeeds with 0, so every closed circuit read as "open until 1970" = half-open, and the first failure opened it: the shared breaker would have opened on one failure instead of three, on every replica. (b) Noise that compiled (or would have). (c) The loop outran the continuation and moved fake time 10 s, so the test failed for the wrong reason.
- **How it was caught:** (a) the real-Redis Testcontainers test failed (the in-memory unit tests could not see it); a mutation run removing the fix makes two Redis tests fail. (b) the agent's own read of the diff before building. (c) the failure output named 10 s, not 50 ms.
- **Correction:** `!value.IsNull &&` with a comment; helper rewritten; one `Advance` past the budget. Lesson: test a store against the real store; library "TryParse" semantics on null differ.

### AI-018: Auth and refresh tests written from memory, and one with a timing race
- **Date / phase:** 2026-10-31 · testing (D-033, D-034)
- **AI output:** (a) An auth test reading `sources[].documentCount` (the field is `count`). (b) A test helper that JSON-parsed every body, including `/healthz` (`Healthy`) and `/metrics`. (c) A stale source built with `SourceResult.Failed(…, Stale)`, which the type forbids. (d) `BackgroundRefreshApiTests` "healed" the mock with `Reset()` 1 s before the first refresh; under full parallel load the refresh could run first and `Reset` erased the evidence. It passed 6/6 alone and failed once in a full run. (e) A first version of that test polled with searches, which would have passed with no worker at all.
- **Problem:** (a)–(c) failed for reasons unrelated to the feature; (d) a flaky test; (e) a test that could not fail.
- **How it was caught:** each failure message read before any change; (d) by the full concurrent run; (e) by the agent's own review before running it, then a mutation (worker delayed 600 s) that must fail.
- **Correction:** field names checked against the DTO; plain-text bodies not parsed; stale built as the aggregator does; the refresh test uses a WireMock scenario (500 then 200, no heal step) and waits on the cache itself. 6 concurrent full runs green afterwards. Lesson (again, AI-009): check names against the code, never from memory.

<!-- Add new AI corrections above this line, continuing from AI-019. -->

---

## 5. Open Questions and Risks

| # | Item | Owner | Status |
|---|---|---|---|
| Q1 | Do we need pagination for very long histories, or is a single list fine for the demo? | Kumar | Closed 2026-10-30 — opt-in keyset pagination (D-031) |
| Q2 | Should stale fallback be disabled by config for audit-sensitive dealers? | Kumar | Open |
| Q3 | `/readyz` returns 503 when the database is down (DESIGN §7.1), but searches still work without it (invariant 10). Should readiness report DB loss as *degraded* (200) so a DB blip does not pull every instance from the load balancer? | Kumar | Closed 2026-10-28 — *degraded* (200), D-024 |
| Q5 | Invariant 5 promises ~2.2 s, but with the E-018/E-019 budgets the theoretical worst case is cache read 0.15 s + sources 2.0 s + snapshot read 0.3 s ≈ 2.45 s (measured worst so far: 1.84 s). Restate the invariant as ~2.5 s (new decision), or shrink the budgets (e.g. 100 ms each, more cold misses)? | Kumar | Open (2026-10-30) |
| Q4 | The Docker healthcheck probes `/readyz`, which after D-024 answers 200 whenever the process is up, so it now detects nothing more than `/healthz` while opening a database connection every 10 s. Switch the probe to `/healthz`? (D5.6 review) | Kumar | Open |
| R3 | Raw VINs are stored at rest in the Redis cache key (`docs:v1:{VIN}`) and `source_snapshot.vin`; both need the VIN for lookup. D-011 hashes only the audit table. | — | Accepted risk, recorded in D5.6 |
| R1 | Per-process breaker state across replicas (D-007) | Kumar | Closed 2026-10-31 — shared through Redis (D-032); per process again only while Redis is down |
| R2 | Duplicate heuristic false positives (D-009) | — | Accepted risk, tested. 2026-10-29 review: the `(type, date)` fallback (`DocumentMerger.cs:63-64`) flags two **different** invoices or warranties from different sources issued the same day without a reference as `POSSIBLE_DUPLICATE` (flag only, nothing hidden). Matching by reference is exact after the `SV-{category}-` prefix is stripped (`ServiceAdapter.cs:112-117`), never a suffix match. |
| R4 | The breaker needed ≥ 5 calls in 10 s, so at low traffic it never opened and each search to a dead source cost ~1.5 s (measured 2026-10-29: 8 timeouts, circuit stayed `closed`). | Kumar | Closed 2026-10-30 — min throughput 3 (D-027); opens by the 3rd search in the same experiment |
| R5 | No rate limiting and no auth (A8): anyone who can reach the API could try VINs one after another and read every match. | Kumar | Closed 2026-10-30 — 60 searches/min per IP, 429 (D-028). Auth is still out of scope (A8), so a slow enumerator is only slowed, not stopped. 2026-10-31: optional dealer-scoped auth (D-034) stops it when turned on. |
| R6 | Compose fell back to the public demo salt even in Production, so a forgotten setting made the audit hashes guessable. | Kumar | Closed 2026-10-30 — Production refuses published salts; compose defaults to `Demo` (D-029) |
| R7 | `partial` was explained only in the operation description; the `partial` and `sources[].status` fields had no description in `docs/openapi.json`. | — | Closed 2026-10-30 — descriptions on `partial`, `sources[].status` and `asOf`; pinned by `OpenApiContractTests` |
| R8 | Single-flight is per process (D-020). With Redis down, 20 requests for one VIN at 4/s caused 3 fan-outs on one replica (1 with Redis up); that was low only because the slow cache write kept the fan-out alive ~5.5 s. Since E-019 the write gives up after 150 ms, so with Redis down a popular VIN now fans out about once per non-overlapping request per replica (N replicas ⇒ N×). Each fan-out is bounded by the breaker and D-005. A local in-memory fallback cache would cap it; not built. | — | Accepted risk, updated 2026-10-30 |

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
