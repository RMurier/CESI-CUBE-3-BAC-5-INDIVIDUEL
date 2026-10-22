# Chart Helm `collector`

Un seul chart, une valeur par environnement. Déployer sur dev/uat/prod ne change **aucun template**
— uniquement le fichier `values-<env>.yaml` et le tag d'image (`--set global.imageTag=...`).

Ce chart ne contient que **l'application**. Le monitoring (`infra/prometheus`, `infra/kube-state-metrics`, `infra/grafana`,
`infra/headlamp`) et les DAGs Airflow (`infra/airflow`) ont chacun leur dossier, déployés en parallèle par `pipeline.yml`.

## Prérequis sur le cluster K3s (à faire une fois, hors chart)

1. **Traefik** — déjà fourni par défaut par k3s (`--disable traefik` NON utilisé à l'installation).
2. **cert-manager** avec un `ClusterIssuer` nommé `letsencrypt` (Let's Encrypt via challenge HTTP01 ou
   DNS01 selon le provider DNS) — requis par `templates/ingressroute.yaml`.
3. **Infisical Secrets Operator** installé au niveau cluster — requis par `templates/infisical-secret.yaml`.
   Voir https://infisical.com/docs/integrations/platforms/kubernetes (à revérifier, l'opérateur évolue vite).
4. Une Machine Identity Infisical (Universal Auth) existante, avec son `clientId`/`clientSecret` — le
   Secret K8s `infisical-machine-identity` qui authentifie l'opérateur dans chaque namespace est
   provisionné **automatiquement à chaque déploiement Helm** par `templates/infisical-machine-identity.yaml`,
   alimenté par `pipeline.yml` via `--set-string infisical.machineIdentity.clientId/clientSecret` depuis
   les GitHub Secrets `INFISICAL_CLIENT_ID`/`INFISICAL_CLIENT_SECRET` — plus de `kubectl create
   secret` manuel, et changer de serveur (nouveau `KUBE_CONFIG_B64`) n'exige aucune étape
   supplémentaire : le prochain déploiement recrée tout.

## Clés attendues dans Infisical (environnement dev/uat/prod)

Toutes synchronisées automatiquement vers le Secret K8s `collector-secrets` par l'opérateur Infisical :

**Un mot de passe `sa` distinct par service** (pas une valeur partagée entre les 5 instances SQL
Server) : une base compromise ne donne pas accès aux 4 autres. Chaque `ConnectionStrings__<service>`
doit utiliser le `MSSQL_SA_PASSWORD__<service>` **du même service**, jamais celui d'un autre.

| Clé | Utilisée par |
|---|---|
| `Jwt__SigningKey` | Identity, Catalog, Chat (vérification JWT) |
| `MSSQL_SA_PASSWORD__identity` | StatefulSet `collector-identity-sql` |
| `ConnectionStrings__identity` | Identity.Api (`Server=collector-identity-sql,1433;Database=identity;User Id=sa;Password=<valeur de MSSQL_SA_PASSWORD__identity>;TrustServerCertificate=True`) |
| `MSSQL_SA_PASSWORD__catalog` | StatefulSet `collector-catalog-sql` |
| `ConnectionStrings__catalog` | Catalog.Api (idem avec `collector-catalog-sql` / `Database=catalog` / `MSSQL_SA_PASSWORD__catalog`) |
| `MSSQL_SA_PASSWORD__chat` | StatefulSet `collector-chat-sql` |
| `ConnectionStrings__chat` | Chat.Api (idem `collector-chat-sql` / `Database=chat` / `MSSQL_SA_PASSWORD__chat`) |
| `MSSQL_SA_PASSWORD__notification` | StatefulSet `collector-notification-sql` |
| `ConnectionStrings__notification` | Notification.Api (idem `collector-notification-sql` / `Database=notification` / `MSSQL_SA_PASSWORD__notification`) |
| `MSSQL_SA_PASSWORD__moderation` | StatefulSet `collector-moderation-sql` |
| `ConnectionStrings__moderation` | Moderation.Api (idem `collector-moderation-sql` / `Database=moderation` / `MSSQL_SA_PASSWORD__moderation`) |
| `RabbitMq__UserName` / `RabbitMq__Password` | RabbitMQ (StatefulSet) + tous les services consommateurs |
| `Stripe__SecretKey` | Catalog (création des sessions Stripe Checkout) — `sk_test_...` en dev/uat. Absente = paiement désactivé (`POST /orders` répond 503). **Connect doit être activé** sur le compte Stripe (onboarding vendeur), le service démarre quand même |
| `Maintenance__ApiKey` | Identity, Catalog (endpoints `/internal/*` appelés par Airflow) — une valeur par environnement, identique à la Variable Airflow `collector_<env>_maintenance_key`. Absente = endpoints désactivés (404) |
| `Stripe__WebhookSecret` | Catalog (vérification de la signature des webhooks) — `whsec_...` du endpoint `https://<domaine>/api/catalog/webhooks/stripe` créé dans le dashboard Stripe. **Un endpoint (donc un `whsec_`) par environnement** : chaque scope Infisical porte le sien |
| `Omnia__ProjectId` | Les 5 services — **même valeur dans les 3 environnements** (GUID du projet "Collector" enregistré sur omnia-monitoring.com) |
| `Omnia__Version` / `Omnia__Secret` | Les 5 services — **paire cohérente et DIFFÉRENTE par environnement** (rotation de clé Omnia : chaque version de secret a son propre numéro de version, envoyé dans `X-Key-Version` et inclus dans la signature HMAC). Ex. dev en v5 avec le secret v5, prod en v3 avec le secret v3 — mélanger la version d'un environnement avec le secret d'un autre fait rejeter la signature côté Omnia. Absentes = `IOmniaClient` ne fait rien silencieusement (pas d'exception, pas de blocage) |

**`KUBE_CONFIG_B64`, `INFISICAL_CLIENT_ID` et `INFISICAL_CLIENT_SECRET` ne sont volontairement PAS
dans cette liste** : ce sont des secrets d'infra CI/CD, pas des secrets applicatifs — même cluster
physique pour les 3 environnements, donc une seule valeur de chaque, en secrets de **repo** GitHub
(`Settings > Secrets and variables > Actions > Repository secrets`), lus directement par `pipeline.yml`.
Le pipeline CI/CD ne lit jamais les secrets applicatifs d'Infisical ; il se contente de
provisionner le Secret K8s qui permet à l'opérateur Infisical (dans le cluster) de les récupérer
lui-même (voir `templates/infisical-machine-identity.yaml`).

## Déploiement manuel (hors CI, pour test local)

```bash
helm upgrade --install collector infra/helm/collector \
  -f infra/helm/collector/values-dev.yaml \
  --namespace collector-dev --create-namespace \
  --set global.imageTag=latest \
  --set-string infisical.machineIdentity.clientId=<...> \
  --set-string infisical.machineIdentity.clientSecret=<...> \
  --kubeconfig <chemin-vers-kubeconfig>
```

## Validation sans cluster

```bash
helm lint infra/helm/collector -f infra/helm/collector/values-dev.yaml
helm template collector infra/helm/collector -f infra/helm/collector/values-dev.yaml --namespace collector-dev
```


## Monitoring (quatre charts séparés)

Prometheus, kube-state-metrics, Grafana et Headlamp ont chacun leur dossier et leur chart Helm, déployés par 4 jobs séparés
(`deploy-prometheus`, `deploy-kube-state-metrics`, `deploy-grafana`, `deploy-headlamp`) de `pipeline.yml` (en parallèle de
deploy-dev/uat/prod, mêmes conditions de déclenchement : push `develop` -> dev, push `master` -> uat, release publiée ->
production avec approbation reviewer) à **chaque push/release** (comme l'application) : modifier `infra/prometheus/files/prometheus.yml`
ou un dashboard de `infra/grafana/dashboards/` puis pousser suffit, le pod redémarre seul. Une seule stack, namespace
`collector-monitoring`, qui surveille les 3 environnements.

Prérequis manuels (une fois) :
- DNS `grafana.collector.romainmurier.com` et `headlamp.collector.romainmurier.com` (A -> IP du serveur, modifiables via `host` dans `infra/grafana/values.yaml` et `infra/headlamp/values.yaml`) ; chaque chart crée son certificat Let's Encrypt, sa route HTTPS Traefik et la redirection HTTP -> HTTPS ;
- dans **Infisical** (même projet que l'application, environnement `prod`, dossier `/monitoring`) : `GRAFANA_ADMIN_PASSWORD`
  (obligatoire) et `GRAFANA_ADMIN_USER` (optionnel, défaut `admin`). Sans la première, le pod Grafana reste en attente du Secret ;
- la Machine Identity Infisical doit avoir accès à ce dossier (ce sont les mêmes `INFISICAL_CLIENT_ID/SECRET` que l'application).
- Headlamp (interface Kubernetes en lecture seule, `infra/headlamp`) : connexion avec un jeton
  `view` (aucun droit d'écriture, pas d'accès aux Secrets) :
  `kubectl -n collector-monitoring get secret headlamp-viewer-token -o jsonpath='{.data.token}' | base64 -d`.
  Pour restreindre l'accès à ton IP : `allowedCidrs` dans `infra/headlamp/values.yaml` (Traefik v3).

La stack de monitoring comporte quatre charts, tous déployés par le même job : `infra/prometheus`, `infra/kube-state-metrics`
(état des pods, des réplicas et du HPA), `infra/grafana` (dashboards « vue d'ensemble » et « Scalabilité ») et `infra/headlamp`.
Prometheus scrape aussi cAdvisor (CPU/mémoire réels des conteneurs) et accepte le `remote-write` de k6 (voir `load-tests/README.md`).

Les services exposent `/metrics` sur le port `9090` (jamais routé par Traefik) ; Prometheus les
découvre via les annotations `prometheus.io/*` des pods. RabbitMQ est scrapé sur son plugin natif (port 15692).
