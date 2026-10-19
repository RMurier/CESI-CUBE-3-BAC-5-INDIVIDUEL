import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import AccountPage from "../compte/page";
import NotificationsPage from "../notifications/page";
import { useAuth } from "@/lib/auth-context";
import { createShop } from "@/lib/api/catalog";
import { listNotifications, markRead, subscribe } from "@/lib/api/notification";

const push = jest.fn();
jest.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));
jest.mock("@/lib/auth-context", () => ({ useAuth: jest.fn() }));
jest.mock("@/components/PayoutSetup", () => ({ PayoutSetup: () => <p>payout-setup</p> }));
jest.mock("@/lib/api/catalog", () => ({ createShop: jest.fn() }));
jest.mock("@/lib/api/notification", () => ({
  listNotifications: jest.fn(),
  markRead: jest.fn(),
  subscribe: jest.fn(),
}));

const buyer = { token: "t", userId: "u1", email: "a@b.c", displayName: "Alice", roles: ["Buyer"] };
const seller = { ...buyer, roles: ["Buyer", "Seller"] };

beforeEach(() => {
  push.mockReset();
  localStorage.clear();
  jest.clearAllMocks();
});

describe("AccountPage", () => {
  it("redirige vers /login sans session", () => {
    (useAuth as jest.Mock).mockReturnValue({ auth: null, loading: false });
    render(<AccountPage />);
    expect(push).toHaveBeenCalledWith("/login");
  });

  it("affiche chargement tant que loading", () => {
    (useAuth as jest.Mock).mockReturnValue({ auth: null, loading: true });
    render(<AccountPage />);
    expect(screen.getByText("Chargement…")).toBeInTheDocument();
    expect(push).not.toHaveBeenCalled();
  });

  it("permet de devenir vendeur et gère l'échec", async () => {
    const becomeSeller = jest.fn().mockRejectedValueOnce(new Error("Non")).mockRejectedValueOnce("x").mockResolvedValue(undefined);
    (useAuth as jest.Mock).mockReturnValue({ auth: buyer, loading: false, becomeSeller });
    render(<AccountPage />);
    const btn = screen.getByRole("button", { name: "Devenir vendeur" });
    fireEvent.click(btn);
    expect(await screen.findByText("Non")).toBeInTheDocument();
    fireEvent.click(btn);
    expect(await screen.findByText("Impossible de devenir vendeur.")).toBeInTheDocument();
    fireEvent.click(btn);
    await waitFor(() => expect(becomeSeller).toHaveBeenCalledTimes(3));
  });

  it("crée une boutique, la mémorise et l'affiche", async () => {
    (useAuth as jest.Mock).mockReturnValue({ auth: seller, loading: false, getToken: async () => "t" });
    (createShop as jest.Mock).mockResolvedValue({ id: "s1", name: "Ma boutique" });
    render(<AccountPage />);
    expect(screen.getByText(/Aucune boutique créée/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText(/Nom/), { target: { value: "Ma boutique" } });
    fireEvent.change(screen.getByLabelText(/Description/), { target: { value: "desc" } });
    fireEvent.click(screen.getByRole("button", { name: "Créer la boutique" }));
    await waitFor(() => expect(createShop).toHaveBeenCalledWith("t", "Ma boutique", "desc"));
    expect(await screen.findByText("Ma boutique", { selector: "a" })).toBeInTheDocument();
  });

  it("affiche l'erreur de création de boutique", async () => {
    (useAuth as jest.Mock).mockReturnValue({ auth: seller, loading: false, getToken: async () => "t" });
    (createShop as jest.Mock).mockRejectedValueOnce(new Error("Refusé")).mockRejectedValueOnce("x");
    render(<AccountPage />);
    fireEvent.change(screen.getByLabelText(/Nom/), { target: { value: "B" } });
    fireEvent.click(screen.getByRole("button", { name: "Créer la boutique" }));
    expect(await screen.findByText("Refusé")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Créer la boutique" }));
    expect(await screen.findByText("Impossible de créer la boutique.")).toBeInTheDocument();
  });
});

describe("NotificationsPage", () => {
  const notif = (id: string, read: boolean) => ({ id, userId: "u1", message: `msg-${id}`, read, createdAt: "2026-01-01T10:00:00Z" });

  it("redirige sans session / chargement", () => {
    (useAuth as jest.Mock).mockReturnValue({ auth: null, loading: false });
    render(<NotificationsPage />);
    expect(push).toHaveBeenCalledWith("/login");
    expect(screen.getByText("Chargement…")).toBeInTheDocument();
  });

  it("liste les notifications et marque comme lue", async () => {
    (useAuth as jest.Mock).mockReturnValue({ auth: buyer, loading: false });
    (listNotifications as jest.Mock).mockResolvedValue([notif("1", false), notif("2", true)]);
    (markRead as jest.Mock).mockResolvedValue(undefined);
    render(<NotificationsPage />);
    expect(await screen.findByText("msg-1")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Marquer comme lue" }));
    await waitFor(() => expect(markRead).toHaveBeenCalledWith("1"));
    await waitFor(() => expect(listNotifications).toHaveBeenCalledTimes(2));
  });

  it("affiche vide, et les erreurs de chargement / marquage", async () => {
    (useAuth as jest.Mock).mockReturnValue({ auth: buyer, loading: false });
    (listNotifications as jest.Mock).mockResolvedValueOnce([]);
    const { unmount } = render(<NotificationsPage />);
    expect(await screen.findByText(/Aucune notification/)).toBeInTheDocument();
    unmount();

    (listNotifications as jest.Mock).mockRejectedValueOnce(new Error("Chargement KO"));
    const second = render(<NotificationsPage />);
    expect(await screen.findByText("Chargement KO")).toBeInTheDocument();
    second.unmount();

    (listNotifications as jest.Mock).mockResolvedValue([notif("1", false)]);
    (markRead as jest.Mock).mockRejectedValueOnce(new Error("Marquage KO")).mockRejectedValueOnce("x");
    render(<NotificationsPage />);
    const btn = await screen.findByRole("button", { name: "Marquer comme lue" });
    fireEvent.click(btn);
    expect(await screen.findByText("Marquage KO")).toBeInTheDocument();
    fireEvent.click(btn);
    expect(await screen.findByText("Impossible de marquer comme lue.")).toBeInTheDocument();
  });

  it("s'abonne à un mot-clé et gère l'échec", async () => {
    (useAuth as jest.Mock).mockReturnValue({ auth: buyer, loading: false });
    (listNotifications as jest.Mock).mockResolvedValue([]);
    (subscribe as jest.Mock).mockResolvedValueOnce(undefined).mockRejectedValueOnce(new Error("Abo KO")).mockRejectedValueOnce("x");
    render(<NotificationsPage />);
    const input = screen.getByLabelText(/Mot-clé/);
    const submit = screen.getByRole("button", { name: "S'abonner" });
    fireEvent.change(input, { target: { value: "figurine" } });
    fireEvent.click(submit);
    expect(await screen.findByText(/Abonnement créé pour « figurine »/)).toBeInTheDocument();
    fireEvent.change(input, { target: { value: "k" } });
    fireEvent.click(submit);
    expect(await screen.findByText("Abo KO")).toBeInTheDocument();
    fireEvent.change(input, { target: { value: "k" } });
    fireEvent.click(submit);
    expect(await screen.findByText("Impossible de créer l'abonnement.")).toBeInTheDocument();
  });
});
