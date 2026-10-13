import { render, screen } from "@testing-library/react";
import { ArticleCard } from "../ArticleCard";
import { ArticleStatus, type Article } from "@/lib/api/catalog";

const article: Article = {
  id: "11111111-1111-1111-1111-111111111111",
  shopId: "shop-1",
  title: "Figurine rare",
  description: "Une figurine de collection en excellent état.",
  price: 42,
  shippingCost: 5.5,
  status: ArticleStatus.Published,
  rejectionReason: null,
  submittedAt: "2026-10-01T10:00:00Z",
  publishedAt: "2026-10-01T10:05:00Z",
};

describe("<ArticleCard />", () => {
  it("affiche le titre, le prix et un lien vers le détail de l'article", () => {
    render(<ArticleCard article={article} />);

    expect(screen.getByText("Figurine rare")).toBeInTheDocument();
    expect(screen.getByText("42,00 €")).toBeInTheDocument();
    expect(screen.getByRole("link")).toHaveAttribute("href", `/articles/${article.id}`);
  });
});
