# Build context attendu : la racine du repo (nécessaire pour global.json + Directory.Build.props)
# docker build -f src/services/Identity/Identity.Api/Dockerfile -t collector-identity .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json .
COPY src/Directory.Build.props src/Directory.Build.props
COPY src/shared/Collector.Contracts/Collector.Contracts.csproj src/shared/Collector.Contracts/
COPY src/shared/Collector.Messaging/Collector.Messaging.csproj src/shared/Collector.Messaging/
COPY src/shared/Collector.Observability/Collector.Observability.csproj src/shared/Collector.Observability/
COPY src/services/Identity/Identity.Api/Identity.Api.csproj src/services/Identity/Identity.Api/
RUN dotnet restore src/services/Identity/Identity.Api/Identity.Api.csproj

COPY src/shared/ src/shared/
COPY src/services/Identity/Identity.Api/ src/services/Identity/Identity.Api/
RUN dotnet publish src/services/Identity/Identity.Api/Identity.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# apt lui-même a une CVE connue (TOCTOU lors d'une install, CVE-2026-109518) : le cache GHA par
# service (voir build-and-push dans pipeline.yml) peut réutiliser une couche de base plus ancienne
# que le dernier correctif publié par Microsoft, donc on force la mise à jour ici.
RUN apt-get update && apt-get upgrade -y apt && rm -rf /var/lib/apt/lists/*
RUN useradd --create-home appuser
COPY --from=build /app .
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# Pas de HEALTHCHECK Docker : les probes liveness/readiness K8s (voir infra/helm) utilisent /health
# directement en HTTP et ne nécessitent pas de curl dans l'image runtime.

ENTRYPOINT ["dotnet", "Identity.Api.dll"]
