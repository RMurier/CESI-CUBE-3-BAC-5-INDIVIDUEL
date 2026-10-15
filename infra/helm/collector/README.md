# Chart Helm `collector`

Un seul chart, une valeur par environnement. Déployer sur dev/uat/prod ne change **aucun template**
— uniquement le fichier `values-<env>.yaml` et le tag d'image (`--set global.imageTag=...`).

## Prérequis sur le cluster K3s (à faire une fois, hors chart)

1. **Traefik** — déjà fourni par défaut par k3s (`--disable traefik` NON utilisé à l'installation).
2. **cert-manager** avec un `ClusterIssuer` nommé `letsencrypt` (Let's Encrypt via challenge HTTP01 ou
   DNS01 selon le provider DNS) — requis par `templates/ingressroute.yaml`.
3. **Infisical Secrets Operator** installé au niveau cluster — requis par `templates/infisical-secret.yaml`.
   Voir https://infisical.com/docs/integrations/platforms/kubernetes (à revérifier, l'opérateur évolue vite).
4. Pour **chaque** namespace (`collector-dev`, `collector-uat`, `collector-prod`) : un Secret K8s nommé
   `infisical-machine-identity` contenant `clientId`/`clientSecret` d'une Machine Identity Infisical
   (Universal Auth), créé manuellement **avant** le premier déploiement Helm :
   ```bash
   kubectl create namespace collector-dev
   kubectl create secret generic infisical-machine-identity \
     --namespace collector-dev \
     --from-literal=clientId=<...> \
     --from-literal=clientSecret=<...>
   ```

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

**`KUBE_CONFIG_B64` n'est volontairement PAS dans cette liste** : c'est un secret d'infra CI/CD, pas
un secret applicatif — il vit en secret d'**environnement GitHub** (`Settings > Environments >
dev/uat/production > Environment secrets`, une valeur par environnement même si c'est le même
cluster physique), lu directement par `cd.yml`. Le pipeline CI/CD ne contacte jamais Infisical.

## Déploiement manuel (hors CI, pour test local)

```bash
helm upgrade --install collector infra/helm/collector \
  -f infra/helm/collector/values-dev.yaml \
  --namespace collector-dev --create-namespace \
  --set global.imageTag=latest \
  --kubeconfig <chemin-vers-kubeconfig>
```

## Validation sans cluster

```bash
helm lint infra/helm/collector -f infra/helm/collector/values-dev.yaml
helm template collector infra/helm/collector -f infra/helm/collector/values-dev.yaml --namespace collector-dev
```
