import { ArticleStatus, articleStatusLabel } from "../catalog";

describe("articleStatusLabel", () => {
  it("affiche un libellé distinct pour chacun des 3 statuts possibles", () => {
    expect(articleStatusLabel(ArticleStatus.PendingReview)).toBe("En cours de contrôle");
    expect(articleStatusLabel(ArticleStatus.Published)).toBe("En ligne");
    expect(articleStatusLabel(ArticleStatus.Rejected)).toBe("Rejeté");
  });
});
