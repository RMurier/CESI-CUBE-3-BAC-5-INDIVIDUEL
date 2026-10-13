/** URLs des API appelées depuis le navigateur — Traefik en dev/uat/prod, docker-compose + dotnet run en local. */
export const apiConfig = {
  identity: process.env.NEXT_PUBLIC_IDENTITY_API_URL ?? "http://localhost:5101",
  catalog: process.env.NEXT_PUBLIC_CATALOG_API_URL ?? "http://localhost:5102",
  notification: process.env.NEXT_PUBLIC_NOTIFICATION_API_URL ?? "http://localhost:5104",
};
