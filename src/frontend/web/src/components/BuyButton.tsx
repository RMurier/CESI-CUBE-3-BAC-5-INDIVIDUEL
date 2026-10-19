"use client";

import Link from "next/link";
import { useState } from "react";
import { createOrder } from "@/lib/api/orders";
import { useAuth } from "@/lib/auth-context";

export function BuyButton({ articleId }: { articleId: string }) {
  const { auth, getToken } = useAuth();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!auth) {
    return (
      <p className="text-sm text-zinc-600 dark:text-zinc-400">
        <Link href="/login" className="font-medium underline">
          Connectez-vous
        </Link>{" "}
        pour acheter cet article.
      </p>
    );
  }

  async function handleBuy() {
    setError(null);
    setPending(true);
    try {
      const { checkoutUrl } = await createOrder(await getToken(), articleId);
      window.location.assign(checkoutUrl);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Impossible de lancer le paiement.");
      setPending(false);
    }
  }

  return (
    <div className="flex flex-col gap-2">
      <button
        type="button"
        onClick={handleBuy}
        disabled={pending}
        className="w-fit rounded-full bg-foreground px-6 py-2.5 text-sm font-medium text-background disabled:opacity-60"
      >
        {pending ? "Redirection vers le paiement…" : "Acheter"}
      </button>
      {error && <p className="text-sm text-red-600 dark:text-red-400">{error}</p>}
    </div>
  );
}
