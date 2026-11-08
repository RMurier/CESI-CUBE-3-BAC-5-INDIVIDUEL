# Build context attendu : la racine du repo
# docker build -f src/services/Moderation/Moderation.Api/Dockerfile -t collector-moderation .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json .
COPY src/Directory.Build.props src/Directory.Build.props
COPY src/shared/Collector.Contracts/Collector.Contracts.csproj src/shared/Collector.Contracts/
COPY src/shared/Collector.Messaging/Collector.Messaging.csproj src/shared/Collector.Messaging/
COPY src/shared/Collector.Observability/Collector.Observability.csproj src/shared/Collector.Observability/
COPY src/services/Moderation/Moderation.Api/Moderation.Api.csproj src/services/Moderation/Moderation.Api/
RUN dotnet restore src/services/Moderation/Moderation.Api/Moderation.Api.csproj

COPY src/shared/ src/shared/
COPY src/services/Moderation/Moderation.Api/ src/services/Moderation/Moderation.Api/
RUN dotnet publish src/services/Moderation/Moderation.Api/Moderation.Api.csproj -c Release -o /app --no-restore

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
ENTRYPOINT ["dotnet", "Moderation.Api.dll"]
