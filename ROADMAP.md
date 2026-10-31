# Roadmap — Unified Document Viewer (.NET 10)

> **Status legend:** `[ ]` not started · `[~]` partial / in progress · `[x]` complete
>
> **Rules** (full version in [AGENTS.md](AGENTS.md)):
> - Mark a task `[~]` when you start it and `[x]` only when **all** of its acceptance criteria pass, including tests.
> - Work top to bottom within a day unless a dependency says otherwise.
> - Anything learned, decided or broken along the way goes in [SYSTEMS.md](SYSTEMS.md).
> - Update the **Current Focus** and **Progress Summary** blocks at the end of every session.

---

## Current Focus

| Field | Value |
|---|---|
| Active day | Day 3 |
| Next task | **Kumar:** (1) decide Q4 (healthcheck on `/healthz`?) and Q5 (restate invariant 5 as ~2.5 s?), (2) OK to tag `v1.1` (closes D5.6), (3) browser pass at :5173 (D4.6), (4) record the video from `docs/video-script.md` (D5.7). Every backlog item is now done (2026-10-31) |
| Blockers | None |
| Last updated | 2026-10-31 (backlog: shared breaker, background refresh, auth, Helm) |

## Progress Summary

| Phase | Tasks | Done | Status |
|---|---|---|---|
| Day 0 — Planning | 6 | 6 | `[x]` |
| Day 1 — Foundations: solution, domain, mocks, adapters | 7 | 7 | `[x]` |
| Day 2 — Aggregation, resilience, cache, persistence, API | 7 | 7 | `[x]` |
| Day 3 — Observability, packaging, docs (**submission-ready**) | 7 | 7 | `[x]` |
| Day 4 — Frontend demo client (Blazor, D-025) | 6 | 5 (+D4.6 partial) | `[~]` |
| Day 5 — Smart features, hardening, video | 7 | 5 (+D5.6, D5.7 partial) | `[~]` |

**Plan shape:** Days 1–3 build the complete, gradable submission (core backend, tests, docs, Docker). Days 4–5 are extra development that make it stand out. If time runs short, the submission at the end of Day 3 must already be complete.

---

## Day 0 — Planning `[x]`

- [x] **D0.1** Choose scenario (D — Unified Document Viewer) and layer (backend, fully implemented).
- [x] **D0.2** Review the first-pass AI plan against the brief; log the gaps (SYSTEMS.md §4).
- [x] **D0.3** Write [DESIGN.md](DESIGN.md) (full system design document).
- [x] **D0.4** Write [SYSTEMS.md](SYSTEMS.md) with initial decisions.
- [x] **D0.5** Write [AGENTS.md](AGENTS.md) and this roadmap.
- [x] **D0.6** Re-platform the plan from Python to .NET 10 / C# (D-013 … D-017, AI-006); discard the Python scaffold.

---

## Day 1 — Foundations `[x]`

Goal: solution structure, pure domain logic, both mock systems and both adapters, all tested. No aggregation yet.

- [x] **D1.1 Solution bootstrap**
  - Install the .NET 10 SDK. `global.json` (SDK 10.0, `rollForward: latestFeature`).
  - `Directory.Build.props` (net10.0, nullable, implicit usings, warnings as errors, `AnalysisLevel latest-recommended`) and `Directory.Packages.props` (Central Package Management) — D-017.
  - `DocViewer.slnx` with projects per DESIGN.md §17: `src/DocViewer.{Domain,Application,Infrastructure,Api}`, `mocks/{Mocks.Common,SalesSystem.Mock,ServiceSystem.Mock}`, `tests/DocViewer.{Domain,Application,Infrastructure,Api}.Tests`, `tests/Mocks.Tests`.
  - Project references follow the dependency rule (SYSTEMS.md invariant 11).
  - `.gitignore` for .NET (`bin/`, `obj/`, `TestResults/`), `.editorconfig` C# rules.
  - ✅ `dotnet build` has 0 warnings; `dotnet test` runs (empty); `dotnet format --verify-no-changes` clean. First commit made (with approval).

