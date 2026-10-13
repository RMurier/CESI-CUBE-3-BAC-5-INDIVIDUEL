"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useAuth } from "@/lib/auth-context";

export function NavBar() {
  const { auth, logout } = useAuth();
  const router = useRouter();

  function handleLogout() {
    logout();
    router.push("/");
  }

  return (
    <header className="border-b border-black/[.08] dark:border-white/[.145]">
      <nav className="mx-auto flex max-w-5xl flex-wrap items-center gap-4 px-6 py-4">
        <Link href="/" className="text-lg font-semibold tracking-tight">
          Collector<span className="text-zinc-400">.shop</span>
        </Link>

        <div className="flex flex-1 items-center gap-4 text-sm">
          <Link href="/" className="text-zinc-600 hover:text-foreground dark:text-zinc-400">
            Catalogue
          </Link>
          {auth && (
            <Link href="/notifications" className="text-zinc-600 hover:text-foreground dark:text-zinc-400">
              Notifications
            </Link>
          )}
        </div>

        <div className="flex items-center gap-3 text-sm">
          {auth ? (
            <>
              <Link href="/compte" className="font-medium hover:underline">
                {auth.displayName}
              </Link>
              <button
                onClick={handleLogout}
                className="rounded-full border border-black/[.08] px-4 py-1.5 transition-colors hover:bg-black/[.04] dark:border-white/[.145] dark:hover:bg-[#1a1a1a]"
              >
                Déconnexion
              </button>
            </>
          ) : (
            <>
              <Link href="/login" className="hover:underline">
                Connexion
              </Link>
              <Link
                href="/register"
                className="rounded-full bg-foreground px-4 py-1.5 text-background transition-colors hover:bg-[#383838] dark:hover:bg-[#ccc]"
              >
                Inscription
              </Link>
            </>
          )}
        </div>
      </nav>
    </header>
  );
}
