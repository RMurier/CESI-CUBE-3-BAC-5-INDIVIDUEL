"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { listMyOrders, orderStatusLabel, type Order } from "@/lib/api/orders";
import { useAuth } from "@/lib/auth-context";

const currency = new Intl.NumberFormat("fr-FR", { style: "currency", currency: "EUR" });

export default function OrdersPage() {
  const { auth, loading, getToken } = useAuth();
  const [orders, setOrders] = useState<Order[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const loggedIn = !!auth;

  useEffect(() => {
    if (!loggedIn) return;
    getToken()
      .then(listMyOrders)
      .then(setOrders)
      .catch((err: Error) => setError(err.message));
    // getToken change à chaque rendu du provider : volontairement hors des dépendances.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loggedIn]);

  if (loading) return <p className="text-sm text-zinc-500">Chargement…</p>;
  if (!auth) {
    return (
      <p className="text-sm">
        <Link href="/login" className="font-medium underline">
          Connectez-vous
        </Link>{" "}
        pour consulter vos commandes.
      </p>
    );
  }
  if (error) return <p className="text-sm text-red-600 dark:text-red-400">{error}</p>;
  if (!orders) return <p className="text-sm text-zinc-500">Chargement de vos commandes…</p>;

  return (
    <section className="flex flex-col gap-4">
      <h1 className="text-3xl font-semibold tracking-tight">Mes commandes</h1>
      {orders.length === 0 ? (
        <p className="text-sm text-zinc-500">Aucune commande pour le moment.</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {orders.map((order) => (
            <li key={order.id}>
              <Link
                href={`/commandes/${order.id}`}
                className="flex items-center justify-between rounded-xl border border-black/[.08] p-4 hover:bg-black/[.03] dark:border-white/[.145]"
              >
                <span>{order.articleTitle}</span>
                <span className="text-sm text-zinc-600 dark:text-zinc-400">
                  {currency.format(order.totalAmount)} · {orderStatusLabel[order.status]}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
