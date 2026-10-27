# Example trace (Jaeger, docker compose --profile obs)

Captured 2026-10-27 from Jaeger's query API for one search with `?refresh=true`.
`source.sales` and `source.service` start within ~1 ms of each other under `aggregate`: the fan-out is parallel.
VINs in URLs are masked by `VinRedactionProcessor`; the Service query string is redacted by the HttpClient instrumentation.

```text
GET /api/v1/vehicles/{vin}/documents  start+0.0ms  dur=21.8ms  url.path=/api/v1/vehicles/MA3**********0002/documents
  aggregate                           start+0.4ms  dur=20.3ms
    source.sales                      start+0.6ms  dur=6.0ms   status=Ok
      GET                             start+1.1ms  dur=3.9ms   http://sales-mock:8080/sales/v1/vehicles/MA3**********0002/documents
    source.service                    start+1.8ms  dur=14.7ms  status=Ok
      GET                             start+2.2ms  dur=12.2ms  http://service-mock:8080/service/api/records?*
    merge                             start+17.1ms dur=0.7ms
    snapshot.persist (x2)             start+20.2ms             (EF Core spans below, after the response)
    audit.write                       start+20.8ms
```

A screenshot of the same trace in the Jaeger UI (http://localhost:16686) is still to be added for the video (ROADMAP D3.3).
