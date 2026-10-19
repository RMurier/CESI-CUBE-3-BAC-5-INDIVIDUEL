import { apiConfig } from "../config";

export type OrderStatus = "Pending" | "Paid" | "Cancelled";

export interface Order {
  id: string;
  articleId: string;
  articleTitle: string;
  itemPrice: number;
  shippingCost: number;
  totalAmount: number;
  commissionAmount: number;
  status: OrderStatus;
  createdAt: string;
  paidAt: string | null;
}

export const orderStatusLabel: Record<OrderStatus, string> = {
  Pending: "Paiement en attente",
  Paid: "Payée",
  Cancelled: "Annulée",
};

async function parseOrThrow<T>(res: Response, fallback: string): Promise<T> {
  if (!res.ok) {
    const body = await res.json().catch(() => null);
    throw new Error(body?.message ?? fallback);
  }
  return (await res.json()) as T;
}

const authHeaders = (token: string) => ({ Authorization: `Bearer ${token}` });

/** Crée la commande et renvoie l'URL Stripe Checkout (page hébergée par Stripe : aucune donnée de carte chez nous). */
export function createOrder(token: string, articleId: string): Promise<{ orderId: string; checkoutUrl: string }> {
  return fetch(`${apiConfig.catalog}/orders`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders(token) },
    body: JSON.stringify({ articleId }),
  }).then((res) => parseOrThrow(res, "Impossible de lancer le paiement."));
}

export function getOrder(token: string, id: string): Promise<Order> {
  return fetch(`${apiConfig.catalog}/orders/${id}`, { headers: authHeaders(token), cache: "no-store" }).then((res) =>
    parseOrThrow(res, "Commande introuvable."),
  );
}

export function listMyOrders(token: string): Promise<Order[]> {
  return fetch(`${apiConfig.catalog}/orders/mine`, { headers: authHeaders(token), cache: "no-store" }).then((res) =>
    parseOrThrow(res, "Impossible de charger vos commandes."),
  );
}

export interface PayoutStatus {
  configured: boolean;
  ready: boolean;
}

/** État du compte de paiement Stripe Connect du vendeur (relu chez Stripe tant qu'il n'est pas prêt). */
export function getPayoutStatus(token: string): Promise<PayoutStatus> {
  return fetch(`${apiConfig.catalog}/seller/payout-account`, { headers: authHeaders(token), cache: "no-store" }).then((res) =>
    parseOrThrow(res, "Impossible de lire l'état de vos paiements."),
  );
}

/** Renvoie le lien d'onboarding hébergé par Stripe (identité, IBAN : rien n'est saisi chez Collector). */
export function startPayoutOnboarding(token: string): Promise<{ url: string }> {
  return fetch(`${apiConfig.catalog}/seller/payout-account/onboarding`, { method: "POST", headers: authHeaders(token) }).then((res) =>
    parseOrThrow(res, "Impossible de démarrer la configuration des paiements."),
  );
}
