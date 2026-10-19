"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { getOrder, orderStatusLabel, type Order } from "@/lib/api/orders";
import { useAuth } from "@/lib/auth-context";

const currency = new Intl.NumberFormat("fr-FR", { style: "currency", currency: "EUR" });
const POLL_INTERVAL_MS = 3000;

export function OrderDetailClient({ id }: { id: string }) {
  const { auth, loading, getToken } = useAuth();
  const [order, setOrder] = useState<Order | null>(null);
  const [error, setError] = useState<string | null>(null);
  const loggedIn = !!auth;

  // Au retour de Stripe, la confirmation arrive par webhook (asynchrone) : on interroge la commande
  // tant qu'elle est "Pending" plutôt que de faire confiance à l'URL de retour du navigateur.
  const pending = order?.status === "Pending";
  const done = order !== null && !pending;
  useEffect(() => {
    if (!loggedIn) return;
    let cancelled = false;
    const load = () =>
      getToken()
        .then((token) => getOrder(token, id))
        .then((o) => !cancelled && setOrder(o))
        .catch((err: Error) => !cancelled && setError(err.message));
    load();
    if (done) {
      return () => {
        cancelled = true;
      };
    }
    const interval = setInterval(load, POLL_INTERVAL_MS);
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
    // getToken change à chaque rendu du provider : volontairement hors des dépendances.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, loggedIn, done]);

  if (loading) return <p className="text-sm text-zinc-500">Chargement…</p>;
  if (!auth) {
    return (
      <p className="text-sm">
        <Link href="/login" className="font-medium underline">
          Connectez-vous
        </Link>{" "}
        pour consulter votre commande.
      </p>
    );
  }
  if (error) return <p className="text-sm text-red-600 dark:text-red-400">{error}</p>;
  if (!order) return <p className="text-sm text-zinc-500">Chargement de la commande…</p>;

  return (
    <section className="flex flex-col gap-4">
      <h1 className="text-3xl font-semibold tracking-tight">Commande</h1>
      <p className="text-lg">{order.articleTitle}</p>
      <p role="status" className="text-sm font-medium">
        {orderStatusLabel[order.status]}
        {pending && " — confirmation du paiement en cours…"}
      </p>
      <dl className="grid w-fit grid-cols-2 gap-x-8 gap-y-1 text-sm">
        <dt className="text-zinc-500">Article</dt>
        <dd>{currency.format(order.itemPrice)}</dd>
        <dt className="text-zinc-500">Frais de port</dt>
        <dd>{currency.format(order.shippingCost)}</dd>
        <dt className="font-medium">Total</dt>
        <dd className="font-medium">{currency.format(order.totalAmount)}</dd>
      </dl>
      <Link href="/" className="text-sm underline">
        Retour au catalogue
      </Link>
    </section>
  );
}
