// Parcours visiteur : catalogue, puis détail d'un article au hasard. Lecture seule (aucune écriture,
// aucun appel Stripe : on ne charge jamais le Checkout, il est rate-limité côté Stripe).
//
//   k6 run -e BASE_URL=https://dev.collector.romainmurier.com load-tests/browse.js
import http from "k6/http";
import { check, sleep } from "k6";

const BASE_URL = __ENV.BASE_URL || "https://dev.collector.romainmurier.com";
const PEAK_VUS = Number(__ENV.PEAK_VUS || 30);
const RAMP = __ENV.RAMP || "1m";
const HOLD = __ENV.HOLD || "2m";

export const options = {
  stages: [
    { duration: RAMP, target: PEAK_VUS },
    { duration: HOLD, target: PEAK_VUS },
    { duration: "20s", target: 0 },
  ],
  thresholds: {
    http_req_failed: ["rate<0.01"],
    http_req_duration: ["p(95)<800"],
  },
};

export default function () {
  const list = http.get(`${BASE_URL}/api/catalog/articles`);
  check(list, { "catalogue 200": (r) => r.status === 200 });

  const articles = list.status === 200 ? list.json() : [];
  if (articles.length > 0) {
    const pick = articles[Math.floor(Math.random() * articles.length)];
    const detail = http.get(`${BASE_URL}/api/catalog/articles/${pick.id}`);
    check(detail, { "détail 200": (r) => r.status === 200 });
  }
  sleep(0.5);
}
