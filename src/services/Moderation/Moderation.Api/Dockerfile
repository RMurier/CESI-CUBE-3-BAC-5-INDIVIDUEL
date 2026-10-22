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
RUN useradd --create-home appuser
COPY --from=build /app .
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Moderation.Api.dll"]