- [x] **D1.2 Options and time**
  - Options classes with data-annotation validation and `ValidateOnStart`: `SourcesOptions` (base URLs, max connections), `ResilienceOptions` (D-005/D-014 values), `CacheOptions`, `PersistenceOptions`, `BusinessRulesOptions` (expiry warning days, timezone).
  - `TimeProvider.System` registered; `DealershipCalendar` gives "today" in the dealership timezone from `TimeProvider`.
  - ✅ Tests: defaults match DESIGN; invalid values fail validation; `Today` crosses midnight correctly in IST using `FakeTimeProvider`.

- [x] **D1.3 Domain models and VIN**
  - `DocViewer.Domain`: `Document`, `DocumentType`, `SourceSystem`, `DocumentStatus`, `DocumentFlag` records/enums; `Vin` value object with `TryParse(string?, out Vin, out string error)` and `Masked`.
  - Application contracts: `SourceResult`, `SourceStatus`, `AggregatedDocuments`.
  - ✅ Theory tests: valid; lower case normalised; whitespace trimmed; 16/18 chars; I/O/Q at several positions; symbols; null/empty; masking format `MA3**********0001`.

- [x] **D1.4 Domain rules: status, sort, dedupe/conflict**
  - `DocumentStatusCalculator` (DESIGN §9.2) and `DocumentMerger` (sort §9.3, duplicate/conflict §9.4).
  - ✅ Tests: expiry boundaries (yesterday, today, today+30, today+31, null); sort tie-breaks; same-source match not flagged; cross-source same dates ⇒ `PossibleDuplicate`; differing expiry ⇒ `Conflict`; ≥ 95 % line coverage on Domain.

- [x] **D1.5 Mock Sales API**
  - `Mocks.Common`: fault middleware + `POST/GET /__admin/fault` (modes `none|slow|timeout|error|flaky|malformed`).
  - `SalesSystem.Mock` on :8081 with `seed/sales.json` per DESIGN Appendix A/B (camelCase, ISO dates, 404 for unknown VIN).
  - ✅ `Mocks.Tests` (WebApplicationFactory): each seed scenario; each fault mode.

