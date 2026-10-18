# Build context attendu : la racine du repo (cohérence avec les Dockerfiles des services .NET)
# docker build -f src/frontend/web/Dockerfile -t collector-frontend .

FROM node:22-alpine AS deps
WORKDIR /app
COPY src/frontend/web/package.json src/frontend/web/package-lock.json ./
# --ignore-scripts : aucun script d'installation (supply chain). Seuls fsevents (macOS) et
# unrs-resolver (résolveur de lint/tests, binaire natif via optionalDependencies) en ont un, et
# aucun n'est nécessaire à "next build".
RUN npm ci --ignore-scripts

FROM node:22-alpine AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY src/frontend/web/ .
RUN npm run build

FROM node:22-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production

# L'image Next.js "standalone" ne lance que "node server.js" : le CLI npm, yarn et corepack
# embarqués dans l'image de base node:22-alpine ne servent à rien à l'exécution, et Trivy y
# détecte régulièrement des CVE HIGH (ex. brace-expansion, pacote, picomatch — dépendances de
# npm lui-même, pas de l'app). On les retire du stage final.
RUN rm -rf \
    /usr/local/lib/node_modules/npm \
    /usr/local/lib/node_modules/corepack \
    /usr/local/bin/npm \
    /usr/local/bin/npx \
    /usr/local/bin/corepack \
    /usr/local/bin/yarn \
    /usr/local/bin/yarnpkg \
    /opt/yarn-v*

RUN addgroup -S appgroup && adduser -S appuser -G appgroup

# Fichiers appartenant à root et en lecture seule (0555) : appuser peut les lire/exécuter mais pas
# les modifier, ce qui limite l'impact d'une compromission du processus Node.
COPY --from=build --chown=root:root --chmod=0555 /app/public ./public
COPY --from=build --chown=root:root --chmod=0555 /app/.next/standalone ./
COPY --from=build --chown=root:root --chmod=0555 /app/.next/static ./.next/static

USER appuser
ENV PORT=3000
EXPOSE 3000
CMD ["node", "server.js"]
