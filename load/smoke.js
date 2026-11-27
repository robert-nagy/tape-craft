// k6 smoke test. Set TARGET_URL to the deployed app to exercise it;
// without it the script only checks that k6 runs.
import http from 'k6/http';
import { check } from 'k6';

export const options = { vus: 1, iterations: 1 };

export default function () {
  const target = __ENV.TARGET_URL;
  if (!target) return;
  const res = http.get(target);
  check(res, { 'status is 2xx': (r) => r.status >= 200 && r.status < 300 });
}
