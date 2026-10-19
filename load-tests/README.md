# Tests de charge (k6)

## Interface de lancement

```bash
node load-tests/ui/server.mjs        # puis ouvrir http://127.0.0.1:4300
```

Prérequis sur la machine qui lance l'interface : **Node ≥ 18**, **k6** ([installation](https://grafana.com/docs/k6/latest/set-up/install-k6/))
et, pour le panneau « Pods & autoscaling », **kubectl** avec un kubeconfig qui atteint le cluster (`KUBECONFIG=...`).
Sans kubectl, les tests de charge fonctionnent quand même : seuls le panneau pods et l'envoi des métriques vers Prometheus sont indisponibles.

L'interface permet de :
- cocher **un ou plusieurs environnements** (dev / uat / prod) : un processus k6 par environnement, en parallèle ;
- choisir le scénario, le nombre d'utilisateurs virtuels, la durée de montée en charge et le palier (+ 3 préréglages) ;
- suivre les logs, le statut et le résumé final (req/s, p95, taux d'erreur) de chaque environnement ;
- ouvrir le **dashboard web natif de k6** (graphiques en direct) via le lien affiché par environnement ;
- **voir les pods se créer** : tableau pods + barre HPA par namespace, rafraîchi toutes les 2 s, nouveaux pods surlignés ;
- **envoyer les métriques k6 à Prometheus** (case cochée par défaut) : l'interface ouvre un `kubectl port-forward` vers le Prometheus du
  cluster (Prometheus reste fermé à tout autre client) et k6 y pousse ses métriques en `remote-write`. Elles apparaissent dans le
  dashboard Grafana **« Collector — Scalabilité »** (lien en haut de l'interface), superposées à l'état des pods et du HPA.
  Si le tunnel échoue, le test continue sans et l'interface l'indique.

### Écran de démo recommandé
1. **Grafana « Scalabilité »** : utilisateurs virtuels k6 → CPU rapporté au `requests` (avec le seuil de 70 %) → réplicas demandés par le HPA
   → réplicas prêts → pods par état → latence p95. La causalité se lit de haut en bas.
2. **Headlamp** (`https://headlamp.collector.romainmurier.com`) : pods, HPA et événements Kubernetes (`Scheduled`, `Pulling`, `Started`) en direct.
   Connexion avec le jeton en lecture seule : `kubectl -n collector-monitoring get secret headlamp-viewer-token -o jsonpath='{.data.token}' | base64 -d`.
3. Le panneau pods de cette interface comme vue de secours (ne dépend ni de Grafana ni de Headlamp).

Garde-fous : écoute sur `127.0.0.1` uniquement, un seul test à la fois, plafond d'utilisateurs virtuels par environnement
(`ui/environments.json`), confirmation à taper pour la **prod** (plafonnée à 20), paramètres validés et passés à k6 sans shell.

Variables : `PORT` (défaut 4300), `K6_BIN`, `KUBECTL_BIN`, `KUBECONFIG`, `GRAFANA_URL` et `HEADLAMP_URL` (liens affichés en haut),
`MONITORING_NAMESPACE` (défaut `collector-monitoring`) et `PROMETHEUS_LOCAL_PORT` (port local du tunnel, défaut 19090).

## Scénarios

| Fichier | Contenu |
|---|---|
| `catalog-read.js` | `GET /api/catalog/articles` en boucle (lecture seule) — déclenche le HPA de Catalog |
| `browse.js` | Catalogue puis détail d'un article au hasard |

Seuils (échec du run si dépassés) : p95 < 500 ms (800 ms pour `browse`), erreurs < 1 %.
**On ne charge jamais le Checkout Stripe** : il est limité en débit côté Stripe et chaque appel crée des objets réels dans le compte de test.

## Ligne de commande (sans interface)

```bash
k6 run -e BASE_URL=https://dev.collector.romainmurier.com -e PEAK_VUS=40 -e RAMP=1m -e HOLD=2m load-tests/catalog-read.js
kubectl -n collector-dev get hpa collector-catalog -w
```

Le HPA de Catalog n'est actif qu'en **dev** (`values-dev.yaml`), avec des `requests` CPU volontairement
minuscules pour déclencher le scale-out sans saturer le serveur ; prod garde ses vraies ressources.
