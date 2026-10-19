// Montée en charge sur le catalogue public (lecture seule : GET /articles ne passe que par Catalog.Api).
// Objectif de démo : voir le HPA de Catalog passer de 1 à 3 pods dans Grafana pendant la rampe.
//
//   k6 run -e BASE_URL=https://dev.collector.romainmurier.com load-tests/catalog-read.js
//
// Profil ajustable : -e PEAK_VUS=60 -e RAMP=1m -e HOLD=3m (ou via l'interface : load-tests/ui)
import http from "k6/http";
import { check, sleep } from "k6";

const BASE_URL = __ENV.BASE_URL || "https://dev.collector.romainmurier.com";
const PEAK_VUS = Number(__ENV.PEAK_VUS || 40);
const RAMP = __ENV.RAMP || "1m";
const HOLD = __ENV.HOLD || "2m";

export const options = {
  stages: [
    { duration: "30s", target: Math.ceil(PEAK_VUS / 4) }, // échauffement
    { duration: RAMP, target: PEAK_VUS },                 // rampe -> déclenche l'autoscaling
    { duration: HOLD, target: PEAK_VUS },                 // palier : les pods supplémentaires démarrent
    { duration: "30s", target: 0 },                       // descente
  ],
  // Les seuils font échouer k6 (code de sortie 99) : réutilisable comme gate de non-régression.
  thresholds: {
    http_req_failed: ["rate<0.01"],
    http_req_duration: ["p(95)<500"],
    checks: ["rate>0.99"],
  },
};

export default function () {
  const res = http.get(`${BASE_URL}/api/catalog/articles`);
  check(res, {
    "status 200": (r) => r.status === 200,
    "réponse JSON": (r) => (r.headers["Content-Type"] || "").includes("application/json"),
  });
  sleep(0.2);
}
