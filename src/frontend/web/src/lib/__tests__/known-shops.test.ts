import { addKnownShop, getKnownShops } from "../known-shops";

describe("known-shops (contournement de l'absence d'un GET /shops?sellerId côté Catalog.Api)", () => {
  afterEach(() => {
    localStorage.clear();
  });

  it("ne retourne aucune boutique pour un utilisateur sans historique", () => {
    expect(getKnownShops("user-1")).toEqual([]);
  });

  it("retient une boutique créée et la restitue pour le même utilisateur", () => {
    addKnownShop("user-1", { id: "shop-1", name: "Ma boutique" });

    expect(getKnownShops("user-1")).toEqual([{ id: "shop-1", name: "Ma boutique" }]);
  });

  it("ne mélange pas les boutiques de deux utilisateurs différents", () => {
    addKnownShop("user-1", { id: "shop-1", name: "Boutique A" });
    addKnownShop("user-2", { id: "shop-2", name: "Boutique B" });

    expect(getKnownShops("user-1")).toEqual([{ id: "shop-1", name: "Boutique A" }]);
    expect(getKnownShops("user-2")).toEqual([{ id: "shop-2", name: "Boutique B" }]);
  });

  it("ignore les doublons (même id de boutique ajouté deux fois)", () => {
    addKnownShop("user-1", { id: "shop-1", name: "Ma boutique" });
    addKnownShop("user-1", { id: "shop-1", name: "Ma boutique" });

    expect(getKnownShops("user-1")).toHaveLength(1);
  });
});
