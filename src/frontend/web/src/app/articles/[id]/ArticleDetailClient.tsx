"use client";

import { useEffect, useState } from "react";
import { getArticle, type Article } from "@/lib/api/catalog";
import { StatusBadge } from "@/components/StatusBadge";

const currency = new Intl.NumberFormat("fr-FR", { style: "currency", currency: "EUR" });
const dateFormat = new Intl.DateTimeFormat("fr-FR", { dateStyle: "medium", timeStyle: "short" });

export function ArticleDetailClient({ id }: { id: string }) {
  const [article, setArticle] = useState<Article | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getArticle(id)
      .then(setArticle)
      .catch((err: Error) => setError(err.message));
  }, [id]);

  if (error) return <p className="text-sm text-red-600 dark:text-red-400">{error}</p>;
  if (!article) return <p className="text-sm text-zinc-500">Chargement de l&apos;article…</p>;

  return (
    <article className="flex flex-col gap-6">
      <div className="flex items-start justify-between gap-4">
        <h1 className="text-3xl font-semibold tracking-tight">{article.title}</h1>
        <StatusBadge status={article.status} />
      </div>

      <p className="text-zinc-700 dark:text-zinc-300">{article.description}</p>

      <div className="flex gap-8 rounded-xl border border-black/[.08] p-5 dark:border-white/[.145]">
        <div>
          <p className="text-xs text-zinc-500">Prix</p>
          <p className="text-xl font-semibold">{currency.format(article.price)}</p>
        </div>
        <div>
          <p className="text-xs text-zinc-500">Frais de port</p>
          <p className="text-xl font-semibold">{currency.format(article.shippingCost)}</p>
        </div>
        <div>
          <p className="text-xs text-zinc-500">Soumis le</p>
          <p className="text-sm">{dateFormat.format(new Date(article.submittedAt))}</p>
        </div>
        {article.publishedAt && (
          <div>
            <p className="text-xs text-zinc-500">Publié le</p>
            <p className="text-sm">{dateFormat.format(new Date(article.publishedAt))}</p>
          </div>
        )}
      </div>

      {article.rejectionReason && (
        <p className="rounded-lg bg-red-50 p-4 text-sm text-red-700 dark:bg-red-900/20 dark:text-red-300">
          Motif du rejet : {article.rejectionReason}
        </p>
      )}
    </article>
  );
}
