# Build context attendu : la racine du repo
# docker build -f src/services/Chat/Chat.Api/Dockerfile -t collector-chat .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json .
COPY src/Directory.Build.props src/Directory.Build.props
COPY src/services/Chat/Chat.Api/Chat.Api.csproj src/services/Chat/Chat.Api/
RUN dotnet restore src/services/Chat/Chat.Api/Chat.Api.csproj

COPY src/services/Chat/Chat.Api/ src/services/Chat/Chat.Api/
RUN dotnet publish src/services/Chat/Chat.Api/Chat.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN useradd --create-home appuser
COPY --from=build /app .
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Chat.Api.dll"]
