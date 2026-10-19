// Interface locale de lancement des tests de charge k6 (aucune dépendance npm, Node >= 18).
//
//   node load-tests/ui/server.mjs          puis ouvrir http://127.0.0.1:4300
//
// - lance k6 sur 1 ou plusieurs environnements EN PARALLÈLE (un processus k6 par environnement) ;
// - diffuse les logs en direct (Server-Sent Events) et expose le dashboard web natif de k6 ;
// - affiche les pods et HPA de chaque namespace via kubectl (pour voir le scale-out en direct).
//
// Écoute uniquement sur 127.0.0.1 : cet outil peut générer de la charge sur la prod, il ne doit
// jamais être exposé sur le réseau. Les paramètres sont validés et passés à k6 en tableau d'arguments
// (jamais via un shell), donc aucune injection de commande possible.
import { spawn, execFile } from "node:child_process";
import { createServer } from "node:http";
import { randomUUID } from "node:crypto";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const LOAD_TESTS_DIR = join(HERE, "..");
const PORT = Number(process.env.PORT || 4300);
const K6_BIN = process.env.K6_BIN || "k6";
const KUBECTL_BIN = process.env.KUBECTL_BIN || "kubectl";
const DASHBOARD_BASE_PORT = 5665;
const MONITORING_NAMESPACE = process.env.MONITORING_NAMESPACE || "collector-monitoring";
const PROMETHEUS_LOCAL_PORT = Number(process.env.PROMETHEUS_LOCAL_PORT || 19090);
const GRAFANA_URL = process.env.GRAFANA_URL || "https://grafana.collector.romainmurier.com/d/collector-scalability";
const HEADLAMP_URL = process.env.HEADLAMP_URL || "https://headlamp.collector.romainmurier.com";
const MAX_LOG_LINES = 300;

const ENVIRONMENTS = JSON.parse(await readFile(join(HERE, "environments.json"), "utf8"));
const SCENARIOS = {
  "catalog-read": { label: "Catalogue (GET /articles)", file: "catalog-read.js" },
  browse: { label: "Parcours visiteur (catalogue + détail)", file: "browse.js" },
};
const DURATION = /^\d{1,3}[sm]$/;

/** @type {Map<string, Run>} */
const runs = new Map();
let activeRunId = null;

const json = (res, status, body) => {
  res.writeHead(status, { "Content-Type": "application/json" });
  res.end(JSON.stringify(body));
};

const readBody = (req) =>
  new Promise((resolve, reject) => {
    let data = "";
    req.on("data", (chunk) => {
      data += chunk;
      if (data.length > 10_000) reject(new Error("Corps trop volumineux."));
    });
    req.on("end", () => {
      try {
        resolve(JSON.parse(data || "{}"));
      } catch {
        reject(new Error("JSON invalide."));
      }
    });
  });

/** Valide la requête de lancement ; renvoie les paramètres nettoyés ou lève une Error lisible. */
function validateRunRequest(body) {
  const envs = Array.isArray(body.envs) ? [...new Set(body.envs)] : [];
  if (envs.length === 0) throw new Error("Sélectionnez au moins un environnement.");
  for (const env of envs) {
    if (!ENVIRONMENTS[env]) throw new Error(`Environnement inconnu : ${env}`);
    if (ENVIRONMENTS[env].requireConfirm && body.confirm?.[env] !== env) {
      throw new Error(`Confirmation requise pour « ${env} » : tapez « ${env} » pour valider.`);
    }
  }
  if (!SCENARIOS[body.scenario]) throw new Error("Scénario inconnu.");
  const vus = Number(body.vus);
  if (!Number.isInteger(vus) || vus < 1) throw new Error("Nombre d'utilisateurs virtuels invalide.");
  for (const env of envs) {
    if (vus > ENVIRONMENTS[env].maxVus) {
      throw new Error(`Maximum ${ENVIRONMENTS[env].maxVus} utilisateurs virtuels sur « ${env} ».`);
    }
  }
  if (!DURATION.test(body.ramp) || !DURATION.test(body.hold)) {
    throw new Error("Durées invalides (ex : 30s, 2m).");
  }
  return { envs, scenario: body.scenario, vus, ramp: body.ramp, hold: body.hold, sendMetrics: body.sendMetrics === true };
}

