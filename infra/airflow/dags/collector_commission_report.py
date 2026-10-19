from airflow import DAG

try:  # Airflow 3
    from airflow.sdk import task
except ImportError:  # Airflow 2.x
    from airflow.decorators import task

from collector_config import DAG_DEFAULTS, TAGS, call_maintenance, get_environments

with DAG(
    "collector_commission_report",
    description="Rapprochement quotidien : commissions encaissées la veille, par environnement",
    schedule="0 1 * * *",
    tags=TAGS + ["collector_commission_report"],
    **{k: v for k, v in DAG_DEFAULTS.items() if k != "tags"},
) as dag:

    @task
    def environments() -> list[dict]:
        return get_environments()

    @task
    def commission_report(env: dict, data_interval_start=None, data_interval_end=None) -> dict:
        report = call_maintenance(
            "GET",
            env,
            "/api/catalog/internal/reports/commissions",
            params={"from": data_interval_start.isoformat(), "to": data_interval_end.isoformat()},
        )
        print(
            f"[{env['name']}] {report['orders']} commande(s), total {report['totalAmount']} EUR, "
            f"commission Collector {report['commissionAmount']} EUR, net vendeurs {report['sellerAmount']} EUR"
        )
        return {"env": env["name"], **report}

    commission_report.expand(env=environments())
