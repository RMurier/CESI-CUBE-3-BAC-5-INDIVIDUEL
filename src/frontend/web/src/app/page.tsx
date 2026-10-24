"use client";

import { useEffect, useState } from "react";
import { listCategories, listPublishedArticles, type Article, type Category } from "@/lib/api/catalog";
import { ArticleCard } from "@/components/ArticleCard";

export default function CataloguePage() {
  const [articles, setArticles] = useState<Article[] | null>(null);
  const [categories, setCategories] = useState<Category[]>([]);
  const [selectedCategoryId, setSelectedCategoryId] = useState<string>("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    listCategories()
      .then(setCategories)
      .catch(() => setCategories([])); // non bloquant : le catalogue reste consultable sans filtre
  }, []);

  useEffect(() => {
    // Pas de setArticles(null) synchrone ici (react-hooks/set-state-in-effect) : le catalogue
    // précédent reste affiché jusqu'à l'arrivée du nouveau filtre plutôt qu'un flash de chargement.
    listPublishedArticles(selectedCategoryId || undefined)
      .then(setArticles)
      .catch((err: Error) => setError(err.message));
  }, [selectedCategoryId]);

  return (
    <div className="flex flex-col gap-8">
      <div>
        <h1 className="text-3xl font-semibold tracking-tight">Catalogue</h1>
        <p className="mt-2 text-zinc-600 dark:text-zinc-400">
          Objets de collection entre particuliers — articles publiés après contrôle automatisé.
        </p>
      </div>

      {categories.length > 0 && (
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            onClick={() => setSelectedCategoryId("")}
            className={`rounded-full border px-4 py-1.5 text-sm transition-colors ${
              selectedCategoryId === ""
                ? "border-foreground bg-foreground text-background"
                : "border-black/[.08] dark:border-white/[.145]"
            }`}
          >
            Toutes
          </button>
          {categories.map((category) => (
            <button
              key={category.id}
              type="button"
              onClick={() => setSelectedCategoryId(category.id)}
              className={`rounded-full border px-4 py-1.5 text-sm transition-colors ${
                selectedCategoryId === category.id
                  ? "border-foreground bg-foreground text-background"
                  : "border-black/[.08] dark:border-white/[.145]"
              }`}
            >
              {category.name}
            </button>
          ))}
        </div>
      )}

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
