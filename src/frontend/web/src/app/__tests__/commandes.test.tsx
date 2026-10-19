import { render, screen } from "@testing-library/react";
import OrdersPage from "../commandes/page";
import { OrderDetailClient } from "../commandes/[id]/OrderDetailClient";
import { BuyButton } from "@/components/BuyButton";
import { useAuth } from "@/lib/auth-context";
import { getOrder, listMyOrders, type Order } from "@/lib/api/orders";

jest.mock("@/lib/auth-context", () => ({ useAuth: jest.fn() }));
jest.mock("@/lib/api/orders", () => ({
  ...jest.requireActual("@/lib/api/orders"),
  getOrder: jest.fn(),
  listMyOrders: jest.fn(),
  createOrder: jest.fn(),
}));

const order: Order = {
  id: "o1",
  articleId: "a1",
  articleTitle: "Poster dédicacé",
  itemPrice: 100,
  shippingCost: 10,
  totalAmount: 110,
  commissionAmount: 5.5,
  status: "Paid",
  createdAt: "2026-01-01T10:00:00Z",
  paidAt: "2026-01-01T10:05:00Z",
};
const signedIn = { auth: { token: "t", userId: "u1" }, loading: false, getToken: async () => "t" };

describe("commandes", () => {
  it("la liste affiche les commandes de l'acheteur", async () => {
    (useAuth as jest.Mock).mockReturnValue(signedIn);
    (listMyOrders as jest.Mock).mockResolvedValue([order]);
    render(<OrdersPage />);
    expect(await screen.findByText("Poster dédicacé")).toBeInTheDocument();
    expect(screen.getByText(/Payée/)).toBeInTheDocument();
  });

  it("demande de se connecter sans session", () => {
    (useAuth as jest.Mock).mockReturnValue({ auth: null, loading: false, getToken: jest.fn() });
    render(<OrdersPage />);
    expect(screen.getByText(/Connectez-vous/)).toBeInTheDocument();
  });

  it("le détail affiche le statut confirmé par le serveur", async () => {
    (useAuth as jest.Mock).mockReturnValue(signedIn);
    (getOrder as jest.Mock).mockResolvedValue(order);
    render(<OrderDetailClient id="o1" />);
    expect(await screen.findByRole("status")).toHaveTextContent("Payée");
  });

  it("le détail indique que la confirmation est en cours tant que la commande est Pending", async () => {
    (useAuth as jest.Mock).mockReturnValue(signedIn);
    (getOrder as jest.Mock).mockResolvedValue({ ...order, status: "Pending", paidAt: null });
    render(<OrderDetailClient id="o1" />);
    expect(await screen.findByRole("status")).toHaveTextContent("confirmation du paiement en cours");
  });
});

describe("BuyButton", () => {
  it("propose la connexion à un visiteur", () => {
    (useAuth as jest.Mock).mockReturnValue({ auth: null, getToken: jest.fn() });
    render(<BuyButton articleId="a1" />);
    expect(screen.getByText(/Connectez-vous/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Acheter" })).not.toBeInTheDocument();
  });

  it("affiche le bouton Acheter à un utilisateur connecté", () => {
    (useAuth as jest.Mock).mockReturnValue(signedIn);
    render(<BuyButton articleId="a1" />);
    expect(screen.getByRole("button", { name: "Acheter" })).toBeInTheDocument();
  });
});
