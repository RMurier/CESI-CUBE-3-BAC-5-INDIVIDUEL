import { render, screen } from "@testing-library/react";
import CataloguePage from "../page";
import { ArticleDetailClient } from "../articles/[id]/ArticleDetailClient";
import ArticleDetailPage from "../articles/[id]/page";
import ShopDetailPage from "../boutiques/[shopId]/page";
import { getArticle, listCategories, listPublishedArticles, type Article } from "@/lib/api/catalog";

jest.mock("@/lib/api/catalog", () => ({
  ...jest.requireActual("@/lib/api/catalog"),
  listPublishedArticles: jest.fn(),
  listCategories: jest.fn(),
  getArticle: jest.fn(),
}));
jest.mock("@/lib/auth-context", () => ({ useAuth: () => ({ auth: null, getToken: jest.fn() }) }));
jest.mock("../boutiques/[shopId]/ShopDetailClient", () => ({
  ShopDetailClient: ({ shopId }: { shopId: string }) => <p>shop:{shopId}</p>,
}));

const article: Article = {
  id: "a1",
  shopId: "s1",
  title: "Figurine rare",
  description: "Très belle pièce",
  price: 10,
  shippingCost: 2,
  categoryId: null,
  status: 1,
  rejectionReason: null,
  submittedAt: "2026-01-01T10:00:00Z",
  publishedAt: "2026-01-02T10:00:00Z",
};

describe("CataloguePage", () => {
  beforeEach(() => {
    (listCategories as jest.Mock).mockResolvedValue([]);
  });

  it("affiche les articles", async () => {
    (listPublishedArticles as jest.Mock).mockResolvedValue([article]);
    render(<CataloguePage />);
    expect(screen.getByText(/Chargement du catalogue/)).toBeInTheDocument();
    expect(await screen.findByText("Figurine rare")).toBeInTheDocument();
  });

  it("affiche un message si vide", async () => {
    (listPublishedArticles as jest.Mock).mockResolvedValue([]);
    render(<CataloguePage />);
    expect(await screen.findByText(/Aucun article en ligne/)).toBeInTheDocument();
  });

  it("affiche l'erreur", async () => {
    (listPublishedArticles as jest.Mock).mockRejectedValue(new Error("KO"));
    render(<CataloguePage />);
    expect(await screen.findByText("KO")).toBeInTheDocument();
  });
});

describe("ArticleDetailClient", () => {
  it("affiche le détail publié", async () => {
    (getArticle as jest.Mock).mockResolvedValue(article);
    render(<ArticleDetailClient id="a1" />);
    expect(screen.getByText(/Chargement de l/)).toBeInTheDocument();
    expect(await screen.findByText("Figurine rare")).toBeInTheDocument();
    expect(screen.getByText("Publié le")).toBeInTheDocument();
    expect(screen.getByText(/Connectez-vous/)).toBeInTheDocument(); // visiteur : pas de bouton Acheter
  });

  it("affiche le motif de rejet", async () => {
    (getArticle as jest.Mock).mockResolvedValue({ ...article, status: 2, publishedAt: null, rejectionReason: "Interdit" });
    render(<ArticleDetailClient id="a1" />);
    expect(await screen.findByText(/Motif du rejet : Interdit/)).toBeInTheDocument();
    expect(screen.queryByText("Publié le")).not.toBeInTheDocument();
  });

  it("affiche l'erreur", async () => {
    (getArticle as jest.Mock).mockRejectedValue(new Error("Introuvable"));
    render(<ArticleDetailClient id="a1" />);
    expect(await screen.findByText("Introuvable")).toBeInTheDocument();
  });
});

describe("pages serveur", () => {
  it("résolvent leurs params", async () => {
    render(await ArticleDetailPage({ params: Promise.resolve({ id: "a1" }) } as never));
    render(await ShopDetailPage({ params: Promise.resolve({ shopId: "s9" }) } as never));
    expect(screen.getByText("shop:s9")).toBeInTheDocument();
  });
});
