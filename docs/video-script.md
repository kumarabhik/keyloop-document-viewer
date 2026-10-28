# Video script — Unified Document Viewer (target 9 minutes, limit 5–10)

Recording notes for Kumar (D5.7). Each segment lists what is **on screen**, what to **say** (in your own words; the lines are a guide, not a teleprompter), and the exact commands. Timings add up to 9:00 and leave a minute of slack.

## Before you record (10 minutes)

| Step | Command / action | Check |
|---|---|---|
| 1. Free the CPU | Stop anything heavy, e.g. the minikube cluster | Task Manager is quiet |
| 2. Fresh stack | `docker compose --profile obs down -v` then `docker compose --profile obs up --build -d` | `docker compose ps` shows `api` **healthy** |
| 3. Warm it up | `Invoke-RestMethod http://localhost:8080/api/v1/vehicles/MA3EWB81S00100001/documents \| Out-Null` | Avoids a cold first call on camera (E-010) |
| 3b. Keep one VIN unsearched | Do **not** search `MA3EWB81S00100010` before the 503 step | Once searched, a saved copy exists and the 503 becomes a stale 200 (which is correct, just not the demo) |
| 4. Heal the mocks | `Invoke-RestMethod -Method Post http://localhost:8082/__admin/fault -ContentType 'application/json' -Body '{"mode":"none"}'` (and :8081) | Both mocks healthy |
| 5. Open tabs | <http://localhost:5173> · <http://localhost:16686> (Jaeger) · <http://localhost:3000> (Grafana) · this repo in VS Code with `DESIGN.md`, `SYSTEMS.md` §4, `AggregationService.cs`, `SourcePipeline.cs` | — |
| 6. Terminal | Windows PowerShell, font size 16+, `cd` to the repo | — |
| 7. Screen | 1920×1080, notifications off, browser zoom 110 % | — |

Helper for the fault demo (paste once at the start):

```powershell
function Set-Fault($port, $mode) {
  Invoke-RestMethod -Method Post "http://localhost:$port/__admin/fault" -ContentType 'application/json' -Body (@{ mode = $mode } | ConvertTo-Json) | Out-Null
}
```

---

## 1. Intro and scenario — 0:00–0:30

**On screen:** README top section.

**Say:** "I'm Kumar. I chose Scenario D, the Unified Document Viewer. A service advisor has one VIN and two systems, Sales and Service, which disagree on formats and fail independently. I built the backend fully in .NET 10, with a small Blazor client as an extra. One search calls both systems in parallel and returns one list where every document says where it came from. When one system is down, you still get the other's documents, clearly marked."

## 2. Design walkthrough — 0:30–2:30

**On screen:** `DESIGN.md` §4 diagram, then §6.3 failure matrix, then §7.2 response.

**Say, in this order:**

1. **Layers** (§4): Domain is pure rules (VIN, status, duplicate/conflict), Application orchestrates, Infrastructure has one adapter per source, Api is thin. Project references enforce the direction (invariant 11).
2. **The fan-out**: `Task.WhenAll` over two adapters that never throw; each failure becomes a value (`SourceResult`).
3. **The budget** (D-005): 2 s per source *including* the one retry, no retry after a timeout, so the worst case is about 2.2 s. "The first AI plan said 2 s plus a retry, which is 4 s or more; I caught that by walking the timeline."
4. **Failure matrix** (§6.3): one down → 200 partial; both down → last saved copy (stale, labelled with its time) or 503; no documents → 200 with an empty list, not 404.
5. **Cache and persistence**: only complete answers are cached, 60 s (D-006). Postgres keeps the last good copy per source (stale fallback) and an audit row with a hashed VIN, which is the brief's persistent database with a real job.
6. **Duplicates and conflicts** are flagged, never merged (D-009), so no data is ever hidden.

## 3. Implementation highlights — 2:30–4:00

**On screen:** code, briefly each.

| Show | Point to make |
|---|---|
| `src/DocViewer.Infrastructure/Resilience/SourcePipeline.cs` | Polly v8 per source: total timeout → retry (only connect errors and 502/503/504) → breaker → attempt timeout. The default `ShouldHandle` would retry timeouts, so it is overridden. |
| `src/DocViewer.Application/Documents/AggregationService.cs` | Cache → fan-out → merge → stale fallback → persist. Concurrent searches for one VIN share one fan-out (D-020). |
| `src/DocViewer.Infrastructure/Sources/Service/ServiceAdapter.cs` | `snake_case`, `dd/MM/yyyy`, a malformed record is skipped and counted. Source quirks live only here (invariant 9). |
| `tests/` folder, then `dotnet test` result | 331 tests: WireMock for real HTTP failures, `FakeTimeProvider` for time, real PostgreSQL via Testcontainers, every invariant tagged. CI also fuzzes the API against its OpenAPI contract. |

