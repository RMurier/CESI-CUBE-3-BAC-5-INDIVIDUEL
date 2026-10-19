import { createOrder, getOrder, listMyOrders } from "../orders";

describe("orders api", () => {
  const fetchMock = jest.fn();
  beforeEach(() => {
    fetchMock.mockReset();
    global.fetch = fetchMock as unknown as typeof fetch;
  });

  it("createOrder poste l'article avec un Bearer et renvoie l'URL de paiement", async () => {
    fetchMock.mockResolvedValue({ ok: true, json: async () => ({ orderId: "o1", checkoutUrl: "https://checkout.stripe.com/x" }) });
    await expect(createOrder("tok", "a1")).resolves.toEqual({ orderId: "o1", checkoutUrl: "https://checkout.stripe.com/x" });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toContain("/orders");
    expect(init.headers.Authorization).toBe("Bearer tok");
    expect(JSON.parse(init.body)).toEqual({ articleId: "a1" });
  });

  it("remonte le message d'erreur du serveur", async () => {
    fetchMock.mockResolvedValue({ ok: false, json: async () => ({ message: "Cet article n'est plus disponible." }) });
    await expect(createOrder("tok", "a1")).rejects.toThrow("plus disponible");
  });

  it("utilise le message par défaut si le corps n'est pas du JSON", async () => {
    fetchMock.mockResolvedValue({ ok: false, json: async () => { throw new Error("no body"); } });
    await expect(getOrder("tok", "o1")).rejects.toThrow("Commande introuvable.");
    await expect(listMyOrders("tok")).rejects.toThrow("Impossible de charger vos commandes.");
  });
});
