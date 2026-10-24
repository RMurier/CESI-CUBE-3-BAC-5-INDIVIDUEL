import { ArticleStatus, articleStatusLabel } from "../catalog";

describe("articleStatusLabel", () => {
  it("affiche un libellé distinct pour chacun des 4 statuts possibles", () => {
    expect(articleStatusLabel(ArticleStatus.PendingReview)).toBe("En cours de contrôle");
    expect(articleStatusLabel(ArticleStatus.Published)).toBe("En ligne");
    expect(articleStatusLabel(ArticleStatus.Rejected)).toBe("Rejeté");
    expect(articleStatusLabel(ArticleStatus.Sold)).toBe("Vendu");
  });
});

import { createArticle, createShop, getArticle, getShop, listPublishedArticles } from "../catalog";

describe("catalog api", () => {
  const fetchMock = jest.fn();
  beforeEach(() => {
    fetchMock.mockReset();
    global.fetch = fetchMock as unknown as typeof fetch;
  });

  it("liste, article et boutique renvoient le JSON", async () => {
    fetchMock.mockResolvedValue({ ok: true, json: async () => ({ id: "x" }) });
    await expect(listPublishedArticles()).resolves.toEqual({ id: "x" });
    await expect(getArticle("a")).resolves.toEqual({ id: "x" });
    await expect(getShop("s")).resolves.toEqual({ id: "x" });
  });

  it("createShop envoie un Bearer et description null si vide", async () => {
    fetchMock.mockResolvedValue({ ok: true, json: async () => ({ id: "s" }) });
    await createShop("tok", "Ma boutique", "");
    const init = fetchMock.mock.calls[0][1];
    expect(init.headers.Authorization).toBe("Bearer tok");
    expect(JSON.parse(init.body)).toEqual({ name: "Ma boutique", description: null });
  });

  it("createArticle poste sur la boutique", async () => {
    fetchMock.mockResolvedValue({ ok: true, json: async () => ({ id: "a" }) });
    await createArticle("tok", "s1", { title: "t", description: "d", price: 1, shippingCost: 0, categoryId: "c1" });
    expect(fetchMock.mock.calls[0][0]).toContain("/shops/s1/articles");
  });

  it("utilise le message d'erreur de l'API, sinon le message par défaut", async () => {
    fetchMock.mockResolvedValueOnce({ ok: false, json: async () => ({ message: "Boom" }) });
    await expect(getArticle("a")).rejects.toThrow("Boom");
    fetchMock.mockResolvedValueOnce({ ok: false, json: async () => Promise.reject(new Error("no body")) });
    await expect(getArticle("a")).rejects.toThrow("Article introuvable.");
  });
});
