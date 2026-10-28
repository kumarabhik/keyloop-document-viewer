# Demo UI — Blazor WebAssembly

A small client for the Unified Document Viewer API. It is an **extra**: the backend is the graded layer (D-001). Built in C# with Blazor WebAssembly so the whole repository is one language and needs no Node.js (D-025).

## Run it

| How | Command | Open |
|---|---|---|
| With Docker (whole stack) | `docker compose up --build -d` (from the repo root) | <http://localhost:5173> |
| Against a locally running API | `dotnet run --project frontend/DocViewer.Web` | <http://localhost:5173> |
| **Mock mode — no backend at all** | `dotnet run --project frontend/DocViewer.Web -p:WasmApplicationEnvironmentName=Mock` | <http://localhost:5173> |

Mock mode loads [`wwwroot/appsettings.Mock.json`](DocViewer.Web/wwwroot/appsettings.Mock.json). In .NET 10 the WebAssembly environment is fixed at build time by the `WasmApplicationEnvironmentName` property (default `Development`), so a launch-profile environment variable has no effect. Stop the Docker `web` container first if it holds port 5173 (`docker compose stop web`).

The API address and the mocks' fault-switch addresses are in [`DocViewer.Web/wwwroot/appsettings.json`](DocViewer.Web/wwwroot/appsettings.json). The API allows the UI's origin (`http://localhost:5173`) through CORS.

## What it shows

| Feature | Where |
|---|---|
| VIN box with live hints (same rules as the API: 17 characters, no I/O/Q), upper-casing, recent searches | top of the page |
| Deep links: `/?vin=MA3EWB81S00100001`, with filters `&source=SALES&type=PUC&status=EXPIRED` | the URL |
| One merged table; a coloured **source badge** on every row; status chips (*Expired*, *Expiring soon · expires in 10 days*); ⚠ conflict and ⧉ possible-duplicate markers with tooltips | results |
| Expiry alert: "1 expired, 1 expiring in 10 days", with buttons that filter the table | above the table |
| **Partial** banner naming the system that failed; **stale** banner with the time of the saved copy | above the table |
| Loading skeleton, empty state, 400 hint, 503 screen with *Try again*, trace id for support | in place of the table |
| Source health: circuit state, last call, latency, and **demo switches** that make a mock system fail | bottom panel |

## Mock mode demo VINs

Mock mode answers every request from an in-process handler ([`Api/MockApiHandler.cs`](DocViewer.Web/Api/MockApiHandler.cs)), the role MSW plays in a JavaScript app. The fault switches in the health panel work in mock mode too; after you set one back to *Healthy*, the circuit shows *Half-open* until the next search.

| VIN | State |
|---|---|
| `MA3EWB81S00100001` | Complete: 3 Sales + 3 Service documents |
| `MA3EWB81S00100004` | Empty: no documents anywhere |
| `MA3EWB81S00100005` | 1 expired, 1 expiring in 10 days |
| `MA3EWB81S00100006` | Possible duplicate across systems |
| `MA3EWB81S00100007` | Conflict across systems |
| `MA3EWB81S00100011` | Partial: Service timed out |
| `MA3EWB81S00100012` | Stale: Service served from its saved copy |
| `MA3EWB81S00100013` | 503: both systems down |
| `MA3EWB81S00100014` | Service circuit open: "paused after repeated failures" |
| any VIN, searched twice within 60 s | Second answer is a cache `HIT` ("From cache") |
| `BADVIN` | Invalid: hint only, no request sent |

## Tests

```bash
dotnet test tests/DocViewer.Web.Tests
```

bUnit renders the real components and the whole page against mock mode: every state above, URL-synced filters, the expiry alert, recent searches and the fault switches. `WireContractTests` fails if the client's response records drift from [`docs/openapi.json`](../docs/openapi.json).
