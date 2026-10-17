import { act, render, screen, waitFor } from "@testing-library/react";
import { AuthProvider, useAuth } from "../auth-context";

function makeToken(claims: object) {
  return `h.${btoa(JSON.stringify(claims))}.s`;
}
const token = makeToken({ sub: "u1", email: "a@b.c", display_name: "Alice", exp: 1 });

let ctx: ReturnType<typeof useAuth>;
function Probe() {
  ctx = useAuth();
  return <p>{ctx.loading ? "loading" : (ctx.auth?.displayName ?? "anon")}</p>;
}

const fetchMock = jest.fn();
beforeEach(() => {
  fetchMock.mockReset();
  global.fetch = fetchMock as unknown as typeof fetch;
  localStorage.clear();
});

async function mount() {
  render(
    <AuthProvider>
      <Probe />
    </AuthProvider>,
  );
  await waitFor(() => expect(ctx.loading).toBe(false));
}

describe("AuthProvider", () => {
  it("useAuth hors provider lève une erreur", () => {
    const spy = jest.spyOn(console, "error").mockImplementation(() => {});
    expect(() => render(<Probe />)).toThrow("AuthProvider");
    spy.mockRestore();
  });

  it("restaure la session depuis localStorage", async () => {
    localStorage.setItem(
      "collector.auth",
      JSON.stringify({ token, userId: "u1", email: "a@b.c", displayName: "Alice", roles: [] }),
    );
    await mount();
    expect(screen.getByText("Alice")).toBeInTheDocument();
  });

  it("ignore un stockage corrompu", async () => {
    localStorage.setItem("collector.auth", "{pas du json");
    await mount();
    expect(screen.getByText("anon")).toBeInTheDocument();
  });

  it("login réussi persiste la session, logout l'efface", async () => {
    fetchMock.mockResolvedValue({ ok: true, json: async () => ({ accessToken: token, roles: ["Buyer"] }) });
    await mount();
    await act(() => ctx.login("a@b.c", "pw"));
    expect(screen.getByText("Alice")).toBeInTheDocument();
    expect(localStorage.getItem("collector.auth")).toContain("u1");
    act(() => ctx.logout());
    expect(screen.getByText("anon")).toBeInTheDocument();
    expect(localStorage.getItem("collector.auth")).toBeNull();
  });

  it("login: 401, autre erreur et jeton invalide", async () => {
    await mount();
    fetchMock.mockResolvedValueOnce({ ok: false, status: 401 });
    await expect(ctx.login("a", "b")).rejects.toThrow("incorrect");
    fetchMock.mockResolvedValueOnce({ ok: false, status: 500 });
    await expect(ctx.login("a", "b")).rejects.toThrow("Connexion impossible.");
    fetchMock.mockResolvedValueOnce({ ok: true, json: async () => ({ accessToken: "nimporte" }) });
    await expect(ctx.login("a", "b")).rejects.toThrow("invalide");
  });

  it("register: 409, autre erreur, et succès enchaîné sur login", async () => {
    await mount();
    fetchMock.mockResolvedValueOnce({ ok: false, status: 409 });
    await expect(ctx.register("a", "b", "c")).rejects.toThrow("existe déjà");
    fetchMock.mockResolvedValueOnce({ ok: false, status: 500 });
    await expect(ctx.register("a", "b", "c")).rejects.toThrow("Inscription impossible.");
    fetchMock
      .mockResolvedValueOnce({ ok: true })
      .mockResolvedValueOnce({ ok: true, json: async () => ({ accessToken: token }) });
    await act(() => ctx.register("a", "b", "c"));
    expect(screen.getByText("Alice")).toBeInTheDocument();
  });

  it("becomeSeller: refuse sans session, met à jour les rôles, gère l'échec", async () => {
    await mount();
    await expect(ctx.becomeSeller()).rejects.toThrow("Connexion requise");
    fetchMock.mockResolvedValueOnce({ ok: true, json: async () => ({ accessToken: token, roles: [] }) });
    await act(() => ctx.login("a", "b"));
    fetchMock.mockResolvedValueOnce({ ok: true, json: async () => ({ roles: ["Buyer", "Seller"] }) });
    await act(() => ctx.becomeSeller());
    expect(ctx.auth?.roles).toContain("Seller");
    fetchMock.mockResolvedValueOnce({ ok: false });
    await expect(ctx.becomeSeller()).rejects.toThrow("devenir vendeur");
  });
});
