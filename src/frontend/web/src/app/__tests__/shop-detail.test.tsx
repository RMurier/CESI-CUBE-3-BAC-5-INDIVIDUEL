import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { ShopDetailClient } from "../boutiques/[shopId]/ShopDetailClient";
import { useAuth } from "@/lib/auth-context";
import { createArticle, getShop } from "@/lib/api/catalog";

jest.mock("@/lib/auth-context", () => ({ useAuth: jest.fn() }));
jest.mock("@/lib/api/catalog", () => ({
  ...jest.requireActual("@/lib/api/catalog"),
  getShop: jest.fn(),
  createArticle: jest.fn(),
}));

const shop = {
  id: "s1",
  sellerId: "u1",
  name: "Ma boutique",
  description: "Une description",
  createdAt: "2026-01-01T00:00:00Z",
  articles: [
    { id: "a1", shopId: "s1", title: "Objet", description: "d", price: 5, shippingCost: 1, status: 0, rejectionReason: null, submittedAt: "x", publishedAt: null },
  ],
};

function type(label: RegExp, value: string) {
  fireEvent.change(screen.getByLabelText(label), { target: { value } });
}

describe("ShopDetailClient", () => {
  it("affiche la boutique et ses articles sans formulaire pour un visiteur", async () => {
    (useAuth as jest.Mock).mockReturnValue({ auth: null });
    (getShop as jest.Mock).mockResolvedValue(shop);
    render(<ShopDetailClient shopId="s1" />);
    expect(screen.getByText(/Chargement de la boutique/)).toBeInTheDocument();
    expect(await screen.findByText("Ma boutique")).toBeInTheDocument();
    expect(screen.getByText("Objet")).toBeInTheDocument();
    expect(screen.queryByText("Publier un article")).not.toBeInTheDocument();
  });

  it("affiche un message si aucun article", async () => {
    (useAuth as jest.Mock).mockReturnValue({ auth: null });
    (getShop as jest.Mock).mockResolvedValue({ ...shop, description: null, articles: [] });
    render(<ShopDetailClient shopId="s1" />);
    expect(await screen.findByText(/Aucun article pour/)).toBeInTheDocument();
  });

  it("affiche l'erreur", async () => {
    (useAuth as jest.Mock).mockReturnValue({ auth: null });
    (getShop as jest.Mock).mockRejectedValue(new Error("Introuvable"));
    render(<ShopDetailClient shopId="s1" />);
    expect(await screen.findByText("Introuvable")).toBeInTheDocument();
  });

  it("rafraîchit périodiquement", async () => {
    jest.useFakeTimers();
    (useAuth as jest.Mock).mockReturnValue({ auth: null });
    (getShop as jest.Mock).mockResolvedValue(shop);
    render(<ShopDetailClient shopId="s1" />);
    await act(async () => {
      jest.advanceTimersByTime(3100);
    });
    expect((getShop as jest.Mock).mock.calls.length).toBeGreaterThanOrEqual(2);
    jest.useRealTimers();
  });

  it("le propriétaire soumet un article, puis voit les erreurs", async () => {
    (useAuth as jest.Mock).mockReturnValue({ auth: { token: "t", userId: "u1" }, getToken: async () => "t" });
    (getShop as jest.Mock).mockResolvedValue(shop);
    (createArticle as jest.Mock).mockResolvedValueOnce({}).mockRejectedValueOnce(new Error("Refusé")).mockRejectedValueOnce("x");
    render(<ShopDetailClient shopId="s1" />);
    await screen.findByText("Publier un article");
    const fillAll = () => {
      type(/Titre/, "Titre");
      type(/Description/, "Une longue description");
      type(/Prix/, "12.5");
      type(/Frais de port/, "3");
    };
    const submit = () => fireEvent.click(screen.getByRole("button", { name: "Soumettre l'article" }));

    fillAll();
    submit();
    await waitFor(() =>
      expect(createArticle).toHaveBeenCalledWith("t", "s1", { title: "Titre", description: "Une longue description", price: 12.5, shippingCost: 3 }),
    );
    fillAll();
    submit();
    expect(await screen.findByText("Refusé")).toBeInTheDocument();
    fillAll();
    submit();
    expect(await screen.findByText("Impossible de soumettre l'article.")).toBeInTheDocument();
  });
});
