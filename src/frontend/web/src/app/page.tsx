"use client";

import { useEffect, useState } from "react";
import { listPublishedArticles, type Article } from "@/lib/api/catalog";
import { ArticleCard } from "@/components/ArticleCard";

export default function CataloguePage() {
  const [articles, setArticles] = useState<Article[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    listPublishedArticles()
      .then(setArticles)
      .catch((err: Error) => setError(err.message));
  }, []);

  return (
    <div className="flex flex-col gap-8">
      <div>
        <h1 className="text-3xl font-semibold tracking-tight">Catalogue</h1>
        <p className="mt-2 text-zinc-600 dark:text-zinc-400">
          Objets de collection entre particuliers — articles publiés après contrôle automatisé.
        </p>
      </div>

      {error && <p className="text-sm text-red-600 dark:text-red-400">{error}</p>}

      {articles === null && !error && <p className="text-sm text-zinc-500">Chargement du catalogue…</p>}

      {articles !== null && articles.length === 0 && (
        <p className="text-sm text-zinc-500">
          Aucun article en ligne pour le moment. Inscrivez-vous et devenez vendeur pour publier le premier !
        </p>
      )}

      {articles !== null && articles.length > 0 && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {articles.map((article) => (
            <ArticleCard key={article.id} article={article} />
          ))}
        </div>
      )}
    </div>
  );
}