- [x] **D1.6 Mock Service API**
  - `ServiceSystem.Mock` on :8082 with `seed/service.json` (snake_case, `dd/MM/yyyy`, `records` wrapper, empty list for unknown VIN, one malformed record for VIN #9).
  - ✅ Same test coverage as D1.5.

- [x] **D1.7 Source adapters**
  - `ISourceAdapter` port in Application; `SalesAdapter`, `ServiceAdapter` in `Infrastructure/Sources/` using typed `HttpClient`s and source-specific DTOs.
  - Type mapping per DESIGN §8.2; unknown types → `Other` + log; malformed records skipped and counted; all failures mapped to `SourceResult` (never throw).
  - ✅ WireMock.Net tests: field mapping, `dd/MM/yyyy` parsing, Sales 404 ⇒ empty ok, malformed record skipped, invalid JSON ⇒ `error`, 500 ⇒ `error`.

**Day 1 exit check:** `dotnet build` clean · `dotnet test` green · both mocks run · SYSTEMS.md updated · commit `day1: domain, mocks, adapters`.

---

## Day 2 — Aggregation core `[x]`

Goal: the endpoint works end to end with resilience, cache and persistence.

- [x] **D2.1 Resilience pipelines**
  - `AddResilienceHandler` per source client exactly as D-014 (total 2 s → retry ×1 on connect/502/503/504 → breaker → attempt 1.5 s); custom `ShouldHandle` so attempt timeouts are not retried; `CircuitBreakerStateProvider` per source exposed through an `ISourceHealth` service.
  - Adapters map `TimeoutRejectedException` ⇒ `timeout`, `BrokenCircuitException` ⇒ `circuit_open`.
  - ✅ WireMock tests: 3 s delay ⇒ `timeout` within ~2.1 s and **no** retry; 503 then 200 ⇒ `ok` after one retry; repeated failures open the breaker ⇒ `circuit_open` without an HTTP call; breaker half-opens after break duration.

- [x] **D2.2 Cache**
  - `IDocumentCache` (Application) over `IDistributedCache` (Infrastructure); memory by default, Redis when `ConnectionStrings:Redis` is set (D-015). Cache errors are treated as misses and logged.
  - ✅ Tests: round-trip serialisation; TTL honoured; a throwing `IDistributedCache` ⇒ miss, no exception.

- [x] **D2.3 Persistence**
  - EF Core `DocViewerDbContext` with `SourceSnapshot` and `SearchAudit` (DESIGN §8.3); JSON columns via value converter (`jsonb` on Postgres, `TEXT` on SQLite); initial migration; `ISnapshotRepository` + `ISearchAuditWriter`. VIN hashed with salted SHA-256 for audit.
  - ✅ SQLite in-memory tests: upsert overwrites; snapshot older than 24 h ignored; DB failure swallowed and logged.

- [x] **D2.4 AggregationService**
  - Orchestration per DESIGN §6.2: cache → `Task.WhenAll` → merge/enrich → stale fallback → persist → result; per-VIN single-flight.
  - ✅ Tests with fakes: both ok; Sales down ⇒ partial; Service timeout ⇒ partial; both down + snapshot ⇒ stale; both down no snapshot ⇒ `AllSourcesFailed`; partial **not cached**; complete cached; adapters run concurrently (total ≈ max, not sum); concurrent misses for one VIN ⇒ one fan-out.

- [x] **D2.5 API endpoints**
  - Minimal API group `/api/v1`: documents (filters `source|type|status|refresh`), sources/health; `/healthz`, `/readyz`; `ProblemDetails` for 400/503/500 with `traceId`; `X-Cache`, `X-Trace-Id` headers; `Retry-After` on 503.
  - ✅ `WebApplicationFactory` + WireMock tests: 400 messages, 200 complete, 200 partial, 503, empty 200, cache HIT on second call, filters.

- [x] **D2.6 Composition root**
  - `Program.cs` with `AddApplication()`, `AddInfrastructure(config)`, CORS for the UI origin, migrations applied in Development; `http/docviewer.http` requests.
  - ✅ Run API + both mocks ⇒ every cURL example in DESIGN §7.4 behaves as documented.

- [x] **D2.7 Coverage gate**
  - coverlet collection; ≥ 85 % line coverage on Domain + Application (report via ReportGenerator).
  - ✅ Gate passes; numbers recorded in the Session Log.

**Day 2 exit check:** every row of DESIGN §6.3 is covered by an automated test · commit `day2: aggregation, resilience, cache, persistence`.

---

## Day 3 — Observability, packaging, documentation `[x]`

Goal: **submission-ready.** Everything the brief requires exists and runs from a clean clone.

- [x] **D3.1 Structured logging** — Serilog compact JSON, request logging, `TraceId` enrichment, `[LoggerMessage]` methods for search and per-source events (DESIGN §12.1).
  - ✅ Test with an in-memory sink: raw VIN never logged; trace id on every event.

- [x] **D3.2 Metrics** — `DocViewerMetrics` (Meter "DocViewer") with all custom instruments in DESIGN §12.2; OpenTelemetry metrics incl. ASP.NET Core, HttpClient, runtime, Polly; Prometheus `/metrics`.
  - ✅ Test with `MetricCollector<T>`: hit/miss/partial/stale counters increment; no VIN tags.

- [x] **D3.3 Tracing** — OpenTelemetry tracing (ASP.NET Core, HttpClient, EF Core) + `ActivitySource("DocViewer")` spans; OTLP exporter; `traceId` in body and `X-Trace-Id`. — _verified via Jaeger API (docs/diagrams/trace-example.md) and in the Jaeger UI by Kumar on 2026-10-30: `docs/diagrams/jaeger-trace.png` shows `source.sales` and `source.service` overlapping in one trace (also saved: Grafana and UI screenshots)_
  - ✅ Manual check in Jaeger / Aspire dashboard: one trace shows two parallel source spans (screenshot in `docs/diagrams/`).

- [x] **D3.4 Docker Compose** — multi-stage Dockerfiles (SDK build → chiselled ASP.NET runtime) for API and mocks; `docker-compose.yml` with api, sales-mock, service-mock, postgres, redis, healthchecks; `obs` profile with Prometheus, Grafana (provisioned dashboard), Jaeger.
  - ✅ Clean clone → `docker compose up --build` → cURL demo works.

- [x] **D3.5 OpenAPI** — `Microsoft.AspNetCore.OpenApi` + build-time generation to `docs/openapi.json`; examples and descriptions on endpoints; Scalar UI in Development.
  - ✅ Spec committed; CI fails if `git diff --exit-code docs/openapi.json` shows drift after build.

- [x] **D3.6 CI** — `.github/workflows/ci.yml`: setup-dotnet 10, restore, build (warnings as errors), `dotnet format --verify-no-changes`, test + coverage, OpenAPI drift.
  - ✅ Green run on GitHub.

- [x] **D3.7 README and design doc finalisation** — README: overview, prerequisites, quick start (Docker and `dotnet run`), tests, cURL/`.http` demo, fault-injection demo, layout, **AI Collaboration Narrative** (strategy, verification, quality — from SYSTEMS.md §4 and the Session Log). DESIGN.md §19 refreshed; version 1.0.
  - ✅ A reviewer can build, run and test from the README alone (verified on a fresh clone). — _README, frontend/README, DESIGN §19 and v1.0 done; every README output captured from a real run. Verified 2026-10-28 on a fresh `git clone` of `1bd7ad5` from GitHub: build 0 warnings, 306/306 on the first cold run, no OpenAPI drift, `docker compose up --build` from the clone → all services up, API healthy, search and UI work. CI green on the same commit._

**Day 3 exit check:** brief deliverables 1 and 2 complete · tag `v1.0-submission`.

---

## Day 4 — Frontend demo client (extra) `[~]`

Goal: a polished UI that shows the full user experience against the real API (or its built-in mock mode). Blazor WebAssembly, decided 2026-10-28 (D-025; replaces the React plan).

- [x] **D4.1 Scaffold** — `frontend/DocViewer.Web` (Blazor WASM standalone, :5173) referencing `DocViewer.Domain`; `tests/DocViewer.Web.Tests` (bUnit, xUnit, Shouldly); typed `DocumentsApiClient` over `HttpClient` with a source-generated `JsonSerializerContext`; wire DTOs checked against `docs/openapi.json` by a test.
  - ✅ `dotnet build` 0 warnings; a contract test fails if a DTO property is not in the OpenAPI schema.
- [x] **D4.2 Search** — VIN input with live format hints from `Vin.TryParse`, normalisation (trim, upper case), recent searches (localStorage, failures ignored), deep link `/?vin=`.
  - ✅ bUnit: hint text for 16 chars / `O`; submit disabled until valid; deep link triggers a search; recent list updates.
- [x] **D4.3 Results** — merged table; colour-coded **source badges**; status chips (Expired / Expiring soon); duplicate & conflict icons with tooltips; summary counts.
  - ✅ bUnit: one badge per document with its source; chips and flag icons render per status/flag.
- [x] **D4.4 States** — loading skeleton, empty state, 400 inline error, **partial banner** naming the failed source, **stale banner** with "as of" time, 503 retry screen, trace id shown for support.
  - ✅ bUnit test per state.
- [x] **D4.5 Mock mode** — `Api:UseMocks=true` serves canned responses for every state above from an in-process handler (no backend needed).
  - ✅ Tests run the page against the mock handler; README documents the demo VINs.
- [~] **D4.6 Tests + compose** — UI image (SDK publish → `nginx:alpine`) and `web` service on :5173; CORS verified against the running API.
  - ✅ `docker compose up --build` → UI loads and searches the real API. — _verified over HTTP: UI 200 on :5173 incl. deep links, API answers the UI origin with CORS + exposed headers, mock admin preflight allowed. Not yet clicked through in a browser (no browser tool in the agent session)._

**Day 4 exit check:** UI demo of normal, partial, stale and invalid flows · commit `day4: frontend`.

---

## Day 5 — Smart features, hardening, video (extra) `[~]`

- [x] **D5.1 Expiry alerts** — summary banner "1 expired, 1 expiring in 10 days"; filter by status (server-side filters already exist).
- [x] **D5.2 Source health panel** — UI panel from `/api/v1/sources/health` showing circuit state and latency; failure demo toggles that call the mocks' `/__admin/fault` (mocks allow the UI origin on `/__admin` only).
- [x] **D5.3 Filters** — by source and type in the UI, synced to URL query.
- [x] **D5.4 Load test** — k6 script (`infra/k6/search.js`), results table in README; verify p95 targets and breaker behaviour under induced failure. — _All targets met on 2026-10-30 (Kumar, machine otherwise idle): hit p95 5.5 ms, miss p95 8.2 ms, 0/11 970 failed, breaker-open p95 6.9 ms. The earlier miss was CPU contention from minikube (E-014, resolved)._
- [x] **D5.5 Real-Postgres tests** — Testcontainers PostgreSQL job for the repository and migrations (catches SQLite/Postgres differences); log any differences found. — _6 tests on `postgres:16-alpine` via Testcontainers 4.16.0, in the normal test run (CI included); skipped with a reason without Docker. One difference found and logged: `jsonb` re-formats stored JSON (E-015)._
- [~] **D5.6 Final review** — `/code-review` pass on the whole repo, security review (no VIN in logs, no secrets), update docs, re-tag `v1.1`. — _Code review of everything since the first submission: 10 findings, 9 fixed with tests (AI-013), 1 is Kumar's decision (Q4). Security: raw VIN found in nginx logs and fixed (E-017); 0 raw VINs in all container logs and metrics afterwards; no secrets committed; VINs at rest recorded as R3. 331 tests, coverage gate passes. Remaining: tag `v1.1` (needs Kumar's OK)._
- [~] **D5.7 Video (5–10 min)** — script in `docs/video-script.md`: intro & scenario (0:30) → design walkthrough (2:00) → implementation highlights (1:30) → AI collaboration story (1:30) → live demo incl. failure toggle (2:30) → learnings & challenges (1:00). Record and upload. — _Script written (`docs/video-script.md`, 9:00 with prep checklist and paste-ready PowerShell). Recording and upload: Kumar._

