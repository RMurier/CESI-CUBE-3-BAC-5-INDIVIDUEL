from datetime import timedelta

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
    def commission_report(env: dict, data_interval_end=None) -> dict:
        # schedule="0 1 * * *" (chaîne cron) utilise le CronTriggerTimetable d'Airflow 3 : il n'a pas
        # de vrai intervalle, data_interval_start == data_interval_end == date de déclenchement. On
        # recalcule donc nous-mêmes la fenêtre "les 24h précédentes" à partir de data_interval_end.
        to = data_interval_end
        from_ = to - timedelta(days=1)
        report = call_maintenance(
            "GET",
            env,
            "/api/catalog/internal/reports/commissions",
            params={"from": from_.isoformat(), "to": to.isoformat()},
        )
        print(
            f"[{env['name']}] {report['orders']} commande(s), total {report['totalAmount']} EUR, "
            f"commission Collector {report['commissionAmount']} EUR, net vendeurs {report['sellerAmount']} EUR"
        )
        return {"env": env["name"], **report}

    commission_report.expand(env=environments())
