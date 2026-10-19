from airflow import DAG

try:  # Airflow 3
    from airflow.sdk import task
except ImportError:  # Airflow 2.x
    from airflow.decorators import task

from collector_config import DAG_DEFAULTS, TAGS, call_maintenance, get_environments

with DAG(
    "collector_purge_refresh_tokens",
    description="Supprime les refresh tokens expirés ou révoqués depuis plus de 30 jours",
    schedule="0 3 * * *",
    tags=TAGS + ["collector_purge_refresh_tokens"],
    **{k: v for k, v in DAG_DEFAULTS.items() if k != "tags"},
) as dag:

    @task
    def environments() -> list[dict]:
        return get_environments()

    @task
    def purge_refresh_tokens(env: dict) -> dict:
        result = call_maintenance("POST", env, "/api/identity/internal/maintenance/purge-refresh-tokens")
        print(f"[{env['name']}] refresh tokens supprimés : {result['deleted']}")
        return {"env": env["name"], **result}

    purge_refresh_tokens.expand(env=environments())