---

## Backlog (only if time remains)

- [x] .NET Aspire AppHost: one-click local orchestration with the Aspire dashboard (logs, traces, metrics). — _2026-10-30: `aspire/DocViewer.AppHost` (Aspire 13.6.1, D-030); end-to-end test starts everything and makes a real search; MessagePack security pin (E-021). Dashboard view of traces/metrics not yet checked by eye._
- [x] Contract fuzzing of the API against `docs/openapi.json` (e.g. Schemathesis in CI). — _2026-10-28: `contract-fuzz` CI job (D-026). Found and fixed a schema bug (E-016); spec now states the VIN pattern and filter enums. Local run: 1 390 requests, 0 issues._
- [x] Pagination for long histories (`limit`/`cursor`). — _2026-10-30: opt-in keyset pagination (D-031); no change without `limit`; fuzzing config sends a valid cursor; the UI still loads full lists._
- [x] Shared circuit-breaker state across replicas. — _2026-10-31: full failure window in Redis (D-032), custom Polly strategy with D-014's rules, local fallback while Redis is down. Verified with two API containers: failures on one open the circuit on the other. A null-parsing bug found only by the real-Redis test (AI-017); a cold-connect budget miss found only live (E-022)._
- [x] Background refresh of stale sources via a queue. — _2026-10-31: in-process `Channel` scheduler (D-033); partial results retried after 5/15/45 s, complete result cached so the next search is a complete HIT; no audit row, not counted as a response. Verified in Docker. CI flake on the previous commit logged as E-023._
- [x] OIDC auth (JWT bearer) with dealer scoping. — _2026-10-31: optional, off by default (D-034); per-document dealer scope, fail closed, marks and counts recomputed so nothing hidden leaks; `refresh` needs `admin`. 16 API tests through the real JwtBearer handler; not tried against a real identity provider. The demo UI has no sign-in._
- [x] Helm chart. — _2026-10-31: `deploy/helm/docviewer` (D-035), CI lint + kubeconform + guard rails. Installed on minikube: all checks passed after fixing three product bugs the cluster exposed: no schema retry (E-024), raw VINs in pipeline log scopes since D-014 (E-025), cold shared breaker (E-026)._

