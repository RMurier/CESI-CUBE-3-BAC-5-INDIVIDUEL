import { apiConfig } from "../config";

/** System.Text.Json sérialise les enums en entier par défaut (pas de JsonStringEnumConverter côté API). */
export const ArticleStatus = {
  PendingReview: 0,
  Published: 1,
  Rejected: 2,
  Sold: 3,
} as const;

export type ArticleStatusCode = (typeof ArticleStatus)[keyof typeof ArticleStatus];

export interface Article {
  id: string;
  shopId: string;
  title: string;
  description: string;
  price: number;
  shippingCost: number;
  categoryId: string | null;
  status: ArticleStatusCode;
  rejectionReason: string | null;
  submittedAt: string;
  publishedAt: string | null;
}

export interface Category {
  id: string;
  name: string;
  createdAt: string;
}

export interface ShopSummary {
  id: string;
  sellerId: string;
  name: string;
  description: string | null;
  createdAt: string;
}

export interface ShopDetail extends ShopSummary {
  articles: Article[];
}

export function articleStatusLabel(status: ArticleStatusCode): string {
  switch (status) {
    case ArticleStatus.Published:
      return "En ligne";
    case ArticleStatus.Rejected:
      return "Rejeté";
    case ArticleStatus.Sold:
      return "Vendu";
    default:
      return "En cours de contrôle";
  }
}

async function parseOrThrow<T>(res: Response, fallback: string): Promise<T> {
  if (!res.ok) {
    const body = await res.json().catch(() => null);
    throw new Error(body?.message ?? fallback);
  }
  return (await res.json()) as T;
}

export function listPublishedArticles(categoryId?: string): Promise<Article[]> {
  const query: string = categoryId ? `?categoryId=${encodeURIComponent(categoryId)}` : "";
  return fetch(`${apiConfig.catalog}/articles${query}`, { cache: "no-store" }).then((res) =>
    parseOrThrow(res, "Impossible de charger le catalogue."),
  );
}

export function listCategories(): Promise<Category[]> {
  return fetch(`${apiConfig.catalog}/categories`, { cache: "no-store" }).then((res) =>
    parseOrThrow(res, "Impossible de charger les catégories."),
  );
}

export function createCategory(token: string, name: string): Promise<Category> {
  return fetch(`${apiConfig.catalog}/categories`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify({ name }),
  }).then((res) => parseOrThrow(res, "Impossible de créer la catégorie."));
}

export function getArticle(id: string): Promise<Article> {
  return fetch(`${apiConfig.catalog}/articles/${id}`, { cache: "no-store" }).then((res) =>
    parseOrThrow(res, "Article introuvable."),
  );
}

export function getShop(id: string): Promise<ShopDetail> {
  return fetch(`${apiConfig.catalog}/shops/${id}`, { cache: "no-store" }).then((res) =>
    parseOrThrow(res, "Boutique introuvable."),
  );
}

export function createShop(token: string, name: string, description: string): Promise<ShopSummary> {
  return fetch(`${apiConfig.catalog}/shops`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify({ name, description: description || null }),
  }).then((res) => parseOrThrow(res, "Impossible de créer la boutique."));
}

export interface CreateArticleInput {
  title: string;
  description: string;
  price: number;
  shippingCost: number;
  categoryId: string;
}

export function createArticle(token: string, shopId: string, input: CreateArticleInput): Promise<Article> {
  return fetch(`${apiConfig.catalog}/shops/${shopId}/articles`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify(input),
  }).then((res) => parseOrThrow(res, "Impossible de soumettre l'article."));
}
