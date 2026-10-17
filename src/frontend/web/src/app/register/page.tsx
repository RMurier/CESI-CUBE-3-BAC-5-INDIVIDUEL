"use client";

import { useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useAuth } from "@/lib/auth-context";

export default function RegisterPage() {
  const { register } = useAuth();
  const router = useRouter();
  const [displayName, setDisplayName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await register(email, password, displayName);
      router.push("/compte");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Inscription impossible.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="mx-auto flex w-full max-w-sm flex-col gap-6">
      <h1 className="text-2xl font-semibold tracking-tight">Inscription</h1>
      <p className="text-sm text-zinc-600 dark:text-zinc-400">
        Tout compte est acheteur par défaut. Vous pourrez devenir vendeur ensuite depuis votre espace.
      </p>

      <form onSubmit={handleSubmit} className="flex flex-col gap-4">
        <label className="flex flex-col gap-1 text-sm">
          Nom affiché{" "}
          <input
            required
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            className="rounded-lg border border-black/[.08] bg-transparent px-3 py-2 dark:border-white/[.145]"
          />
        </label>

        <label className="flex flex-col gap-1 text-sm">
          Email{" "}
          <input
            type="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className="rounded-lg border border-black/[.08] bg-transparent px-3 py-2 dark:border-white/[.145]"
          />
        </label>

        <label className="flex flex-col gap-1 text-sm">
          Mot de passe{" "}
          <input
            type="password"
            required
            minLength={8}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className="rounded-lg border border-black/[.08] bg-transparent px-3 py-2 dark:border-white/[.145]"
          />
        </label>

        {error && <p className="text-sm text-red-600 dark:text-red-400">{error}</p>}

        <button
          type="submit"
          disabled={submitting}
          className="rounded-full bg-foreground px-5 py-2 text-background transition-colors hover:bg-[#383838] disabled:opacity-50 dark:hover:bg-[#ccc]"
        >
          {submitting ? "Inscription…" : "S'inscrire"}
        </button>
      </form>

      <p className="text-sm text-zinc-600 dark:text-zinc-400">
        Déjà inscrit ?{" "}
        <Link href="/login" className="font-medium hover:underline">
          Se connecter
        </Link>
      </p>
    </div>
  );
}
