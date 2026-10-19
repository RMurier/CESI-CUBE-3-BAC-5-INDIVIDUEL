"use client";

import { useEffect, useState } from "react";
import { getPayoutStatus, startPayoutOnboarding, type PayoutStatus } from "@/lib/api/orders";
import { useAuth } from "@/lib/auth-context";

/** Un vendeur doit avoir un compte Stripe Connect actif pour que ses articles soient achetables (versement du net, 5 % de commission retenus). */
export function PayoutSetup() {
  const { getToken } = useAuth();
  const [status, setStatus] = useState<PayoutStatus | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  useEffect(() => {
    getToken()
      .then(getPayoutStatus)
      .then(setStatus)
      .catch((err: Error) => setError(err.message));
    // getToken change à chaque rendu du provider : volontairement hors des dépendances.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function handleSetup() {
    setError(null);
    setPending(true);
    try {
      const { url } = await startPayoutOnboarding(await getToken());
      window.location.assign(url);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Impossible de démarrer la configuration des paiements.");
      setPending(false);
    }
  }

  return (
    <section className="flex flex-col gap-3 rounded-xl border border-black/[.08] p-5 dark:border-white/[.145]">
      <h2 className="text-lg font-semibold">Paiements</h2>
      {status?.ready ? (
        <p role="status" className="text-sm text-emerald-700 dark:text-emerald-400">
          Compte de paiement actif : vous recevez le montant de vos ventes, moins 5 % de commission Collector.
        </p>
      ) : (
        <>
          <p className="text-sm text-zinc-600 dark:text-zinc-400">
            {status?.configured
              ? "Votre configuration n'est pas terminée : vos articles ne peuvent pas encore être achetés."
              : "Configurez votre compte de paiement (hébergé par Stripe) pour pouvoir vendre."}
          </p>
          <button
            type="button"
            onClick={handleSetup}
            disabled={pending}
            className="w-fit rounded-full bg-foreground px-5 py-2 text-background disabled:opacity-50"
          >
            {pending ? "Redirection…" : status?.configured ? "Terminer la configuration" : "Configurer mes paiements"}
          </button>
        </>
      )}
      {error && <p className="text-sm text-red-600 dark:text-red-400">{error}</p>}
    </section>
  );
}
