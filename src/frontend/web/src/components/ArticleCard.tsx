import Link from "next/link";
import type { Article } from "@/lib/api/catalog";

const currency = new Intl.NumberFormat("fr-FR", { style: "currency", currency: "EUR" });

export function ArticleCard({ article }: { article: Article }) {
  return (
    <Link
      href={`/articles/${article.id}`}
      className="flex flex-col gap-2 rounded-xl border border-black/[.08] p-5 transition-colors hover:border-black/[.2] dark:border-white/[.145] dark:hover:border-white/[.3]"
    >
      <h3 className="text-lg font-semibold">{article.title}</h3>
      <p className="line-clamp-2 text-sm text-zinc-600 dark:text-zinc-400">{article.description}</p>
      <div className="mt-2 flex items-baseline justify-between">
        <span className="text-xl font-semibold">{currency.format(article.price)}</span>
        <span className="text-xs text-zinc-500">+ {currency.format(article.shippingCost)} de port</span>
      </div>
    </Link>
  );
}
