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
| Next task | D3.7 README + design doc finalisation; D3.3 needs only a Jaeger UI screenshot |
| Blockers | None |
| Last updated | 2026-10-27 (D2.4–D3.6 session; 230 tests green) |

## Progress Summary

| Phase | Tasks | Done | Status |
|---|---|---|---|
| Day 0 — Planning | 6 | 6 | `[x]` |
| Day 1 — Foundations: solution, domain, mocks, adapters | 7 | 7 | `[x]` |
| Day 2 — Aggregation, resilience, cache, persistence, API | 7 | 7 | `[x]` |
| Day 3 — Observability, packaging, docs (**submission-ready**) | 7 | 5 (+D3.3 partial) | `[~]` |
| Day 4 — Frontend demo client | 6 | 0 | `[ ]` |
| Day 5 — Smart features, hardening, video | 7 | 0 | `[ ]` |

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

## Day 3 — Observability, packaging, documentation `[~]`

Goal: **submission-ready.** Everything the brief requires exists and runs from a clean clone.

- [x] **D3.1 Structured logging** — Serilog compact JSON, request logging, `TraceId` enrichment, `[LoggerMessage]` methods for search and per-source events (DESIGN §12.1).
  - ✅ Test with an in-memory sink: raw VIN never logged; trace id on every event.

- [x] **D3.2 Metrics** — `DocViewerMetrics` (Meter "DocViewer") with all custom instruments in DESIGN §12.2; OpenTelemetry metrics incl. ASP.NET Core, HttpClient, runtime, Polly; Prometheus `/metrics`.
  - ✅ Test with `MetricCollector<T>`: hit/miss/partial/stale counters increment; no VIN tags.

- [~] **D3.3 Tracing** — OpenTelemetry tracing (ASP.NET Core, HttpClient, EF Core) + `ActivitySource("DocViewer")` spans; OTLP exporter; `traceId` in body and `X-Trace-Id`. — _verified via Jaeger API (span tree in docs/diagrams/trace-example.md); UI screenshot still to take_
  - ✅ Manual check in Jaeger / Aspire dashboard: one trace shows two parallel source spans (screenshot in `docs/diagrams/`).

- [x] **D3.4 Docker Compose** — multi-stage Dockerfiles (SDK build → chiselled ASP.NET runtime) for API and mocks; `docker-compose.yml` with api, sales-mock, service-mock, postgres, redis, healthchecks; `obs` profile with Prometheus, Grafana (provisioned dashboard), Jaeger.
  - ✅ Clean clone → `docker compose up --build` → cURL demo works.

- [x] **D3.5 OpenAPI** — `Microsoft.AspNetCore.OpenApi` + build-time generation to `docs/openapi.json`; examples and descriptions on endpoints; Scalar UI in Development.
  - ✅ Spec committed; CI fails if `git diff --exit-code docs/openapi.json` shows drift after build.

- [x] **D3.6 CI** — `.github/workflows/ci.yml`: setup-dotnet 10, restore, build (warnings as errors), `dotnet format --verify-no-changes`, test + coverage, OpenAPI drift.
  - ✅ Green run on GitHub.

- [ ] **D3.7 README and design doc finalisation** — README: overview, prerequisites, quick start (Docker and `dotnet run`), tests, cURL/`.http` demo, fault-injection demo, layout, **AI Collaboration Narrative** (strategy, verification, quality — from SYSTEMS.md §4 and the Session Log). DESIGN.md §19 refreshed; version 1.0.
  - ✅ A reviewer can build, run and test from the README alone (verified on a fresh clone).

**Day 3 exit check:** brief deliverables 1 and 2 complete · tag `v1.0-submission`.

---

## Day 4 — Frontend demo client (extra) `[ ]`

Goal: a polished UI that shows the full user experience against the real API (or MSW mocks).

- [ ] **D4.1 Scaffold** — Vite + React + TS + Tailwind + TanStack Query + Vitest; typed API client generated from `docs/openapi.json` (`openapi-typescript`).
- [ ] **D4.2 Search** — VIN input with live format hints, normalisation, recent searches (localStorage), deep link `/?vin=`.
- [ ] **D4.3 Results** — merged table/cards; colour-coded **source badges**; status chips (Expired / Expiring soon); duplicate & conflict icons with tooltips; summary counts.
- [ ] **D4.4 States** — loading skeleton, empty state, 400 inline error, **partial banner** naming the failed source, **stale banner** with "as of" time, 503 retry screen, trace id shown for support.
- [ ] **D4.5 MSW mock mode** — `VITE_USE_MOCKS=true` runs the UI without the backend, with handlers for every state above.
- [ ] **D4.6 Tests + compose** — Vitest/Testing Library for banners and badges; UI service added to compose; CORS verified.

