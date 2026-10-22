# Build context attendu : la racine du repo (nécessaire pour global.json + Directory.Build.props)
# docker build -f src/services/Identity/Identity.Api/Dockerfile -t collector-identity .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json .
COPY src/Directory.Build.props src/Directory.Build.props
COPY src/shared/Collector.Observability/Collector.Observability.csproj src/shared/Collector.Observability/
COPY src/services/Identity/Identity.Api/Identity.Api.csproj src/services/Identity/Identity.Api/
RUN dotnet restore src/services/Identity/Identity.Api/Identity.Api.csproj

COPY src/shared/Collector.Observability/ src/shared/Collector.Observability/
COPY src/services/Identity/Identity.Api/ src/services/Identity/Identity.Api/
RUN dotnet publish src/services/Identity/Identity.Api/Identity.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN useradd --create-home appuser
COPY --from=build /app .
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# Pas de HEALTHCHECK Docker : les probes liveness/readiness K8s (voir infra/helm) utilisent /health
# directement en HTTP et ne nécessitent pas de curl dans l'image runtime.

ENTRYPOINT ["dotnet", "Identity.Api.dll"]