/**
 * @typedef {{ id: string, params: object, startedAt: number, events: object[], clients: Set<import('node:http').ServerResponse>,
 *   procs: Map<string, import('node:child_process').ChildProcess>, remaining: number, stopped: boolean }} Run
 */

function emit(run, event) {
  run.events.push(event);
  if (run.events.length > 2000) run.events.shift();
  for (const client of run.clients) client.write(`data: ${JSON.stringify(event)}\n\n`);
}

async function startEnv(run, env, index, tmpDir) {
  const { scenario, vus, ramp, hold } = run.params;
  const summaryFile = join(tmpDir, `${env}.json`);
  const dashboardPort = DASHBOARD_BASE_PORT + index;

  const args = [
    "run",
    "--no-color",
    "--summary-export", summaryFile,
    "-e", `BASE_URL=${ENVIRONMENTS[env].baseUrl}`,
    "-e", `PEAK_VUS=${vus}`,
    "-e", `RAMP=${ramp}`,
    "-e", `HOLD=${hold}`,
    join(LOAD_TESTS_DIR, SCENARIOS[scenario].file),
  ];
  if (run.metricsUrl) {
    // Métriques k6 -> Prometheus (remote-write) : superposées aux pods dans le dashboard Grafana « Scalabilité ».
    args.splice(1, 0, "--out", "experimental-prometheus-rw", "--tag", `target_env=${env}`, "--tag", `testid=${run.id.slice(0, 8)}`);
  }
  const child = spawn(K6_BIN, args, {
    env: {
      ...process.env,
      ...(run.metricsUrl && {
        K6_PROMETHEUS_RW_SERVER_URL: run.metricsUrl,
        K6_PROMETHEUS_RW_TREND_STATS: "p(95),avg",
        K6_PROMETHEUS_RW_PUSH_INTERVAL: "5s",
      }),
      K6_WEB_DASHBOARD: "true",
      K6_WEB_DASHBOARD_HOST: "127.0.0.1",
      K6_WEB_DASHBOARD_PORT: String(dashboardPort),
    },
  });
  run.procs.set(env, child);
  emit(run, { type: "status", env, status: "running", dashboardUrl: `http://127.0.0.1:${dashboardPort}` });

  let buffer = "";
  const onData = (chunk) => {
    buffer += chunk.toString();
    const lines = buffer.split(/[\r\n]+/);
    buffer = lines.pop() ?? "";
    for (const line of lines) if (line.trim()) emit(run, { type: "log", env, line: line.slice(0, 300) });
  };
  child.stdout.on("data", onData);
  child.stderr.on("data", onData);

  child.on("error", (err) => {
    const hint = err.code === "ENOENT" ? ` — k6 introuvable (« ${K6_BIN} »). Installez-le ou définissez K6_BIN.` : "";
    emit(run, { type: "status", env, status: "error", message: `${err.message}${hint}` });
    finishEnv(run);
  });

  child.on("close", async (code, signal) => {
    let summary = null;
    try {
      const raw = JSON.parse(await readFile(summaryFile, "utf8")).metrics;
      summary = {
        requests: raw.http_reqs?.count,
        rps: raw.http_reqs?.rate,
        avgMs: raw.http_req_duration?.avg,
        p95Ms: raw.http_req_duration?.["p(95)"],
        maxMs: raw.http_req_duration?.max,
        failedRate: raw.http_req_failed?.value ?? raw.http_req_failed?.rate,
      };
    } catch {
      // pas de résumé (arrêt manuel ou k6 planté avant la fin)
    }
    // k6 : 0 = OK, 99 = seuils franchis, autre = erreur d'exécution.
    const status = run.stopped ? "stopped" : code === 0 ? "passed" : code === 99 ? "thresholds-failed" : "error";
    emit(run, { type: "status", env, status, exitCode: code, signal, summary });
    finishEnv(run);
  });
}