**Day 4 exit check:** UI demo of normal, partial, stale and invalid flows · commit `day4: frontend`.

---

## Day 5 — Smart features, hardening, video (extra) `[ ]`

- [ ] **D5.1 Expiry alerts** — summary banner "1 expired, 1 expiring in 10 days"; filter by status (server-side filters already exist).
- [ ] **D5.2 Source health panel** — UI panel from `/api/v1/sources/health` showing circuit state and latency; failure demo toggles that call the mocks' `/__admin/fault`.
- [ ] **D5.3 Filters** — by source and type in the UI, synced to URL query.
- [ ] **D5.4 Load test** — k6 script (`infra/k6/search.js`), results table in README; verify p95 targets and breaker behaviour under induced failure.
- [ ] **D5.5 Real-Postgres tests** — Testcontainers PostgreSQL job for the repository and migrations (catches SQLite/Postgres differences); log any differences found.
- [ ] **D5.6 Final review** — `/code-review` pass on the whole repo, security review (no VIN in logs, no secrets), update docs, re-tag `v1.1`.
- [ ] **D5.7 Video (5–10 min)** — script in `docs/video-script.md`: intro & scenario (0:30) → design walkthrough (2:00) → implementation highlights (1:30) → AI collaboration story (1:30) → live demo incl. failure toggle (2:30) → learnings & challenges (1:00). Record and upload.

---

## Backlog (only if time remains)

- [ ] .NET Aspire AppHost: one-click local orchestration with the Aspire dashboard (logs, traces, metrics).
- [ ] Contract fuzzing of the API against `docs/openapi.json` (e.g. Schemathesis in CI).
- [ ] Pagination for long histories (`limit`/`cursor`).
- [ ] Shared circuit-breaker state across replicas.
- [ ] Background refresh of stale sources via a queue.
- [ ] OIDC auth (JWT bearer) with dealer scoping.
- [ ] Helm chart.

---

## Session Log

Newest first. One entry per working session. Keep each entry short.

| Date | Agent / person | Tasks touched | Outcome | Notes / next step |
|---|---|---|---|---|
| 2026-10-27 | Claude Code (with Kumar) | D1.1, D3.4–D3.6 | First commit pushed to private repo kumarabhik/keyloop-document-viewer; first CI run green (build, format, tests + coverage gate, OpenAPI drift, Docker build); fresh clone → `docker compose up --build` → demo works without a .env. | Next: D3.7 README; Jaeger screenshot for D3.3. Optional: bump actions to Node 24 versions (deprecation warning only). |
| 2026-10-27 | Claude Code (with Kumar) | D2.4–D3.6 | Day 2 done; D3.1–D3.2 done; D3.3–D3.6 built and verified, each waiting on commit/push or a screenshot. 230 tests green, 0 warnings, format clean. Coverage: Domain 99.0 %, Application 98.4 %, Infrastructure 93.3 %, Api 93.7 %. Docker stack verified end to end (Postgres, Redis, Jaeger trace, Prometheus, Grafana). Found and fixed a raw-VIN leak in log scopes and span URLs (E-007). New: D-020…D-022, E-005…E-009, AI-008, Q3. | Next: D3.7 README. Ask Kumar: first commit + push (closes D1.1, D3.5, D3.6), Q3 readiness semantics. |
| 2026-10-27 | Claude Code (with Kumar) | D1.1–D2.3 | 9 tasks done, D1.1 awaiting commit. 169 tests green, 0 warnings. Coverage: Domain 99 %, Application 80 %, Infrastructure 76 %. New: D-018, D-019, E-001…E-004, AI-007. | Next: D2.4 AggregationService. Ask Kumar about the first commit. |
| 2026-10-27 | Claude Code (with Kumar) | D0.6, D1.1, D1.2 | Python scaffold for D1.1–D1.2 built, then discarded on switch to .NET (D-013, AI-006). Docs re-platformed. | Install .NET 10 SDK, then restart D1.1 |
| 2026-10-27 | Claude Code (with Kumar) | D0.1–D0.5 | Planning docs created | Start D1.1 next session |
