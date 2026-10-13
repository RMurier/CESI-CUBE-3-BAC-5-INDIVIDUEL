import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Image runtime minimale (node server autonome) pour le Dockerfile multi-stage.
  output: "standalone",
  // cacheComponents/partialPrefetching désactivés : l'app est un SPA 100% client (contexte
  // d'auth + fetch côté client vers les API externes), aucune page ne fait de fetch serveur
  // à mettre en cache — ces flags n'imposeraient que des contraintes de Suspense sans bénéfice.
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
