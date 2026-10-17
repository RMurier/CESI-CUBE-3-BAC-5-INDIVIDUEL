import { listNotifications, markRead, subscribe } from "../notification";

const fetchMock = jest.fn();
beforeEach(() => {
  fetchMock.mockReset();
  global.fetch = fetchMock as unknown as typeof fetch;
});

describe("notification api", () => {
  it("subscribe envoie userId et mot-clé", async () => {
    fetchMock.mockResolvedValue({ ok: true });
    await subscribe("u1", "figurine");
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toContain("/subscriptions");
    expect(JSON.parse(init.body)).toEqual({ userId: "u1", keyword: "figurine" });
  });

  it("subscribe échoue si la réponse n'est pas ok", async () => {
    fetchMock.mockResolvedValue({ ok: false });
    await expect(subscribe("u1", "x")).rejects.toThrow("abonnement");
  });

  it("listNotifications renvoie le JSON", async () => {
    fetchMock.mockResolvedValue({ ok: true, json: async () => [{ id: "n1" }] });
    await expect(listNotifications("u1")).resolves.toEqual([{ id: "n1" }]);
  });

  it("listNotifications échoue si la réponse n'est pas ok", async () => {
    fetchMock.mockResolvedValue({ ok: false });
    await expect(listNotifications("u1")).rejects.toThrow("notifications");
  });

  it("markRead appelle l'endpoint read", async () => {
    fetchMock.mockResolvedValue({ ok: true });
    await markRead("n1");
    expect(fetchMock.mock.calls[0][0]).toContain("/notifications/n1/read");
  });

  it("markRead échoue si la réponse n'est pas ok", async () => {
    fetchMock.mockResolvedValue({ ok: false });
    await expect(markRead("n1")).rejects.toThrow("lue");
  });
});
