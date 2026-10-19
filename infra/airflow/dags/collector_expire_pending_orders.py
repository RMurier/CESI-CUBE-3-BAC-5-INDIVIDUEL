from airflow import DAG

try:  # Airflow 3
    from airflow.sdk import task
except ImportError:  # Airflow 2.x
    from airflow.decorators import task

from collector_config import DAG_DEFAULTS, TAGS, call_maintenance, get_environments

with DAG(
    "collector_expire_pending_orders",
    description="Annule les commandes 'Pending' abandonnées (checkout non payé, webhook d'expiration perdu)",
    schedule="*/15 * * * *",
    tags=TAGS + ["collector_expire_pending_orders"],
    **{k: v for k, v in DAG_DEFAULTS.items() if k != "tags"},
) as dag:

    @task
    def environments() -> list[dict]:
        # Lu dans une tâche : les clés d'API ne sont jamais chargées au parsing du DAG.
        return get_environments()

    @task
    def expire_pending_orders(env: dict) -> dict:
        result = call_maintenance("POST", env, "/api/catalog/internal/maintenance/expire-pending-orders")
        print(f"[{env['name']}] commandes annulées : {result['cancelled']}")
        return {"env": env["name"], **result}

    # Une tâche par environnement : un échec sur uat n'empêche pas dev, et inversement.
    expire_pending_orders.expand(env=environments())
