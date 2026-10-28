// D5.4 load test. Run against the compose stack:  docker compose --profile load run --rm k6
//
// Phase "mixed"   — 50 virtual users for 2 minutes over the 10 seed VINs, ~10 % forced cache misses.
//                   Targets from DESIGN.md §2.2: p95 < 20 ms on a cache hit, p95 < 300 ms on a miss.
// Phase "breaker" — the Service mock hangs; one user searches repeatedly. Every answer must still be 200
//                   within the ~2.2 s budget (invariants 1 and 5), and once the breaker opens (D-014) the
//                   calls must be fast and report Service as circuit_open.
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

const API = __ENV.API_URL || 'http://localhost:8080';
const SALES = __ENV.SALES_URL || 'http://localhost:8081';
const SERVICE = __ENV.SERVICE_URL || 'http://localhost:8082';
const DURATION = __ENV.DURATION || '2m';
const VINS = Array.from({ length: 10 }, (_, i) => `MA3EWB81S001000${String(i + 1).padStart(2, '0')}`);

const hitMs = new Trend('search_hit_ms', true);
const missMs = new Trend('search_miss_ms', true);
const partial = new Rate('search_partial');
const breakerWithinBudget = new Rate('breaker_within_budget');
const openFastMs = new Trend('breaker_open_ms', true);
const openSeen = new Counter('breaker_open_responses');

export const options = {
  scenarios: {
    mixed: { executor: 'constant-vus', vus: Number(__ENV.VUS || 50), duration: DURATION, exec: 'mixed' },
    breaker: { executor: 'per-vu-iterations', vus: 1, iterations: 1, exec: 'breaker', startTime: addSeconds(DURATION, 5), maxDuration: '2m' },
  },
  thresholds: {
    'http_req_failed{scenario:mixed}': ['rate<0.01'],
    'checks{scenario:mixed}': ['rate>0.99'],
    search_hit_ms: ['p(95)<20'],
    search_miss_ms: ['p(95)<300'],
    search_partial: ['rate<0.01'], // healthy sources: nothing should be partial
    breaker_within_budget: ['rate==1'],
    breaker_open_responses: ['count>=5'],
    breaker_open_ms: ['p(95)<100'],
  },
};

export function setup() {
  setFault(SALES, 'none');
  setFault(SERVICE, 'none');
  for (const vin of VINS) http.get(`${API}/api/v1/vehicles/${vin}/documents`); // warm connections and cache
}

export function mixed() {
  const vin = VINS[Math.floor(Math.random() * VINS.length)];
  const refresh = Math.random() < 0.1;
  const res = http.get(`${API}/api/v1/vehicles/${vin}/documents${refresh ? '?refresh=true' : ''}`, { tags: { name: 'search' } });

  check(res, { 'status is 200': (r) => r.status === 200 });
  if (res.status === 200) {
    (res.headers['X-Cache'] === 'HIT' ? hitMs : missMs).add(res.timings.duration);
    partial.add(res.json('partial') === true);
  }
  sleep(0.5);
}

export function breaker() {
  setFault(SERVICE, 'timeout');
  try {
    for (let i = 0; i < 25; i++) {
      const res = http.get(`${API}/api/v1/vehicles/${VINS[0]}/documents?refresh=true`, { tags: { name: 'search-breaker' } });
      const service = res.status === 200 ? res.json('sources').find((s) => s.name === 'SERVICE') : null;
      // 2.2 s budget (D-005) plus a little for Docker networking on a laptop.
      breakerWithinBudget.add(res.status === 200 && res.timings.duration < 2500);
      // With a saved copy the source is "stale" and keeps the reason in `error` (D-021); without one it is "circuit_open".
      if (service && (service.status === 'circuit_open' || service.error === 'circuit_open')) {
        openSeen.add(1);
        openFastMs.add(res.timings.duration);
      }
      console.log(`breaker #${i + 1}: HTTP ${res.status} ${Math.round(res.timings.duration)} ms service=${service ? `${service.status}/${service.error}` : '-'}`);
    }
  } finally {
    setFault(SERVICE, 'none');
  }
}

export function teardown() {
  setFault(SALES, 'none');
  setFault(SERVICE, 'none');
}

function setFault(baseUrl, mode) {
  http.post(`${baseUrl}/__admin/fault`, JSON.stringify({ mode }), { headers: { 'Content-Type': 'application/json' }, tags: { name: 'admin' } });
}

// "2m", "90s", "5m30s", "1h": the breaker phase must start only after the mixed phase has ended.
function addSeconds(duration, extra) {
  if (!/^(\d+h)?(\d+m)?(\d+s)?$/.test(duration) || duration === '') {
    throw new Error(`DURATION must look like 2m, 90s, 5m30s or 1h; got "${duration}"`);
  }
  const unit = { h: 3600, m: 60, s: 1 };
  let seconds = 0;
  for (const [, n, u] of duration.matchAll(/(\d+)([hms])/g)) seconds += Number(n) * unit[u];
  return `${seconds + extra}s`;
}