**Say:** "Warnings are errors and nullable is on, so a lot of AI mistakes fail the build instead of reaching review."

## 4. The AI collaboration story — 4:00–5:30

**On screen:** `AGENTS.md` §0–§2, then `SYSTEMS.md` §4 (AI Corrections Log), then README §10.3 table.

**Say:**

1. **How I directed it:** Claude Code works from written files, not chat memory. `AGENTS.md` is its operating manual; `ROADMAP.md` has tasks with acceptance criteria; `SYSTEMS.md` holds decisions, errors and AI corrections. Every session starts by reading them and ends by updating them, which is why work can stop in the afternoon and resume in the evening.
2. **Where it was wrong** (pick three, about 20 s each):
   - **AI-001**: "no persistence needed", which contradicted the brief.
   - **AI-008**: it "proved" no VINs were logged with a grep, but the console format hid log scopes; a test on the real JSON found the leak.
   - **AI-012/E-016**: the generated OpenAPI spec was trusted because it "cannot drift", but it required a field healthy responses leave out; contract fuzzing found it.
3. **How I verified:** run it for real, check failure paths, read library docs for the installed version, investigate failing tests before touching them, re-run coverage after every task.
4. "The log has 13 entries. I kept the embarrassing ones on purpose; that's the evidence."

## 5. Live demo with failure toggle — 5:30–8:00

**On screen:** the UI at <http://localhost:5173>, then PowerShell, then Jaeger.

| Time | Action | What the viewer sees | Say |
|---|---|---|---|
| 5:30 | Type `ma3ewb81s0010000o` slowly | Live hint: "cannot contain 'O'"; the button stays disabled; the input upper-cases | "Same VIN rule as the server, from the shared Domain project; an invalid VIN never reaches a source." |
| 5:50 | Search `MA3EWB81S00100005` | Merged table, Sales/Service badges, "1 expired, 1 expiring in 10 days" | "Status is computed in the dealership's time zone." |
| 6:10 | Click **Show expired**, then copy the URL | Table filters; URL has `&status=EXPIRED` | "Filters live in the URL, so a link can be shared." |
| 6:20 | Search `MA3EWB81S00100007` | Two warranty rows marked ⚠ Conflict | "Both kept, both flagged." |
| 6:35 | Search `MA3EWB81S00100001`, then in the **Source health** panel set Service to *Hang (timeout)*, then **Refresh** | Yellow banner: Service did not answer; showing its saved copy from <time> | "That took about 1.5 s: one attempt timeout, no retry. The user still has everything, with a warning." |
| 7:00 | Press **Refresh** five or six more times | Answers become instant; the health panel shows the circuit **Open** | "After repeated failures the breaker stops calling Service for 30 s and answers at once." |
| 7:15 | PowerShell: `Set-Fault 8081 error`, then `curl.exe -s -i http://localhost:8080/api/v1/vehicles/MA3EWB81S00100010/documents` | `503`, `Retry-After: 5`, problem details naming both failures | "Only when nothing answered and there is no saved copy." |
| 7:35 | Copy the `traceId` from a response into Jaeger | One trace: two source calls side by side | "Every request is traceable end to end; the VIN is masked in spans and logs." |
| 7:55 | `Set-Fault 8081 none; Set-Fault 8082 none` | — | Heal before you move on. |

If something goes wrong on camera, keep going and say what you see; the partial and stale states are the point of the scenario.

## 6. Learnings and challenges — 8:00–9:00

**On screen:** `SYSTEMS.md` §2 (Error Log) list, then the README.

**Say:**

1. "The hardest bugs were timing on cold starts (E-001, E-013) and privacy leaks in places I didn't write, like log scopes and nginx (E-007, E-017). The fix was to test what actually leaves the process."
2. "One open item, stated honestly: under 50 users on my laptop, cache hits miss their p95 target (E-014), probably CPU contention on a shared machine. The breaker and miss targets pass."
3. "What I'd do next: shared breaker state across replicas, auth with dealer scoping, pagination for very long histories."
4. "Working with an agent is fast when the target is written down and checkable. It is most dangerous when it is confident about something nobody checked. Most of this process exists to catch that."

---

## After recording

- Watch it once at 1× and check the timing is under 10:00.
- Upload (unlisted) and put the link at the top of the README.
- Mark D5.7 done in `ROADMAP.md`.
