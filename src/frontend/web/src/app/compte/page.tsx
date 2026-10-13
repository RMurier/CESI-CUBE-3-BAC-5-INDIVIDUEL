"use client";

import { useEffect, useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useAuth } from "@/lib/auth-context";
import { createShop } from "@/lib/api/catalog";
import { addKnownShop, getKnownShops, type KnownShop } from "@/lib/known-shops";

export default function AccountPage() {
  const { auth, loading, becomeSeller } = useAuth();
  const router = useRouter();

  const [shops, setShops] = useState<KnownShop[]>([]);
  const [becoming, setBecoming] = useState(false);
  const [becomeError, setBecomeError] = useState<string | null>(null);

  const [shopName, setShopName] = useState("");
  const [shopDescription, setShopDescription] = useState("");
  const [creatingShop, setCreatingShop] = useState(false);
  const [shopError, setShopError] = useState<string | null>(null);

  useEffect(() => {
    if (!loading && !auth) router.push("/login");
  }, [loading, auth, router]);

  useEffect(() => {
    if (auth) setShops(getKnownShops(auth.userId));
  }, [auth]);

  if (loading || !auth) return <p className="text-sm text-zinc-500">Chargement…</p>;

  const isSeller = auth.roles.includes("Seller");

  async function handleBecomeSeller() {
    setBecomeError(null);
    setBecoming(true);
    try {
      await becomeSeller();
    } catch (err) {
      setBecomeError(err instanceof Error ? err.message : "Impossible de devenir vendeur.");
    } finally {
      setBecoming(false);
    }
  }

  async function handleCreateShop(event: FormEvent) {
    event.preventDefault();
    setShopError(null);
    setCreatingShop(true);
    try {
      const shop = await createShop(auth!.token, shopName, shopDescription);
      addKnownShop(auth!.userId, { id: shop.id, name: shop.name });
      setShops(getKnownShops(auth!.userId));
      setShopName("");
      setShopDescription("");
    } catch (err) {
      setShopError(err instanceof Error ? err.message : "Impossible de créer la boutique.");
    } finally {
      setCreatingShop(false);
    }
  }

  return (
    <div className="flex flex-col gap-10">
      <div>
        <h1 className="text-3xl font-semibold tracking-tight">Mon espace</h1>
        <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
          {auth.displayName} — {auth.email} — rôles : {auth.roles.join(", ")}
        </p>
      </div>

      {!isSeller && (
        <section className="flex flex-col gap-3 rounded-xl border border-black/[.08] p-5 dark:border-white/[.145]">
          <h2 className="text-lg font-semibold">Devenir vendeur</h2>
          <p className="text-sm text-zinc-600 dark:text-zinc-400">
            Créez des boutiques virtuelles et publiez des articles après inscription vendeur.
          </p>
          {becomeError && <p className="text-sm text-red-600 dark:text-red-400">{becomeError}</p>}
          <button
            onClick={handleBecomeSeller}
            disabled={becoming}
            className="w-fit rounded-full bg-foreground px-5 py-2 text-background transition-colors hover:bg-[#383838] disabled:opacity-50 dark:hover:bg-[#ccc]"
          >
            {becoming ? "En cours…" : "Devenir vendeur"}
          </button>
        </section>
      )}

      {isSeller && (
        <section className="flex flex-col gap-6">
          <div className="flex flex-col gap-3 rounded-xl border border-black/[.08] p-5 dark:border-white/[.145]">
            <h2 className="text-lg font-semibold">Créer une boutique</h2>
            <form onSubmit={handleCreateShop} className="flex flex-col gap-3">
              <label className="flex flex-col gap-1 text-sm">
                Nom
                <input
                  required
                  value={shopName}
                  onChange={(e) => setShopName(e.target.value)}
                  className="rounded-lg border border-black/[.08] bg-transparent px-3 py-2 dark:border-white/[.145]"
                />
              </label>
              <label className="flex flex-col gap-1 text-sm">
                Description (optionnelle)
                <textarea
                  value={shopDescription}
                  onChange={(e) => setShopDescription(e.target.value)}
                  className="rounded-lg border border-black/[.08] bg-transparent px-3 py-2 dark:border-white/[.145]"
                />
              </label>
              {shopError && <p className="text-sm text-red-600 dark:text-red-400">{shopError}</p>}
              <button
                type="submit"
                disabled={creatingShop}
                className="w-fit rounded-full bg-foreground px-5 py-2 text-background transition-colors hover:bg-[#383838] disabled:opacity-50 dark:hover:bg-[#ccc]"
              >
                {creatingShop ? "Création…" : "Créer la boutique"}
              </button>
            </form>
          </div>

          <div className="flex flex-col gap-3">
            <h2 className="text-lg font-semibold">Mes boutiques</h2>
            {shops.length === 0 ? (
              <p className="text-sm text-zinc-500">Aucune boutique créée pour l&apos;instant.</p>
            ) : (
              <ul className="flex flex-col gap-2">
                {shops.map((shop) => (
                  <li key={shop.id}>
                    <Link
                      href={`/boutiques/${shop.id}`}
                      className="rounded-lg border border-black/[.08] px-4 py-3 block hover:border-black/[.2] dark:border-white/[.145] dark:hover:border-white/[.3]"
                    >
                      {shop.name}
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </section>
      )}
    </div>
  );
}
