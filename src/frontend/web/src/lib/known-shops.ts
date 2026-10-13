/** Le service Catalog n'expose pas de "liste des boutiques d'un vendeur" (voir README) : on retient
 * localement, par utilisateur, les boutiques créées dans ce navigateur pour les retrouver facilement. */
export interface KnownShop {
  id: string;
  name: string;
}

const keyFor = (userId: string) => `collector.shops.${userId}`;

export function getKnownShops(userId: string): KnownShop[] {
  try {
    const raw = localStorage.getItem(keyFor(userId));
    return raw ? (JSON.parse(raw) as KnownShop[]) : [];
  } catch {
    return [];
  }
}

export function addKnownShop(userId: string, shop: KnownShop): void {
  const shops = getKnownShops(userId);
  if (shops.some((s) => s.id === shop.id)) return;
  try {
    localStorage.setItem(keyFor(userId), JSON.stringify([...shops, shop]));
  } catch {
    // stockage indisponible (navigation privée) : la boutique reste accessible via son URL directe.
  }
}
