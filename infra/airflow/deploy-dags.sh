#!/usr/bin/env bash
# Déploie les DAGs Collector sur une instance Airflow existante.
#
#   deploy-dags.sh dev|prod
#
# Convention des dépôts {app}-airflow (voir leur CLAUDE.md) : fichiers À PLAT dans dags/, tous préfixés
# `collector_` (collector_config.py, collector_<dag>.py, collector_environments.json). Le dossier dags/ est
# partagé avec omnia-airflow et porty-airflow : ce script n'écrit ni ne supprime JAMAIS un fichier qui ne
# commence pas par `collector_`.
#
# Variables d'environnement :
#   AIRFLOW_SSH_HOST, AIRFLOW_SSH_USER, AIRFLOW_SSH_PASSWORD   accès SSH au serveur Airflow (secrets de repo)
#   AIRFLOW_SSH_KNOWN_HOSTS   (optionnel) ligne(s) known_hosts du serveur : active la vérification de l'empreinte
#   DAGS_DIR                  (optionnel) surcharge le dossier dags/ cible (tests)
set -euo pipefail

instance="${1:?usage: deploy-dags.sh dev|prod}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

case "$instance" in
  dev)  default_dir=/opt/airflow-dev/dags ;;
  prod) default_dir=/opt/airflow-prod/dags ;;
  *) echo "Instance inconnue : $instance (dev|prod)" >&2; exit 2 ;;
esac
target="${DAGS_DIR:-$default_dir}"

stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
mkdir "$stage/files"
cp "$here"/dags/collector_*.py "$stage/files/"
cp "$here/environments/$instance.json" "$stage/files/collector_environments.json"

# Garde-fous avant d'envoyer quoi que ce soit.
for f in "$stage"/files/collector_*.py; do
  name="$(basename "$f" .py)"
  python3 -m py_compile "$f"
  # Convention : l'id du DAG est le nom du fichier (sauf le fichier de config, qui ne définit aucun DAG).
  if [ "$name" != collector_config ] && ! grep -q "^    \"$name\",$" "$f"; then
    echo "::error::$name.py : l'id du DAG doit être \"$name\" (convention {app}_{dag_name}.py)." >&2
    exit 1
  fi
done
python3 - "$stage/files/collector_environments.json" <<'PY'
import json, sys
envs = json.load(open(sys.argv[1]))
assert envs, "collector_environments.json vide"
for name, cfg in envs.items():
    assert cfg["base_url"].startswith("https://"), f"{name}: base_url doit être en https"
    assert cfg["key_variable"], f"{name}: key_variable manquante"
print("environnements :", ", ".join(envs))
PY
find "$stage" -name __pycache__ -type d -exec rm -rf {} +
keep="$(cd "$stage/files" && ls -1 | tr '\n' ' ')"

ssh_opts=(-o BatchMode=no)
if [ -n "${AIRFLOW_SSH_KNOWN_HOSTS:-}" ]; then
  printf '%s\n' "$AIRFLOW_SSH_KNOWN_HOSTS" > "$stage/known_hosts"
  ssh_opts+=(-o StrictHostKeyChecking=yes -o "UserKnownHostsFile=$stage/known_hosts")
else
  echo "::warning::AIRFLOW_SSH_KNOWN_HOSTS absent : empreinte du serveur non vérifiée (premier contact accepté)."
  ssh_opts+=(-o StrictHostKeyChecking=accept-new)
fi

export SSHPASS="${AIRFLOW_SSH_PASSWORD:?AIRFLOW_SSH_PASSWORD manquant}"

# 1) extraction (écrase en place : Airflow ne voit jamais le config manquant) ;
# 2) suppression des anciens collector_* qui ne sont plus dans le dépôt (DAG renommé/supprimé), préfixe uniquement.
tar czf - -C "$stage/files" . | sshpass -e ssh "${ssh_opts[@]}" \
  "${AIRFLOW_SSH_USER:?AIRFLOW_SSH_USER manquant}@${AIRFLOW_SSH_HOST:?AIRFLOW_SSH_HOST manquant}" \
  "set -e; mkdir -p '$target'; tar xzf - -C '$target'; cd '$target'; \
   for f in collector_*; do [ -e \"\$f\" ] || continue; case ' $keep ' in *\" \$f \"*) ;; *) echo \"suppression de \$f (obsolète)\"; rm -f -- \"\$f\" ;; esac; done; \
   ls -1 collector_*"

echo "DAGs Collector déployés sur l'instance Airflow « $instance » ($target)."