---

## Session Log

Newest first. One entry per working session. Keep each entry short.

| Date | Agent / person | Tasks touched | Outcome | Notes / next step |
|---|---|---|---|---|
| 2026-10-31 | Claude Code (with Kumar) | Backlog: shared breaker, background refresh, OIDC auth, Helm | Kumar chose: full shared failure window, in-process Channel queue, auth off by default with document scope, commit + push each. Four commits, each verified live (two API containers; Docker refresh; compose with auth off; minikube install). D-032…D-035. Bugs found on the way: Redis null parsed as 1970 (AI-017, only the real-Redis test saw it), cold shared-state connect (E-022), and from the Kubernetes run: no schema retry (E-024), raw VINs in pipeline log scopes since D-014 (E-025, Polly warnings), cold breaker warm-up (E-026). One CI flake not reproduced (E-023). Own mistakes logged: stale-binary test runs, test fields from memory, a racy test, a `!` CI check that could not fail (AI-018, AI-019). 429 tests green incl. containers; coverage Domain 98.5 %, Application 96.9 %. Not verified: auth against a real identity provider; the demo UI has no sign-in. | Kumar's list in Current Focus. Optional next: dealer in the audit row, UI sign-in. |
| 2026-10-30 | Claude Code (with Kumar) | Backlog: pagination | Kumar chose opt-in keyset. D-012 order moved into `DocumentPosition` (merger and pager share it). `?limit`/`?cursor`, `nextCursor`, summary over the whole filtered result; 400 `invalid-page`. Fuzzing found the generated `limit` schema accepted "-64" (fixed, pinned by a test) and needs a valid cursor (schemathesis.toml). Two own mistakes caught before they counted: a test payload edit that matched nothing, and a hand-made cursor with the wrong enum spelling. Live: vehicle 8 pages 20+20+5 = 45, same order as unpaged. 365 tests green. | Rest of Current Focus unchanged. |
| 2026-10-30 | Claude Code (with Kumar) | Backlog: Aspire AppHost | Kumar asked for the strongest backlog item. `aspire/DocViewer.AppHost` on Aspire 13.6.1 (13.6.x newer were < 2 weeks old); NuGet audit blocked MessagePack 2.5.302 (GHSA-qhrr-8q5h-9q3h) → pinned 2.5.307 (E-021). Verified live: `Healthy`, search from both sources, cache key in Redis, mocks + UI + CORS on the usual ports; images pinned to compose's (Aspire defaulted to Postgres 18.3 / Redis 8.6). End-to-end test (30 s) fails when the Postgres wiring is removed. Opt-in OTLP metrics for the dashboard. Not verified: traces/metrics by eye in the dashboard (its API needs a key). An over-broad container clean-up command was blocked by the safety check; only Aspire-labelled containers were touched afterwards (already gone). | Kumar: open the dashboard once and look at Traces/Metrics. Rest of Current Focus unchanged. |
| 2026-10-29/30 | Claude Code (with Kumar) | Design-risk review, then fixes | Kumar's 8 risks checked against code and measured live: 2 high bugs (E-018 Postgres, E-019 Redis: searches ~12 s), gaps R4–R8, AI-014. Kumar chose: breaker min 3, 60/min rate limit, refuse demo salt in Production, commit+push per fix. Fixed one by one, each red test first, then Docker re-measure: snapshot budget 300 ms (1.84 s, was 11.8 s); cache budget 150 ms off-thread (0.32–0.63 s, was 11–12 s) + start-up warm-up; breaker opens by the 3rd search (D-027); 429 after 60 searches/min/IP (D-028); Production refuses demo salts, compose runs as `Demo` (D-029); spec describes `partial` (R7). Three timing-dependent tests fixed at the cause (E-020); CI caught one the agent missed. A wrong "0/10" logged as AI-015. 349 tests green. | Kumar's list in Current Focus is unchanged (Q4, tag, k6 re-run, browser pass, Jaeger screenshot, video). Note for k6: set `RATE_LIMIT_PER_MINUTE` first (README §7.1). |
| 2026-10-28 (evening) | Claude Code (with Kumar) | D5.5, contract fuzzing, D5.6, D5.7 | Resumed from the Session Log as planned. D5.5 done: 6 real-Postgres tests (Testcontainers), run in CI; found the `jsonb` text difference (E-015). Contract fuzzing in CI (D-026): found and fixed a schema bug (E-016, AI-012); CI fuzz 2 934 requests, 0 issues. D5.6: code review → 9 fixes + Q4 for Kumar (AI-013); security pass found raw VINs in nginx logs, fixed (E-017); 0 raw VINs in any container log or metrics. D5.7: video script. 331 tests, coverage Domain 99.0 %, Application 98.4 %, Api 94.6 %, Infra 94.1 %, Web 91.3 %. All pushed to main. | Kumar's list in Current Focus (Q4, tag v1.1, k6 re-run, browser pass, Jaeger screenshot, video). |
| 2026-10-28 | Claude Code (with Kumar) | Commit/push, D3.7, AGENTS.md, D5.4, D5.5 (started) | Kumar decided: commit + push per task; k6 and Testcontainers approved; backlog = contract fuzzing only; AGENTS.md updated for Blazor. Pushed `5b60bd5`…`2a71692` to main; CI green on `1bd7ad5`. D3.7 closed after a fresh `git clone` check. D5.4: k6 script + `load` profile; breaker verified; cache-hit p95 133 ms at 50 users vs 20 ms target (E-014, open). **Paused mid-D5.5, nothing of it committed:** on disk are `Testcontainers.PostgreSql` 4.16.0 in `Directory.Packages.props` + `DocViewer.Infrastructure.Tests.csproj`, and `tests/DocViewer.Infrastructure.Tests/Persistence/Postgres/{PostgresFactAttribute,PostgresContainer,PostgresGroup}.cs` (not yet built). The Docker stack is currently running from a scratch fresh-clone folder (same project name `docviewer`). | **Exact next step:** write `PostgresPersistenceTests.cs` in that folder (`[Collection(PostgresGroup.Name)]`, `[PostgresFact]`, `ResetAsync` per test): migrations apply and `source_snapshot.documents` / `search_audit.source_status` are `jsonb`; snapshot round-trip, overwrite, per-source, max age; audit row. Expect `SourceStatusJson.ShouldContain("\"Service\":\"Timeout\"")` to fail on Postgres (jsonb normalises spacing): assert parsed JSON instead and log the difference (D-019). Then build, test, commit `D5.5`, push, watch CI. Then Schemathesis, D5.6, D5.7. |
| 2026-10-28 | Claude Code (with Kumar) | Q3, D3.7, D4.1–D4.6, D5.1–D5.3 | Kumar decided: Q3 → *degraded* (D-024), Blazor WASM (D-025), no commits. README written (PowerShell + bash, tables from real runs, AI narrative); DESIGN v1.0. Blazor client with mock mode, health panel + fault toggles, URL filters, expiry alert; nginx `web` service. Fixed in Docker: API went `unhealthy` with Postgres stopped (E-012). Found the cause of the cold-run flake (E-013, solves E-011). 306 tests green (Web 58), 0 warnings, format clean; coverage Domain 99.0 %, Application 98.4 %, Infra 94.1 %, Api 93.9 %, Web 89.9 %. New: D-024, D-025, E-011…E-013, AI-009…AI-011. | Nothing committed. Kumar: review + commit/push, then fresh-clone check (D3.7), browser pass at :5173 (D4.6), Jaeger screenshot (D3.3). AGENTS.md still describes a TypeScript frontend (§6, §11): Kumar to approve an update. Next task: D5.4 k6. |
| 2026-10-28 | Claude Code (with Kumar) | Handover | Uncommitted on disk: `ContainerHealthCheck.cs` + tests, compose healthcheck, DESIGN/SYSTEMS/ROADMAP updates. Open questions for Kumar: Q3 (readiness semantics; agent recommends 'degraded' = 200), React vs Blazor for Day 4 (never answered), repo is private. For D3.7 README: Windows PowerShell's `curl` is an alias of Invoke-WebRequest; show `curl.exe` or `Invoke-RestMethod` examples, plus a human-readable table view (`$r.documents \| Format-Table source, type, title, date, expiryDate, status`). Kumar found raw JSON hard to read: explain outputs in plain tables. | Next: ask about commit/push, then D3.7. |
| 2026-10-27 | Claude Code (with Kumar) | API container healthcheck | `--healthcheck` mode + compose healthcheck (D-023); 8 tests; verified in Docker: healthy → unhealthy with Postgres stopped → healthy. Unreproduced cold-start 503 logged (E-010). | Next: D3.7 README; decide Q3 (readiness semantics). |
| 2026-10-27 | Claude Code (with Kumar) | D1.1, D3.4–D3.6 | First commit pushed to private repo kumarabhik/keyloop-document-viewer; first CI run green (build, format, tests + coverage gate, OpenAPI drift, Docker build); fresh clone → `docker compose up --build` → demo works without a .env. | Next: D3.7 README; Jaeger screenshot for D3.3. Optional: bump actions to Node 24 versions (deprecation warning only). |
| 2026-10-27 | Claude Code (with Kumar) | D2.4–D3.6 | Day 2 done; D3.1–D3.2 done; D3.3–D3.6 built and verified, each waiting on commit/push or a screenshot. 230 tests green, 0 warnings, format clean. Coverage: Domain 99.0 %, Application 98.4 %, Infrastructure 93.3 %, Api 93.7 %. Docker stack verified end to end (Postgres, Redis, Jaeger trace, Prometheus, Grafana). Found and fixed a raw-VIN leak in log scopes and span URLs (E-007). New: D-020…D-022, E-005…E-009, AI-008, Q3. | Next: D3.7 README. Ask Kumar: first commit + push (closes D1.1, D3.5, D3.6), Q3 readiness semantics. |
| 2026-10-27 | Claude Code (with Kumar) | D1.1–D2.3 | 9 tasks done, D1.1 awaiting commit. 169 tests green, 0 warnings. Coverage: Domain 99 %, Application 80 %, Infrastructure 76 %. New: D-018, D-019, E-001…E-004, AI-007. | Next: D2.4 AggregationService. Ask Kumar about the first commit. |
| 2026-10-27 | Claude Code (with Kumar) | D0.6, D1.1, D1.2 | Python scaffold for D1.1–D1.2 built, then discarded on switch to .NET (D-013, AI-006). Docs re-platformed. | Install .NET 10 SDK, then restart D1.1 |
| 2026-10-27 | Claude Code (with Kumar) | D0.1–D0.5 | Planning docs created | Start D1.1 next session |
