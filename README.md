# Unified Document Viewer

Keyloop technical assessment — **Scenario D**. A service advisor types one VIN; the backend asks the dealership's **Sales** and **Service** systems **in parallel**, turns their different formats into one document shape, and returns **one merged list in which every document names its source**. If one system is down, the advisor still gets what the other one has, with a clear warning.

| | |
|---|---|
| Fully implemented layer | **Backend** — .NET 10 / ASP.NET Core Minimal API (Clean Architecture) |
| Mocked layer | The two source systems (small ASP.NET Core apps with fault injection); a thin demo UI is an extra |
| Design document | [DESIGN.md](DESIGN.md) — architecture, API contract, rules, resilience, observability |
| Decisions, errors, AI corrections | [SYSTEMS.md](SYSTEMS.md) · progress in [ROADMAP.md](ROADMAP.md) · agent rules in [AGENTS.md](AGENTS.md) |
| API contract | [docs/openapi.json](docs/openapi.json) (generated on every build; CI fails on drift) |

---

## Contents

1. [What it does](#1-what-it-does)
2. [Prerequisites](#2-prerequisites)
3. [Quick start with Docker](#3-quick-start-with-docker)
4. [Quick start without Docker](#4-quick-start-without-docker)
5. [Try the API](#5-try-the-api)
6. [Failure demo (fault injection)](#6-failure-demo-fault-injection)
7. [Tests and quality gates](#7-tests-and-quality-gates)
8. [Observability](#8-observability)
9. [Repository layout](#9-repository-layout)
10. [AI Collaboration Narrative](#10-ai-collaboration-narrative)

---

## 1. What it does

```text
                         GET /api/v1/vehicles/{vin}/documents
                                        │
                        validate VIN ── 400 if invalid (no source is called)
                                        │
                              cache hit? ── return (X-Cache: HIT)
                                        │
                   ┌──────────── Task.WhenAll ────────────┐
                   ▼                                      ▼
         Sales adapter (camelCase, ISO dates,     Service adapter (snake_case, dd/MM/yyyy,
         404 = no documents)                      empty list = no documents)
         Polly: 2 s total, ≤1 retry, breaker      Polly: 2 s total, ≤1 retry, breaker
                   └──────────────┬───────────────────────┘
                                  ▼
            merge · sort · status (expired / expiring soon) · flag duplicates & conflicts
                                  ▼
       one source failed? → use its last-known-good snapshot (≤ 24 h) if there is one
                                  ▼
              200 complete │ 200 partial │ 503 only when both failed and no snapshot
```

| Outcome | HTTP | What the user sees |
|---|---|---|
| Both sources answer | 200, `partial: false` | All documents, each with `source` |
| One source fails | 200, `partial: true` | The other source's documents + the failed source's last snapshot (`status: stale`, `asOf`) if one exists |
| Both fail, a snapshot exists | 200, `partial: true` | Stale documents, labelled |
| Both fail, no snapshot | 503 + `Retry-After` | Problem details naming both failures |
| VIN has no documents | 200, `documents: []` | Empty state, not an error (D-004) |
| Bad VIN | 400 problem | A message saying exactly what is wrong |

Every response is bounded at about 2.2 s, whatever the sources do (D-005). Only complete responses are cached (60 s), so a one-second blip is never served for a minute (D-006). Redis or Postgres being down never fails a search (invariant 10).

## 2. Prerequisites

| Tool | Needed for | Check |
|---|---|---|
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | Quick start with Docker (recommended) | `docker compose version` |
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.403 or later 10.0.x) | Building, running without Docker, tests | `dotnet --version` |

Nothing else: no Node.js, no database install. Without Docker the API uses a local SQLite file and an in-memory cache.

> **Windows PowerShell note.** In Windows PowerShell 5.1, `curl` is an alias for `Invoke-WebRequest`, not real curl. Use **`curl.exe`** (ships with Windows 10+) or **`Invoke-RestMethod`**. Every example below has a PowerShell version.

## 3. Quick start with Docker

```bash
git clone https://github.com/kumarabhik/keyloop-document-viewer.git
cd keyloop-document-viewer
docker compose up --build -d
```

The first build takes a few minutes. When `docker compose ps` shows the `api` container as **healthy**, open:

| URL | What |
|---|---|
| <http://localhost:8080/api/v1/vehicles/MA3EWB81S00100001/documents> | A merged search result |
| <http://localhost:8080/readyz> | `Healthy`, or `Degraded` if Postgres is down (searches still work, D-024) |
| <http://localhost:8081>, <http://localhost:8082> | Sales and Service mocks |
| <http://localhost:5173> | Demo UI (Blazor WebAssembly) — see [§5.4](#54-demo-ui) |

Add the observability stack (Prometheus :9090, Grafana :3000, Jaeger :16686):

```bash
docker compose --profile obs up --build -d
```

Stop everything with `docker compose --profile obs down` (add `-v` to delete the Postgres data). No `.env` file is needed: compose runs in a `Demo` environment with local defaults (see [.env.example](.env.example) to override). For a real deployment set `ASPNETCORE_ENVIRONMENT=Production` **and** a random `VIN_HASH_SALT`; Production refuses to start with the public demo salt (D-029).

## 4. Quick start without Docker

Three terminals, from the repository root:

```bash
dotnet run --project mocks/SalesSystem.Mock      # :8081
dotnet run --project mocks/ServiceSystem.Mock    # :8082
dotnet run --project src/DocViewer.Api           # :8080 (Development: Scalar UI at /scalar)
```

In Development the API creates a SQLite database file and uses an in-memory cache. Interactive API docs: <http://localhost:8080/scalar>.

### 4.1 One command with .NET Aspire (optional)

```powershell
docker compose down                                # same ports (8080–8082, 5173), so stop the compose stack first
dotnet run --project aspire/DocViewer.AppHost      # Docker must be running: Postgres and Redis run as containers
```

The console prints a dashboard link (`http://localhost:15201/login?t=…`). The dashboard lists every resource (Postgres, Redis, both mocks, the API, the UI) with its state and console logs; the API also sends its traces and metrics there. Everything else (URLs, demo VINs, fault switches) works as with Docker Compose. Notes: the first start pulls `postgres:16-alpine` and `redis:7-alpine`; Aspire's tooling may ask to trust the .NET HTTPS development certificate; structured logs stay in the console view because the API logs through Serilog to stdout. `tests/DocViewer.AppHost.Tests` starts the whole system this way and makes a real search (needs Docker).

## 5. Try the API

### 5.1 Seed vehicles

The mocks start with ten vehicles, each built to show one scenario ([DESIGN.md Appendix A](DESIGN.md#appendix-a--seed-data-matrix)). Expiry dates are relative to the day the mock starts, so "expired" and "expiring soon" stay true whenever you run it (D-018).

| VIN | Scenario |
|---|---|
| `MA3EWB81S00100001` | Normal: 4 Sales + 5 Service documents |
| `MA3EWB81S00100002` | Sales only |
| `MA3EWB81S00100003` | Service only (Sales answers 404) |
| `MA3EWB81S00100004` | No documents anywhere → 200 with an empty list |
| `MA3EWB81S00100005` | Insurance **expired**, PUC **expiring soon** |
| `MA3EWB81S00100006` | Same invoice in both systems → `POSSIBLE_DUPLICATE` |
| `MA3EWB81S00100007` | Same warranty, different expiry → `CONFLICT` |
| `MA3EWB81S00100008` | Long history (45 documents) |
| `MA3EWB81S00100009` | Service has one malformed record → dropped and counted |
| `MA3EWB81S00100010` | Documents with equal dates → deterministic tie-break |

### 5.2 Search — Windows PowerShell

`Invoke-RestMethod` parses the JSON, so you can show it as tables:

```powershell
$r = Invoke-RestMethod http://localhost:8080/api/v1/vehicles/MA3EWB81S00100001/documents
$r.sources   | Format-Table name, status, latencyMs, count
$r.documents | Format-Table source, type, title, date, expiryDate, status
```

Real output:

```text
name    status latencyMs count
----    ------ --------- -----
SALES   ok           873     4
SERVICE ok           675     5

source  type            title                               date       expiryDate status
------  ----            -----                               ----       ---------- ------
SERVICE PUC             Pollution Under Control certificate 2026-08-28 2027-02-24 VALID
SERVICE SERVICE_HISTORY Periodic maintenance 20,000 km      2026-08-28            NOT_APPLICABLE
SALES   INSURANCE       Comprehensive Insurance             2026-04-10 2027-04-10 VALID
SERVICE SERVICE_HISTORY Periodic maintenance 10,000 km      2024-10-18            NOT_APPLICABLE
SERVICE JOB_CARD        Second free service                 2024-03-20            NOT_APPLICABLE
SERVICE JOB_CARD        First free service                  2023-09-15            NOT_APPLICABLE
SALES   RC              Registration Certificate            2023-06-20 2038-06-19 VALID
SALES   INVOICE         Sales Invoice                       2023-06-12            NOT_APPLICABLE
SALES   WARRANTY        Extended Warranty                   2023-06-12 2028-06-11 VALID
```

(The first call is a cache miss, which includes connection setup; repeat it and the response comes from the cache in a few milliseconds with `cache = HIT`.)

More examples:

```powershell
# Expired and expiring documents (vehicle 5)
$r = Invoke-RestMethod http://localhost:8080/api/v1/vehicles/MA3EWB81S00100005/documents
$r.summary   | Format-Table total, expired, expiringSoon, duplicates, conflicts
$r.documents | Format-Table source, type, title, expiryDate, status

# Conflict between the two systems (vehicle 7)
$r = Invoke-RestMethod http://localhost:8080/api/v1/vehicles/MA3EWB81S00100007/documents
$r.documents | Format-Table source, type, title, expiryDate, @{ n = 'flags'; e = { $_.flags -join ',' } }

# Pagination (opt-in, D-031): vehicle 8 has 45 documents; walk them 20 at a time
$page = Invoke-RestMethod "http://localhost:8080/api/v1/vehicles/MA3EWB81S00100008/documents?limit=20"
"$($page.documents.Count) of $($page.summary.total); next cursor: $($page.nextCursor)"
$next = Invoke-RestMethod "http://localhost:8080/api/v1/vehicles/MA3EWB81S00100008/documents?limit=20&cursor=$($page.nextCursor)"

# Filters (case-insensitive, repeatable or comma-separated)
Invoke-RestMethod "http://localhost:8080/api/v1/vehicles/MA3EWB81S00100001/documents?source=SERVICE&type=PUC,JOB_CARD" |
  ForEach-Object documents | Format-Table source, type, title

# Headers too (X-Cache, X-Trace-Id) — curl.exe, not the curl alias
curl.exe -s -i http://localhost:8080/api/v1/vehicles/BADVIN/documents
```

Vehicle 7 shows the two systems disagreeing about the warranty; both records are kept and flagged, never merged (D-009):

```text
source  type     title                 expiryDate flags
------  ----     -----                 ---------- -----
SERVICE JOB_CARD First free service
SALES   INVOICE  Sales Invoice
SALES   WARRANTY Standard Warranty     2027-02-11 CONFLICT
SERVICE WARRANTY Warranty registration 2028-02-11 CONFLICT
```

### 5.3 Search — bash / macOS / Linux

```bash
curl -s localhost:8080/api/v1/vehicles/MA3EWB81S00100001/documents | jq '.documents[] | {source, type, title, status}'
curl -s "localhost:8080/api/v1/vehicles/MA3EWB81S00100005/documents?status=EXPIRED" | jq .summary
curl -s -i localhost:8080/api/v1/vehicles/BADVIN/documents          # 400 problem details
curl -s localhost:8080/api/v1/sources/health | jq                   # circuit state per source
```

All of these, and the failure demo below, are also in [http/docviewer.http](http/docviewer.http) for VS Code REST Client, Visual Studio and Rider.

### 5.4 Demo UI

A small Blazor WebAssembly client is included as an extra (the backend is the graded layer). With Docker it runs at <http://localhost:5173>. Without Docker:

```bash
dotnet run --project frontend/DocViewer.Web       # http://localhost:5173, talks to the API on :8080
```

It can also run with **no backend at all** in mock mode — see [frontend/README.md](frontend/README.md).

### 5.5 Response fields at a glance

| Field | Meaning |
|---|---|
| `partial` | `true` when any source is not `ok` |
| `cache` | `HIT`, `MISS`, or `BYPASS` (`?refresh=true`) |
| `traceId` | Same value as the `X-Trace-Id` header; find the request in logs and Jaeger with it |
| `sources[]` | Per source: `status` (`ok`, `stale`, `timeout`, `error`, `circuit_open`), `latencyMs`, `count`, `error`, `asOf` (when stale) |
| `summary` | Counts over every document that matches the filters (all pages): `total`, `expired`, `expiringSoon`, `duplicates`, `conflicts` |
| `nextCursor` | With `?limit=`: pass it as `?cursor=` for the next page; `null` on the last page or without `limit` |
| `documents[]` | `source`, `type`, `title`, `date`, `expiryDate`, `status` (`VALID`, `EXPIRING_SOON`, `EXPIRED`, `NOT_APPLICABLE`), `flags` (`POSSIBLE_DUPLICATE`, `CONFLICT`), `metadata` |

The full contract, with every status code and example, is [DESIGN.md §7](DESIGN.md#7-api-contract) and [docs/openapi.json](docs/openapi.json).

## 6. Failure demo (fault injection)

Each mock has a switch: `POST /__admin/fault` with `{"mode": "none" | "slow" | "timeout" | "error" | "flaky" | "malformed"}`.

**Windows PowerShell:**

```powershell
function Set-Fault($port, $mode) {
  Invoke-RestMethod -Method Post "http://localhost:$port/__admin/fault" -ContentType 'application/json' -Body (@{ mode = $mode } | ConvertTo-Json)
}

# 1. Make the Service system hang
Set-Fault 8082 timeout
$r = Invoke-RestMethod "http://localhost:8080/api/v1/vehicles/MA3EWB81S00100001/documents?refresh=true"
"partial = $($r.partial)"
$r.sources | Format-Table name, status, count, error, asOf

# 2. Break Sales too: a vehicle that was never searched has no snapshot → 503
Set-Fault 8081 error
curl.exe -s -i http://localhost:8080/api/v1/vehicles/MA3EWB81S00100010/documents

# 3. Circuit state and last outcome per source
Invoke-RestMethod http://localhost:8080/api/v1/sources/health | Format-Table

# 4. Heal both
Set-Fault 8081 none; Set-Fault 8082 none
```

**bash:**

```bash
curl -s -X POST localhost:8082/__admin/fault -H 'Content-Type: application/json' -d '{"mode":"timeout"}'
curl -s "localhost:8080/api/v1/vehicles/MA3EWB81S00100001/documents?refresh=true" | jq '{partial, sources}'
curl -s -X POST localhost:8082/__admin/fault -H 'Content-Type: application/json' -d '{"mode":"none"}'
```

What you should see in step 1 (real output; the search took 1.6 s because Service hit its 1.5 s attempt timeout, and the request was **not** retried, D-005):

```text
partial = True

name    status count error   asOf
----    ------ ----- -----   ----
SALES   ok         4
SERVICE stale      5 timeout 2026-10-27T20:31:29.929446+00:00
```

In plain words: Sales answered normally. Service timed out, so the API served Service's last good copy from the database and labelled it `stale` with the time it was taken. The user still sees all nine documents, plus a warning. In step 2 the API returns **503** with `Retry-After: 5`, because neither system answered and there was no earlier copy of vehicle 10 to fall back on.

After three or more calls in ten seconds with at least half failing, a source's circuit breaker opens (`circuit_open`, D-027): the API stops calling it for 30 s and answers immediately instead of waiting 2 s each time (D-014).

## 7. Tests and quality gates

```bash
dotnet build                          # warnings are errors; also regenerates docs/openapi.json
dotnet test                           # all test projects (365 tests, ~1–2 min; needs Docker for 6 of them)
dotnet format --verify-no-changes     # style check
```

Coverage report and the ≥ 85 % gate on Domain and Application:

```powershell
powershell -File scripts/coverage.ps1     # Windows PowerShell 5.1
pwsh scripts/coverage.ps1                 # PowerShell 7 (macOS/Linux/CI)
```

The HTML report lands in `TestResults/coverage/report/index.html`.

| Project | What it proves |
|---|---|
| `DocViewer.Domain.Tests` | VIN rules, expiry status boundaries, sort tie-breaks, duplicate vs conflict |
| `DocViewer.Application.Tests` | Aggregation: partial, stale, both down, never caching partials, parallel fan-out, single-flight |
| `DocViewer.Infrastructure.Tests` | Adapters against WireMock (mapping, `dd/MM/yyyy`, malformed records), Polly timing (timeout ⇒ no retry; 503 ⇒ one retry; breaker opens), cache, EF Core on SQLite **and on a real PostgreSQL container** (migrations, `jsonb`, upserts; skipped without Docker or with `DOCVIEWER_SKIP_POSTGRES_TESTS=1`) |
| `DocViewer.Api.Tests` | HTTP contract end to end, ProblemDetails, filters, headers, **no raw VIN in logs/spans/metrics**, readiness when the DB is down |
| `Mocks.Tests` | Seed scenarios and every fault mode |
| `DocViewer.AppHost.Tests` | The whole system started through the Aspire AppHost answers a real search from both sources and reports `Healthy` (needs Docker) |
| `DocViewer.Web.Tests` | Demo UI with bUnit: source badges, partial/stale banners, every page state, URL filters, contract drift vs `openapi.json` |

Each invariant in [SYSTEMS.md §6](SYSTEMS.md#6-invariants) has a test tagged `[Trait("Invariant", "N")]`:

```bash
dotnet test --filter "Invariant=4"     # e.g. partial responses are never cached
```

CI ([.github/workflows/ci.yml](.github/workflows/ci.yml)) runs build, format, tests with the coverage gate, the OpenAPI drift check, the Docker image build and **contract fuzzing** (Schemathesis sends generated requests to the running stack and checks every answer against `docs/openapi.json`, D-026) on every push.

### 7.1 Load test (k6)

```powershell
$env:RATE_LIMIT_PER_MINUTE = 1000000              # k6 sends ~80 searches/s from one address; lift the 60/min limit (R5)
docker compose up --build -d                      # the stack must be running
docker compose --profile load run --rm k6         # 2 min at 50 users, then the breaker phase (~3 min)
docker compose --profile load run --rm -e VUS=1 -e DURATION=30s k6   # quick single-user run
Remove-Item Env:RATE_LIMIT_PER_MINUTE; docker compose up -d api   # back to the normal limit
```

The script is [infra/k6/search.js](infra/k6/search.js); k6 runs in its own container, nothing to install. Results from 2026-10-28 on the author's laptop (Docker Desktop with 6 CPUs, shared with an unrelated minikube cluster using ~2 of them):

| Measure | Target (DESIGN §2.2) | 1 user | 50 users | Verdict |
|---|---|---|---|---|
| Cache hit, p95 | < 20 ms | **7.8 ms** | **133 ms** (median 5.5 ms) | ✗ at 50 users on this machine |
| Cache miss, p95 | < 300 ms | 44 ms | 251 ms | ✓ |
| Errors | < 1 % | 0 % | 0 % of 11 129 requests | ✓ |
| Throughput | — | 2 req/s | 82 req/s | — |

**Breaker phase** (Service hangs, one user searches 25 times): searches 1–5 took ~1.5 s each (one attempt timeout, no retry, D-005), then the breaker opened and searches 6–25 answered in **4–11 ms** with Service's saved copy (`stale`, reason `circuit_open`). All 25 were HTTP 200 within the 2.2 s budget.

In plain words: one user gets cache hits in a few milliseconds. With 50 users on this laptop most hits are still fast (median 5.5 ms), but the slowest 5 % take up to ~130 ms. The API's thread pool never queued work and GC pauses were small, so the likely cause is CPU contention inside Docker rather than the code. That is a likely explanation, not a proven one; see E-014.

## 8. Observability

| Signal | Where | Notes |
|---|---|---|
| Logs | stdout, compact JSON (Serilog) | Every event carries `@tr` (trace id). VINs appear only masked: `MA3**********0001` (D-011). Successful probes and scrapes are not logged. The UI's nginx logs paths without query strings, so deep-link VINs never reach its log (E-017) |
| Metrics | <http://localhost:8080/metrics> (Prometheus) | `docviewer_source_requests_total`, `docviewer_source_duration_seconds`, `docviewer_cache_requests_total`, `docviewer_partial_responses_total`, `docviewer_stale_served_total`, `docviewer_circuit_state`; no VIN labels. Grafana dashboard provisioned at <http://localhost:3000> |
| Traces | Jaeger <http://localhost:16686> (obs profile) | One trace per search with the two source calls as parallel child spans; see [docs/diagrams/trace-example.md](docs/diagrams/trace-example.md) |
| Health | `/healthz` (process up), `/readyz` (`Healthy` / `Degraded`) | Docker healthcheck runs `dotnet DocViewer.Api.dll --healthcheck` inside the chiselled image (D-023) |

To follow one request: copy `traceId` from the response (or the `X-Trace-Id` header) and paste it into Jaeger's search box.

## 9. Repository layout

```text
src/
  DocViewer.Domain/          pure rules: Vin, Document, status calculator, merger (no I/O)
  DocViewer.Application/     AggregationService, ports (ISourceAdapter, IDocumentCache, …), metrics, options
  DocViewer.Infrastructure/  Sales/Service adapters, Polly pipelines, cache, EF Core (SQLite/Postgres)
  DocViewer.Api/             Minimal API endpoints, ProblemDetails, Serilog, OpenTelemetry, health
mocks/                       Sales (:8081) and Service (:8082) systems + fault injection
frontend/DocViewer.Web/      Blazor WebAssembly demo client (extra)
tests/                       one test project per production project
docs/                        openapi.json, diagrams
http/docviewer.http          runnable requests
docker/, docker-compose.yml  images and the full stack; infra/ holds Prometheus and Grafana config
```

Dependencies point inwards only: `Api → Application → Domain` and `Infrastructure → Application → Domain`, enforced by project references (invariant 11). Source-specific field names (`docType`, `record_no`, `dd/MM/yyyy`) exist only in `Infrastructure/Sources/` and `mocks/` (invariant 9).

## 10. AI Collaboration Narrative

The whole project was built with AI assistance: a chat assistant for the first-pass plan, and **Claude Code** (agentic, in VS Code) for design review, implementation, tests and documentation. This section explains how that AI was directed, how its output was checked, and where it was wrong. The full, dated evidence is the **AI Corrections Log** in [SYSTEMS.md §4](SYSTEMS.md#4-ai-corrections-log); design-phase detail is in [DESIGN.md §19](DESIGN.md#19-how-genai-was-used-in-the-design-phase).

### 10.1 Strategy: how the AI was directed

- **Written memory instead of chat history.** [AGENTS.md](AGENTS.md) is the agent's operating manual. Every session starts by reading [ROADMAP.md](ROADMAP.md) (what is next, with acceptance criteria) and [SYSTEMS.md](SYSTEMS.md) (decisions D-, errors E-, AI corrections AI-, invariants), and ends by updating them. A new session, or a different agent, picks up exactly where the last one stopped.
- **Small, checkable tasks.** Work is split into roadmap tasks (D1.1 … D5.7), each with acceptance criteria that name the tests that prove it. A task is marked done only when every criterion passes.
- **Ask-first boundaries.** The agent must ask before choosing a framework, changing a status code, a timeout or retry, an invariant, or committing. This rule exists because the agent once picked the tech stack by itself (AI-006).
- **Rules the build enforces.** Nullable reference types, warnings as errors, analyzers, `dotnet format` and an OpenAPI drift check (D-017) turn many AI mistakes into build failures instead of review findings.

### 10.2 Verification: how AI output was checked

| Check | Example |
|---|---|
| Trace every design claim to the brief | The first plan said *"no persistence required"*; the brief says a backend **must** use a persistent database (AI-001 → D-003) |
| Walk failure paths by hand | *"2 s timeout + one retry"* can take 4 s+; changed to one 2 s budget including the retry (AI-003 → D-005) |
| Check library behaviour against docs for the installed version | `AddStandardResilienceHandler` defaults retry 3× within 30 s; rejected (D-014). `HybridCache` would cache partial results; rejected (D-015) |
| Tests over the real exported shape | A log grep "proved" no VINs were logged, but the console formatter hid log scopes; a test over the production JSON formatter found the leak (AI-008, E-007) |
| Investigate a failing test before editing it | Flaky resilience tests were rerun 10× and fixed at the cause (timing and WireMock behaviour), not by loosening assertions (AI-007, E-001) |
| Re-measure coverage after every task | An untested converter quietly dropped Domain coverage below its bar after the task was "done" (AI-007) |
| Run it for real | Every task that touches runtime behaviour was also checked against the running stack (cURL / `Invoke-RestMethod`, Docker, Jaeger) |

### 10.3 Where the AI was wrong (summary)

| ID | What the AI proposed | Why it was wrong | Caught by |
|---|---|---|---|
| AI-001 | No database | Fails a stated requirement | Review against the brief |
| AI-002 | 404 when no documents | Treats a normal outcome as an error | REST review |
| AI-003 | 2 s timeout + retry | Up to 4 s+ worst case | Hand-walked timeline |
| AI-004 | Cache every response | A blip would hide a source for 60 s | Failure matrix |
| AI-005 | VIN rule silent on check digit | Would reject many Indian VINs | Domain review |
| AI-006 | Python/FastAPI, inferred from GitHub history | The role is .NET | Human review mid-scaffold |
| AI-007 | Tests asserting library behaviour that does not exist; flaky timing | Wrong or flaky tests | Failing tests investigated, coverage re-run |
| AI-008 | Log grep as privacy proof | Raw VIN leaked via log scopes and span URLs | Automated test on the real format |
| AI-009 | README listed metric names nobody had checked | Reviewers would look for metrics that do not exist | Every name checked against the running `/metrics` |
| AI-010 | Tests that stayed green with the bug present (a 503 test passing by timeout; a DB-down test that only covered instant refusals) | False confidence; a real outage still broke the Docker healthcheck | Cold-run failure instrumented; real Docker outage |
| AI-011 | A launch profile to switch the Blazor environment | Has no effect in .NET 10 (build-time property) | Response headers, then the SDK's own targets file |
| AI-012 | Generated OpenAPI spec trusted because it cannot drift | It required a field healthy responses omit (E-016) | Contract fuzzing |
| AI-013 | The agent's own Day 4–5 code, all tests green | 9 real bugs (time zone, race, timeout, mock gaps) and a raw VIN in nginx logs (E-017) | Independent code review + checking real container logs |
| AI-014 | "Bounded at ~2.2 s" claimed, tested only for the sources | A stopped Redis or Postgres made searches take ~12 s (E-018, E-019) | Kumar's risk review, measured on the running stack |
| AI-015 | "0 / 10 failures" from a check that could not see failures | The real rate was ~25 % | Printing the actual per-run results |

### 10.4 Quality: what the process produced

- 365 automated tests plus contract fuzzing in CI; coverage Domain 99 %, Application 98 %, Infrastructure 94 %, Api 95 %, Web 91 % (gate: 85 % on Domain + Application).
- Every invariant in SYSTEMS.md §6 is guarded by a tagged test; every row of the failure matrix (DESIGN §6.3) is automated.
- 31 recorded decisions, 21 recorded errors and 15 AI corrections, each dated and linked to code and tests.
- The submission runs from a clean clone with one command and no secrets.

**What I learned about working with an agent:** it is fast and thorough when the target is written down and checkable, and it is most dangerous when it is confident about something nobody checked — a library default, a log format, a stack choice. The process above exists to turn those cases into failing tests or explicit questions as early as possible.
