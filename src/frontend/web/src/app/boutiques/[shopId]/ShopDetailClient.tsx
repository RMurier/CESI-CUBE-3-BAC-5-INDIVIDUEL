"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useAuth } from "@/lib/auth-context";
import { createArticle, getShop, type ShopDetail } from "@/lib/api/catalog";
import { StatusBadge } from "@/components/StatusBadge";

const currency = new Intl.NumberFormat("fr-FR", { style: "currency", currency: "EUR" });

/** Poll court pour montrer en direct la transition PendingReview -> Published/Rejected
 * (la décision de modération arrive de manière asynchrone via RabbitMQ). */
const POLL_INTERVAL_MS = 3000;

export function ShopDetailClient({ shopId }: { shopId: string }) {
  const { auth } = useAuth();

  const [shop, setShop] = useState<ShopDetail | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [price, setPrice] = useState("");
  const [shippingCost, setShippingCost] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const refresh = useCallback(() => {
    getShop(shopId)
      .then(setShop)
      .catch((err: Error) => setError(err.message));
  }, [shopId]);

  useEffect(() => {
    refresh();
    const interval = setInterval(refresh, POLL_INTERVAL_MS);
    return () => clearInterval(interval);
  }, [refresh]);

  const isOwner = auth && shop && auth.userId === shop.sellerId;

  async function handleCreateArticle(event: FormEvent) {
    event.preventDefault();
    if (!auth) return;
    setFormError(null);
    setSubmitting(true);
    try {
      await createArticle(auth.token, shopId, {
        title,
        description,
        price: Number(price),
        shippingCost: Number(shippingCost),
      });
      setTitle("");
      setDescription("");
      setPrice("");
      setShippingCost("");
      refresh();
    } catch (err) {
      setFormError(err instanceof Error ? err.message : "Impossible de soumettre l'article.");
    } finally {
      setSubmitting(false);
    }
  }

  if (error) return <p className="text-sm text-red-600 dark:text-red-400">{error}</p>;
  if (!shop) return <p className="text-sm text-zinc-500">Chargement de la boutique…</p>;

  return (
    <div className="flex flex-col gap-10">
      <div>
        <h1 className="text-3xl font-semibold tracking-tight">{shop.name}</h1>
        {shop.description && <p className="mt-2 text-zinc-600 dark:text-zinc-400">{shop.description}</p>}
      </div>

      {isOwner && (
        <section className="flex flex-col gap-3 rounded-xl border border-black/[.08] p-5 dark:border-white/[.145]">
          <h2 className="text-lg font-semibold">Publier un article</h2>
          <p className="text-sm text-zinc-600 dark:text-zinc-400">
            L&apos;article est d&apos;abord soumis au contrôle automatisé (Moderation) avant mise en ligne.
          </p>
          <form onSubmit={handleCreateArticle} className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <label className="flex flex-col gap-1 text-sm sm:col-span-2">
              Titre{" "}
              <input
                required
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                className="rounded-lg border border-black/[.08] bg-transparent px-3 py-2 dark:border-white/[.145]"
              />
            </label>
            <label className="flex flex-col gap-1 text-sm sm:col-span-2">
              Description{" "}
              <textarea
                required
                minLength={10}
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                className="rounded-lg border border-black/[.08] bg-transparent px-3 py-2 dark:border-white/[.145]"
              />
            </label>
            <label className="flex flex-col gap-1 text-sm">
              Prix (€){" "}
              <input
                type="number"
                min="0.01"
                step="0.01"
                required
                value={price}
                onChange={(e) => setPrice(e.target.value)}
                className="rounded-lg border border-black/[.08] bg-transparent px-3 py-2 dark:border-white/[.145]"
              />
            </label>
            <label className="flex flex-col gap-1 text-sm">
              Frais de port (€){" "}
              <input
                type="number"
                min="0"
                step="0.01"
                required
                value={shippingCost}
                onChange={(e) => setShippingCost(e.target.value)}
                className="rounded-lg border border-black/[.08] bg-transparent px-3 py-2 dark:border-white/[.145]"
              />
            </label>
            {formError && <p className="text-sm text-red-600 dark:text-red-400 sm:col-span-2">{formError}</p>}
            <button
              type="submit"
              disabled={submitting}
              className="w-fit rounded-full bg-foreground px-5 py-2 text-background transition-colors hover:bg-[#383838] disabled:opacity-50 dark:hover:bg-[#ccc] sm:col-span-2"
            >
              {submitting ? "Soumission…" : "Soumettre l'article"}
            </button>
          </form>
        </section>
      )}

      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold">Articles</h2>
        {shop.articles.length === 0 ? (
          <p className="text-sm text-zinc-500">Aucun article pour l&apos;instant.</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {shop.articles.map((article) => (
              <li
                key={article.id}
                className="flex items-center justify-between rounded-lg border border-black/[.08] px-4 py-3 dark:border-white/[.145]"
              >
                <div>
                  <p className="font-medium">{article.title}</p>
                  <p className="text-sm text-zinc-500">{currency.format(article.price)}</p>
                </div>
                <StatusBadge status={article.status} />
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
