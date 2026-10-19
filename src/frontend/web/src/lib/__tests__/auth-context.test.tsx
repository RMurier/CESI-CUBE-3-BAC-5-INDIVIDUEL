import { act, renderHook, waitFor } from "@testing-library/react";
import { AuthProvider, useAuth } from "../auth-context";

function makeToken(claims: object) {
  return `h.${btoa(JSON.stringify(claims))}.s`;
}
const token = makeToken({ sub: "u1", email: "a@b.c", display_name: "Alice", exp: 1 });

const fetchMock = jest.fn();
beforeEach(() => {
  fetchMock.mockReset();
  global.fetch = fetchMock as unknown as typeof fetch;
  localStorage.clear();
});

async function mount() {
  const hook = renderHook(() => useAuth(), { wrapper: AuthProvider });
  await waitFor(() => expect(hook.result.current.loading).toBe(false));
  return hook.result;
}

describe("AuthProvider", () => {
  it("useAuth hors provider lève une erreur", () => {
    const spy = jest.spyOn(console, "error").mockImplementation(() => {});
    expect(() => renderHook(() => useAuth())).toThrow("AuthProvider");
    spy.mockRestore();
  });

  it("restaure la session depuis localStorage", async () => {
    localStorage.setItem(
      "collector.auth",
      JSON.stringify({ token, userId: "u1", email: "a@b.c", displayName: "Alice", roles: [] }),
    );
    const r = await mount();
    expect(r.current.auth?.displayName).toBe("Alice");
  });

  it("ignore un stockage corrompu", async () => {
    localStorage.setItem("collector.auth", "{pas du json");
    const r = await mount();
    expect(r.current.auth).toBeNull();
  });

  it("login réussi persiste la session, logout l'efface", async () => {
    fetchMock.mockResolvedValue({ ok: true, json: async () => ({ accessToken: token, roles: ["Buyer"] }) });
    const r = await mount();
    await act(() => r.current.login("a@b.c", "pw"));
    expect(r.current.auth?.displayName).toBe("Alice");
    expect(localStorage.getItem("collector.auth")).toContain("u1");
    act(() => r.current.logout());
    expect(r.current.auth).toBeNull();
    expect(localStorage.getItem("collector.auth")).toBeNull();
  });

  it("login: 401, autre erreur et jeton invalide", async () => {
    const r = await mount();
    fetchMock.mockResolvedValueOnce({ ok: false, status: 401 });
    await expect(r.current.login("a", "b")).rejects.toThrow("incorrect");
    fetchMock.mockResolvedValueOnce({ ok: false, status: 500 });
    await expect(r.current.login("a", "b")).rejects.toThrow("Connexion impossible.");
    fetchMock.mockResolvedValueOnce({ ok: true, json: async () => ({ accessToken: "nimporte" }) });
    await expect(r.current.login("a", "b")).rejects.toThrow("invalide");
  });

  it("register: 409, autre erreur, et succès enchaîné sur login", async () => {
    const r = await mount();
    fetchMock.mockResolvedValueOnce({ ok: false, status: 409 });
    await expect(r.current.register("a", "b", "c")).rejects.toThrow("existe déjà");
    fetchMock.mockResolvedValueOnce({ ok: false, status: 500 });
    await expect(r.current.register("a", "b", "c")).rejects.toThrow("Inscription impossible.");
    fetchMock
      .mockResolvedValueOnce({ ok: true })
      .mockResolvedValueOnce({ ok: true, json: async () => ({ accessToken: token }) });
    await act(() => r.current.register("a", "b", "c"));
    expect(r.current.auth?.displayName).toBe("Alice");
  });

  it("becomeSeller: refuse sans session, met à jour les rôles, gère l'échec", async () => {
    const r = await mount();
    await expect(r.current.becomeSeller()).rejects.toThrow("Connexion requise");
    fetchMock.mockResolvedValueOnce({ ok: true, json: async () => ({ accessToken: token, roles: [] }) });
    await act(() => r.current.login("a", "b"));
    fetchMock.mockResolvedValueOnce({ ok: true, json: async () => ({ roles: ["Buyer", "Seller"] }) });
    await act(() => r.current.becomeSeller());
    expect(r.current.auth?.roles).toContain("Seller");
    fetchMock.mockResolvedValueOnce({ ok: false });
    await expect(r.current.becomeSeller()).rejects.toThrow("devenir vendeur");
  });

  it("getToken rafraîchit un jeton expiré avec le refresh token et applique la rotation", async () => {
    const fresh = makeToken({ sub: "u1", email: "a@b.c", display_name: "Alice", exp: Math.floor(Date.now() / 1000) + 900 });
    fetchMock.mockResolvedValueOnce({ ok: true, json: async () => ({ accessToken: token, refreshToken: "r1", roles: [] }) });
    const r = await mount();
    await act(() => r.current.login("a", "b"));
    fetchMock.mockResolvedValueOnce({ ok: true, json: async () => ({ accessToken: fresh, refreshToken: "r2", roles: ["Buyer"] }) });
    let result = "";
    await act(async () => {
      result = await r.current.getToken();
    });
    expect(result).toBe(fresh);
    expect(r.current.auth?.refreshToken).toBe("r2");
  });

  it("getToken: refresh refusé = déconnexion", async () => {
    fetchMock.mockResolvedValueOnce({ ok: true, json: async () => ({ accessToken: token, refreshToken: "r1", roles: [] }) });
    const r = await mount();
    await act(() => r.current.login("a", "b"));
    fetchMock.mockResolvedValueOnce({ ok: false, status: 401 });
    await act(async () => {
      await expect(r.current.getToken()).rejects.toThrow("Session expirée");
    });
    expect(r.current.auth).toBeNull();
  });
});
