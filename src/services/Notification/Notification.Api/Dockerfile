# Build context attendu : la racine du repo
# docker build -f src/services/Notification/Notification.Api/Dockerfile -t collector-notification .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json .
COPY src/Directory.Build.props src/Directory.Build.props
COPY src/shared/Collector.Contracts/Collector.Contracts.csproj src/shared/Collector.Contracts/
COPY src/shared/Collector.Messaging/Collector.Messaging.csproj src/shared/Collector.Messaging/
COPY src/services/Notification/Notification.Api/Notification.Api.csproj src/services/Notification/Notification.Api/
RUN dotnet restore src/services/Notification/Notification.Api/Notification.Api.csproj

COPY src/shared/ src/shared/
COPY src/services/Notification/Notification.Api/ src/services/Notification/Notification.Api/
RUN dotnet publish src/services/Notification/Notification.Api/Notification.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN useradd --create-home appuser
COPY --from=build /app .
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Notification.Api.dll"]
