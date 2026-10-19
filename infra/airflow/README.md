# Airflow — traitements récurrents Collector.shop

Ce dossier **ne déploie pas Airflow** : il contient les DAGs Collector et leur déploiement sur tes instances
existantes (même serveur, deux instances : `/opt/airflow-dev` et `/opt/airflow-prod`, comme `omnia-airflow` et `porty-airflow`).

| Instance Airflow | Branche → déploiement | Environnements Collector pilotés |
|---|---|---|
| `dev.airflow.romainmurier.com` (`/opt/airflow-dev/dags`) | `develop` **et** `uat` | `dev` **et** `uat` |
| `airflow.romainmurier.com` (`/opt/airflow-prod/dags`) | `master` | `prod` |

dev et uat (« qualif ») partagent donc la même instance, **sans dupliquer aucun DAG** : chaque DAG existe en un seul
exemplaire et crée une tâche par environnement (dynamic task mapping, `.expand(env=...)`). La liste des environnements
vient de `environments/dev.json`, déposé sur le serveur sous le nom `collector_environments.json`. Dans l'interface Airflow,
un run de `collector_expire_pending_orders` affiche donc `expire_pending_orders [dev]` et `expire_pending_orders [uat]` : un échec
sur uat n'empêche pas dev (et inversement) et chacun a ses propres logs et retries. Ajouter un environnement = une ligne dans le
JSON, pas un nouveau DAG. L'instance prod charge `prod.json` et ne peut atteindre ni dev ni uat. Les pushes `develop` et `uat`
déploient tous deux sur cette instance dev partagée : le dernier push l'emporte (en flux normal develop -> uat, uat est en retard
sur develop ; les DAGs de dev sont alors remplacés par ceux de uat jusqu'au prochain push sur develop).

## Fichiers (convention des dépôts `{app}-airflow`)

Même convention qu'`omnia-airflow` et `porty-airflow` (voir leur `CLAUDE.md`) : fichiers **à plat** dans `dags/`, tous préfixés
`collector_` pour ne jamais écraser un fichier d'un autre projet.

| Fichier sur le serveur | Rôle |
|---|---|
| `collector_config.py` | Config partagée : `TAGS`, `DEFAULT_ARGS`, `DAG_DEFAULTS` (mêmes valeurs que les autres projets), lecture des environnements, appel des endpoints |
| `collector_expire_pending_orders.py` | DAG `collector_expire_pending_orders` |
| `collector_purge_refresh_tokens.py` | DAG `collector_purge_refresh_tokens` |
| `collector_commission_report.py` | DAG `collector_commission_report` |
| `collector_environments.json` | Environnements pilotés par l'instance (copié depuis `environments/<instance>.json`) |

Un fichier par DAG, id du DAG = nom du fichier (vérifié au déploiement). Pas de `MSSQL_CONN_ID` : ces DAGs n'accèdent à aucune base.
Les horaires (cron) sont en UTC, comme pour les autres DAGs.

## DAGs

| DAG | Fréquence | Effet |
|---|---|---|
| `collector_expire_pending_orders` | toutes les 15 min | Annule les commandes `Pending` de plus de 35 min (filet de sécurité si le webhook Stripe d'expiration est perdu) |
| `collector_purge_refresh_tokens` | tous les jours 03:00 UTC | Supprime les refresh tokens expirés / révoqués depuis > 30 j |
| `collector_commission_report` | tous les jours 01:00 UTC | Rapprochement : commissions encaissées la veille (log + XCom) |

## CI/CD

`pipeline.yml`, job `deploy-airflow` : il tourne **en parallèle** de `deploy-dev` / `deploy-uat` / `deploy-prod`, avec les mêmes
conditions qu'eux (`needs: build-and-push`, donc tous les tests et scans verts ; mêmes branches ; mêmes environnements GitHub :
develop -> `dev`, uat -> `uat`, master -> `production`, donc approbation du reviewer sur master). Il exécute
`infra/airflow/deploy-dags.sh <dev|prod>` (`prod` si master, sinon `dev`) qui :
1. vérifie : syntaxe Python, id de DAG = nom de fichier, JSON valide, URLs en https ;
2. envoie les fichiers `collector_*` en SSH (`tar`) à la racine de `dags/` ;
3. supprime sur le serveur les `collector_*` qui ne sont plus dans le dépôt (DAG renommé/supprimé) — **uniquement ce préfixe** :
   aucun fichier d'omnia, de porty ou d'un autre projet n'est jamais écrit ni supprimé.

Les DAGs ne sont pas déployés après l'API : si un DAG tourne avant que l'endpoint `/internal/*` qu'il appelle soit déployé, la tâche
échoue (404) puis est relancée (2 retries, 5 min d'écart, voir `collector_config.py`), ce qui couvre l'écart habituel.

### Secrets de repo GitHub à créer
| Secret | Valeur |
|---|---|
| `AIRFLOW_SSH_HOST` / `AIRFLOW_SSH_USER` / `AIRFLOW_SSH_PASSWORD` | mêmes valeurs que `SERVER_HOST` / `SERVER_USER` / `SERVER_PASSWORD` de `omnia-airflow`. Un seul hôte (`airflow.romainmurier.com`) pour les deux instances, qui sont sur le même VPS : c'est le **dossier** de destination (`/opt/airflow-dev/dags` ou `/opt/airflow-prod/dags`) qui choisit l'instance, pas l'hôte |
| `AIRFLOW_SSH_KNOWN_HOSTS` *(recommandé)* | sortie de `ssh-keyscan -t ed25519 <host>` : sans lui l'empreinte du serveur n'est pas vérifiée (warning dans le job) |

### Secrets Airflow (Admin > Variables, sur chaque instance, jamais dans le dépôt)
Une clé par environnement, **même valeur** que `Maintenance__ApiKey` dans le scope Infisical correspondant
(attention : l'environnement `uat` utilise le scope Infisical `staging`) :
`collector_dev_maintenance_key`, `collector_uat_maintenance_key` (instance dev) ; `collector_prod_maintenance_key` (instance prod).
Générer : `openssl rand -base64 32 | tr -d '\n'`.

## Cohabitation avec omnia-airflow et porty-airflow

Le préfixe `collector_` garantit l'absence de collision de noms avec `omnia_*` / `porty_*`. Leurs workflows ne font que
`tar xzf` (sans vider le dossier), donc ils n'effacent pas les fichiers Collector. À l'inverse, un DAG renommé ou supprimé chez eux
reste sur le serveur jusqu'à suppression manuelle (ex. l'ancien `config.py` d'omnia, renommé `omnia_config.py`).

## Sécurité

Les endpoints `/internal/*` passent par Traefik comme le reste de l'API :
- ils exigent la clé `X-Maintenance-Key` (comparaison à temps constant) et sont **désactivés (404) tant que
  `Maintenance__ApiKey` n'est pas défini** ;
- en plus, les serveurs Airflow ayant des IP fixes, tu peux les rendre **injoignables pour tout autre** :
  renseigne leurs IP publiques dans `maintenance.allowedCidrs` (`values-dev.yaml` / `values-prod.yaml`), par exemple
  `maintenance: { allowedCidrs: ["<ip-airflow>/32"] }`. Le chart crée alors un middleware Traefik `ipAllowList`
  sur `/api/catalog/internal` et `/api/identity/internal`. Prérequis : Traefik v3 (`kubectl -n kube-system get deploy traefik -o jsonpath='{.spec.template.spec.containers[0].image}'`)
  et que Traefik voie la vraie IP source (sinon l'accès serait refusé à tort : laisser `allowedCidrs` vide et ne compter que sur la clé).
