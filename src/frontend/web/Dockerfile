# Build context attendu : la racine du repo (cohérence avec les Dockerfiles des services .NET)
# docker build -f src/frontend/web/Dockerfile -t collector-frontend .

FROM node:22-alpine AS deps
WORKDIR /app
COPY src/frontend/web/package.json src/frontend/web/package-lock.json ./
RUN npm ci

FROM node:22-alpine AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY src/frontend/web/ .
RUN npm run build

FROM node:22-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production
RUN addgroup -S appgroup && adduser -S appuser -G appgroup

COPY --from=build /app/public ./public
COPY --from=build --chown=appuser:appgroup /app/.next/standalone ./
COPY --from=build --chown=appuser:appgroup /app/.next/static ./.next/static

USER appuser
ENV PORT=3000
EXPOSE 3000
CMD ["node", "server.js"]