function finishEnv(run) {
  run.remaining -= 1;
  if (run.remaining <= 0) {
    run.tunnel?.kill("SIGTERM");
    emit(run, { type: "done" });
    if (activeRunId === run.id) activeRunId = null;
  }
}

/**
 * Tunnel vers le Prometheus du cluster (kubectl port-forward) pour que k6, qui tourne sur CETTE machine,
 * puisse y pousser ses métriques. Un port-forward ne passe pas par les NetworkPolicy : Prometheus reste
 * fermé à tout autre client. Renvoie { url, process } ou { error }.
 */
function openPrometheusTunnel() {
  return new Promise((resolve) => {
    const child = spawn(KUBECTL_BIN, [
      "port-forward", "-n", MONITORING_NAMESPACE, "svc/prometheus", `${PROMETHEUS_LOCAL_PORT}:9090`, "--address", "127.0.0.1",
    ]);
    const timer = setTimeout(() => { child.kill("SIGTERM"); resolve({ error: "délai dépassé (8 s)" }); }, 8000);
    let stderr = "";
    child.stderr.on("data", (c) => { stderr += c; });
    child.stdout.on("data", (chunk) => {
      if (chunk.toString().includes("Forwarding from")) {
        clearTimeout(timer);
        resolve({ url: `http://127.0.0.1:${PROMETHEUS_LOCAL_PORT}/api/v1/write`, process: child });
      }
    });
    child.on("error", (err) => { clearTimeout(timer); resolve({ error: err.code === "ENOENT" ? "kubectl introuvable" : err.message }); });
    child.on("close", () => { clearTimeout(timer); resolve({ error: (stderr.trim().split("\n").pop() || "tunnel fermé").slice(0, 200) }); });
  });
}

async function startRun(params) {
  const run = {
    id: randomUUID(),
    params,
    startedAt: Date.now(),
    events: [],
    clients: new Set(),
    procs: new Map(),
    remaining: params.envs.length,
    stopped: false,
  };
  runs.set(run.id, run);
  activeRunId = run.id;
  const tmpDir = await mkdtemp(join(tmpdir(), "collector-k6-"));
  emit(run, { type: "started", params, startedAt: run.startedAt });
  if (params.sendMetrics) {
    const tunnel = await openPrometheusTunnel();
    if (tunnel.url) {
      run.metricsUrl = tunnel.url;
      run.tunnel = tunnel.process;
      emit(run, { type: "notice", message: "Métriques k6 envoyées à Prometheus (dashboard Grafana « Scalabilité »)." });
    } else {
      emit(run, { type: "notice", message: `Envoi vers Prometheus indisponible (${tunnel.error}) : le test continue sans.` });
    }
  }
  params.envs.forEach((env, index) => startEnv(run, env, index, tmpDir));
  run.cleanup = () => rm(tmpDir, { recursive: true, force: true });
  return run;
}

/** Pods + HPA d'un namespace, simplifiés pour l'affichage. */
function kubectlSnapshot(namespace) {
  return new Promise((resolve) => {
    execFile(
      KUBECTL_BIN,
      ["get", "pods,hpa", "-n", namespace, "-o", "json"],
      { timeout: 8000, maxBuffer: 5_000_000 },
      (err, stdout) => {
        if (err) {
          const message = err.code === "ENOENT" ? "kubectl introuvable sur cette machine." : (err.stderr || err.message).toString().trim().slice(0, 300);
          return resolve({ available: false, message });
        }
        const items = JSON.parse(stdout).items;
        const now = Date.now();
        const pods = items
          .filter((i) => i.kind === "Pod")
          .map((p) => {
            const statuses = p.status.containerStatuses ?? [];
            const waiting = statuses.map((c) => c.state?.waiting?.reason).find(Boolean);
            return {
              name: p.metadata.name,
              service: p.metadata.labels?.["app.kubernetes.io/name"] ?? "?",
              phase: p.metadata.deletionTimestamp ? "Terminating" : (waiting ?? p.status.phase),
              ready: `${statuses.filter((c) => c.ready).length}/${p.spec.containers.length}`,
              restarts: statuses.reduce((n, c) => n + c.restartCount, 0),
              ageSeconds: Math.max(0, Math.round((now - new Date(p.metadata.creationTimestamp).getTime()) / 1000)),
            };
          });
        const hpa = items
          .filter((i) => i.kind === "HorizontalPodAutoscaler")
          .map((h) => ({
            name: h.metadata.name,
            current: h.status.currentReplicas ?? 0,
            desired: h.status.desiredReplicas ?? 0,
            min: h.spec.minReplicas ?? 1,
            max: h.spec.maxReplicas,
            cpuPercent: h.status.currentMetrics?.find((m) => m.resource?.name === "cpu")?.resource.current.averageUtilization ?? null,
          }));
        resolve({ available: true, pods, hpa });
      },
    );
  });
}

