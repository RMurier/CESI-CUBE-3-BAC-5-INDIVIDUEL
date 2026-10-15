# Chart Helm `collector`

Un seul chart, une valeur par environnement. Déployer sur dev/uat/prod ne change **aucun template**
— uniquement le fichier `values-<env>.yaml` et le tag d'image (`--set global.imageTag=...`).

## Prérequis sur le cluster K3s (à faire une fois, hors chart)

1. **Traefik** — déjà fourni par défaut par k3s (`--disable traefik` NON utilisé à l'installation).
2. **cert-manager** avec un `ClusterIssuer` nommé `letsencrypt` (Let's Encrypt via challenge HTTP01 ou
   DNS01 selon le provider DNS) — requis par `templates/ingressroute.yaml`.
3. **Infisical Secrets Operator** installé au niveau cluster — requis par `templates/infisical-secret.yaml`.
   Voir https://infisical.com/docs/integrations/platforms/kubernetes (à revérifier, l'opérateur évolue vite).
4. Une Machine Identity Infisical (Universal Auth) existante, avec son `clientId`/`clientSecret` — le
   Secret K8s `infisical-machine-identity` qui authentifie l'opérateur dans chaque namespace est
   provisionné **automatiquement à chaque déploiement Helm** par `templates/infisical-machine-identity.yaml`,
   alimenté par `cd.yml` via `--set-string infisical.machineIdentity.clientId/clientSecret` depuis
   les GitHub Secrets `INFISICAL_CLIENT_ID`/`INFISICAL_CLIENT_SECRET` — plus de `kubectl create
   secret` manuel, et changer de serveur (nouveau `KUBE_CONFIG_B64`) n'exige aucune étape
   supplémentaire : le prochain déploiement recrée tout.

## Clés attendues dans Infisical (environnement dev/uat/prod)

Toutes synchronisées automatiquement vers le Secret K8s `collector-secrets` par l'opérateur Infisical :

| Clé | Utilisée par |
|---|---|
| `Jwt__SigningKey` | Identity, Catalog, Chat (vérification JWT) |
| `ConnectionStrings__identity` | Identity.Api (`Server=collector-identity-sql,1433;Database=identity;User Id=sa;Password=<même valeur que MSSQL_SA_PASSWORD>;TrustServerCertificate=True`) |
| `ConnectionStrings__catalog` | Catalog.Api (idem avec `collector-catalog-sql` / `Database=catalog`) |
| `ConnectionStrings__chat` | Chat.Api (idem `collector-chat-sql` / `Database=chat`) |
| `ConnectionStrings__notification` | Notification.Api (idem `collector-notification-sql` / `Database=notification`) |
| `ConnectionStrings__moderation` | Moderation.Api (idem `collector-moderation-sql` / `Database=moderation`) |
| `MSSQL_SA_PASSWORD` | Les 5 StatefulSets SQL Server (même mot de passe que ci-dessus) |
| `RabbitMq__UserName` / `RabbitMq__Password` | RabbitMQ (StatefulSet) + tous les services consommateurs |

**`KUBE_CONFIG_B64`, `INFISICAL_CLIENT_ID` et `INFISICAL_CLIENT_SECRET` ne sont volontairement PAS
dans cette liste** : ce sont des secrets d'infra CI/CD, pas des secrets applicatifs — même cluster
physique pour les 3 environnements, donc une seule valeur de chaque, en secrets de **repo** GitHub
(`Settings > Secrets and variables > Actions > Repository secrets`), lus directement par `cd.yml`.
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
