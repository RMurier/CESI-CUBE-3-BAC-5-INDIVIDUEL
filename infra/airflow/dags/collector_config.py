import json
from datetime import datetime, timedelta
from pathlib import Path

import requests

try:  # Airflow 3
    from airflow.sdk import Variable
except ImportError:  # Airflow 2.x
    from airflow.models import Variable

# Pas de MSSQL_CONN_ID ici : les DAGs Collector n'accèdent à aucune base, ils appellent les endpoints
# /internal/* des services (la base reste la propriété exclusive de chaque service).

TAGS = ["collector", "marketplace", "Romain MURIER"]

DEFAULT_ARGS = {
    "owner": "collector",
    "start_date": datetime(2025, 1, 1),
    "retries": 2,
    "retry_delay": timedelta(minutes=5),
    "execution_timeout": timedelta(hours=1),
    "depends_on_past": False,
    "email": ["murierromain@gmail.com"],
    "email_on_failure": True,
    "email_on_retry": False,
}

DAG_DEFAULTS = {
    "default_args": DEFAULT_ARGS,
    "tags": TAGS,
    "catchup": False,
    "max_active_runs": 1,
}

# --- Environnements Collector pilotés par CETTE instance Airflow -------------------------------------------
# Le pipeline CI/CD dépose à côté de ce fichier `collector_environments.json`, propre à l'instance
# (infra/airflow/environments/dev.json -> instance dev : dev + uat ; prod.json -> instance prod).
# Repli sans ce fichier : Variable Airflow `collector_environments` (même format JSON).
# Les clés d'API ne sont jamais dans le fichier : chaque environnement référence une Variable Airflow
# (`key_variable`), qui doit contenir la même valeur que Maintenance__ApiKey dans Infisical.
ENVIRONMENTS_FILE = Path(__file__).parent / "collector_environments.json"
HTTP_TIMEOUT_SECONDS = 30


def get_environments() -> list[dict]:
    if ENVIRONMENTS_FILE.exists():
        envs = json.loads(ENVIRONMENTS_FILE.read_text(encoding="utf-8"))
    else:
        envs = json.loads(Variable.get("collector_environments", default_var="{}"))
    if not envs:
        raise ValueError("Aucun environnement Collector configuré (collector_environments.json / Variable).")
    return [
        {"name": name, "base_url": cfg["base_url"].rstrip("/"), "key": Variable.get(cfg["key_variable"])}
        for name, cfg in envs.items()
    ]


def call_maintenance(method: str, env: dict, path: str, **kwargs) -> dict:
    response = requests.request(
        method,
        f"{env['base_url']}{path}",
        headers={"X-Maintenance-Key": env["key"]},
        timeout=HTTP_TIMEOUT_SECONDS,
        **kwargs,
    )
    # raise_for_status : un 401/404/5xx fait échouer la tâche (donc retry puis e-mail d'échec).
    response.raise_for_status()
    return response.json()
