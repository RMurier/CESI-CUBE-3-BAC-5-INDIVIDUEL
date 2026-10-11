import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Image runtime minimale (node server autonome) pour le Dockerfile multi-stage.
  output: "standalone",
  cacheComponents: true,
  partialPrefetching: true,
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
