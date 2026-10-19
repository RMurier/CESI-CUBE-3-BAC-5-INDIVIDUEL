import type { NextConfig } from "next";

// CSP appliquée en production uniquement : le mode dev de Next injecte du code évalué (HMR) et se
// connecte à des API sur d'autres ports, ce que cette politique stricte bloquerait.
// 'unsafe-inline' sur script-src : Next injecte des scripts inline d'hydratation ; passer à des
// nonces demanderait un middleware par requête (piste d'amélioration documentée dans le plan de remédiation).
// En prod les API sont appelées en chemins relatifs via Traefik, donc connect-src 'self' suffit
// (SignalR en wss sur la même origine inclus). form-action autorise le retour vers Stripe Checkout.
const contentSecurityPolicy = [
  "default-src 'self'",
  "script-src 'self' 'unsafe-inline'",
  "style-src 'self' 'unsafe-inline'",
  "img-src 'self' data: blob: https:",
  "font-src 'self' data:",
  "connect-src 'self' wss:",
  "frame-ancestors 'none'",
  "base-uri 'self'",
  "form-action 'self' https://checkout.stripe.com",
  "object-src 'none'",
  "upgrade-insecure-requests",
].join("; ");

const securityHeaders = [
  { key: "Strict-Transport-Security", value: "max-age=63072000; includeSubDomains" },
  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "X-Frame-Options", value: "DENY" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=()" },
  ...(process.env.NODE_ENV === "production"
    ? [{ key: "Content-Security-Policy", value: contentSecurityPolicy }]
    : []),
];

const nextConfig: NextConfig = {
  poweredByHeader: false,
  async headers() {
    return [{ source: "/:path*", headers: securityHeaders }];
  },
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
