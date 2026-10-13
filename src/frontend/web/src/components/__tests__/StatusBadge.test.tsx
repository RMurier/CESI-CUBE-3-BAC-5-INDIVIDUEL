import { render, screen } from "@testing-library/react";
import { StatusBadge } from "../StatusBadge";
import { ArticleStatus } from "@/lib/api/catalog";

describe("<StatusBadge />", () => {
  it("affiche 'En ligne' pour un article publié", () => {
    render(<StatusBadge status={ArticleStatus.Published} />);
    expect(screen.getByText("En ligne")).toBeInTheDocument();
  });

  it("affiche 'Rejeté' pour un article rejeté", () => {
    render(<StatusBadge status={ArticleStatus.Rejected} />);
    expect(screen.getByText("Rejeté")).toBeInTheDocument();
  });

  it("affiche 'En cours de contrôle' pour un article en attente de modération", () => {
    render(<StatusBadge status={ArticleStatus.PendingReview} />);
    expect(screen.getByText("En cours de contrôle")).toBeInTheDocument();
  });
});