const server = createServer(async (req, res) => {
  const url = new URL(req.url, `http://${req.headers.host}`);
  try {
    if (req.method === "GET" && url.pathname === "/") {
      res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
      return res.end(await readFile(join(HERE, "index.html")));
    }

    if (req.method === "GET" && url.pathname === "/api/config") {
      return json(res, 200, {
        environments: ENVIRONMENTS,
        scenarios: Object.fromEntries(Object.entries(SCENARIOS).map(([k, v]) => [k, v.label])),
        activeRunId,
        links: { grafana: GRAFANA_URL, headlamp: HEADLAMP_URL },
      });
    }

    if (req.method === "GET" && url.pathname === "/api/pods") {
      const env = ENVIRONMENTS[url.searchParams.get("env")];
      if (!env) return json(res, 404, { message: "Environnement inconnu." });
      return json(res, 200, await kubectlSnapshot(env.namespace));
    }

    if (req.method === "POST" && url.pathname === "/api/runs") {
      if (activeRunId) return json(res, 409, { message: "Un test est déjà en cours." });
      const params = validateRunRequest(await readBody(req));
      const run = await startRun(params);
      return json(res, 201, { id: run.id });
    }

    const eventsMatch = url.pathname.match(/^\/api\/runs\/([\w-]+)\/events$/);
    if (req.method === "GET" && eventsMatch) {
      const run = runs.get(eventsMatch[1]);
      if (!run) return json(res, 404, { message: "Run inconnu." });
      res.writeHead(200, { "Content-Type": "text/event-stream", "Cache-Control": "no-cache", Connection: "keep-alive" });
      for (const event of run.events.slice(-MAX_LOG_LINES * run.params.envs.length)) res.write(`data: ${JSON.stringify(event)}\n\n`);
      run.clients.add(res);
      req.on("close", () => run.clients.delete(res));
      return;
    }

    const stopMatch = url.pathname.match(/^\/api\/runs\/([\w-]+)\/stop$/);
    if (req.method === "POST" && stopMatch) {
      const run = runs.get(stopMatch[1]);
      if (!run) return json(res, 404, { message: "Run inconnu." });
      run.stopped = true;
      for (const child of run.procs.values()) child.kill("SIGINT"); // k6 s'arrête proprement sur SIGINT
      return json(res, 202, { stopping: true });
    }

    json(res, 404, { message: "Introuvable." });
  } catch (err) {
    json(res, 400, { message: err.message });
  }
});

server.listen(PORT, "127.0.0.1", () => {
  console.log(`Interface de test de charge : http://127.0.0.1:${PORT}`);
  console.log(`k6 : ${K6_BIN} | kubectl : ${KUBECTL_BIN} (KUBECONFIG=${process.env.KUBECONFIG ?? "défaut"})`);
});

process.on("SIGINT", () => {
  for (const run of runs.values()) {
    for (const child of run.procs.values()) child.kill("SIGINT");
    run.tunnel?.kill("SIGTERM");
  }
  process.exit(0);
});
