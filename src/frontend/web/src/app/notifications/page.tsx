"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { useAuth } from "@/lib/auth-context";
import { listNotifications, markRead, subscribe, type UserNotification } from "@/lib/api/notification";

const dateFormat = new Intl.DateTimeFormat("fr-FR", { dateStyle: "medium", timeStyle: "short" });

export default function NotificationsPage() {
  const { auth, loading } = useAuth();
  const router = useRouter();

  const [notifications, setNotifications] = useState<UserNotification[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [keyword, setKeyword] = useState("");
  const [subscribing, setSubscribing] = useState(false);
  const [subscribeMessage, setSubscribeMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!loading && !auth) router.push("/login");
  }, [loading, auth, router]);

  const refresh = useCallback(() => {
    if (!auth) return;
    listNotifications(auth.userId)
      .then(setNotifications)
      .catch((err: Error) => setError(err.message));
  }, [auth]);

  useEffect(() => {
    refresh();
  }, [refresh]);

  if (loading || !auth) return <p className="text-sm text-zinc-500">Chargement…</p>;

  async function handleSubscribe(event: FormEvent) {
    event.preventDefault();
    setSubscribeMessage(null);
    setSubscribing(true);
    try {
      await subscribe(auth!.userId, keyword);
      setSubscribeMessage(`Abonnement créé pour « ${keyword} ».`);
      setKeyword("");
    } catch (err) {
      setSubscribeMessage(err instanceof Error ? err.message : "Impossible de créer l'abonnement.");
    } finally {
      setSubscribing(false);
    }
  }

  async function handleMarkRead(id: string) {
    try {
      await markRead(id);
      refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Impossible de marquer comme lue.");
    }
  }

  return (
    <div className="flex flex-col gap-10">
      <div>
        <h1 className="text-3xl font-semibold tracking-tight">Notifications</h1>
        <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">
          Paramétrez vos centres d&apos;intérêt : vous serez averti ici (et par email) quand un article
          publié correspond à un mot-clé.
        </p>
      </div>

      <section className="flex flex-col gap-3 rounded-xl border border-black/[.08] p-5 dark:border-white/[.145]">
        <h2 className="text-lg font-semibold">S&apos;abonner à un centre d&apos;intérêt</h2>
        <form onSubmit={handleSubscribe} className="flex flex-wrap items-end gap-3">
          <label className="flex flex-col gap-1 text-sm">
            Mot-clé{" "}
            <input
              required
              placeholder="ex: figurine"
              value={keyword}
              onChange={(e) => setKeyword(e.target.value)}
              className="rounded-lg border border-black/[.08] bg-transparent px-3 py-2 dark:border-white/[.145]"
            />
          </label>
          <button
            type="submit"
            disabled={subscribing}
            className="rounded-full bg-foreground px-5 py-2 text-background transition-colors hover:bg-[#383838] disabled:opacity-50 dark:hover:bg-[#ccc]"
          >
            {subscribing ? "Enregistrement…" : "S'abonner"}
          </button>
        </form>
        {subscribeMessage && <p className="text-sm text-zinc-600 dark:text-zinc-400">{subscribeMessage}</p>}
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold">Mes notifications</h2>
        {error && <p className="text-sm text-red-600 dark:text-red-400">{error}</p>}
        {notifications !== null && notifications.length === 0 && (
          <p className="text-sm text-zinc-500">Aucune notification pour l&apos;instant.</p>
        )}
        {notifications !== null && notifications.length > 0 && (
          <ul className="flex flex-col gap-2">
            {notifications.map((notification) => (
              <li
                key={notification.id}
                className={`flex items-center justify-between gap-4 rounded-lg border border-black/[.08] px-4 py-3 dark:border-white/[.145] ${
                  notification.read ? "opacity-60" : ""
                }`}
              >
                <div>
                  <p className="text-sm">{notification.message}</p>
                  <p className="text-xs text-zinc-500">{dateFormat.format(new Date(notification.createdAt))}</p>
                </div>
                {!notification.read && (
                  <button
                    onClick={() => handleMarkRead(notification.id)}
                    className="shrink-0 rounded-full border border-black/[.08] px-3 py-1 text-xs transition-colors hover:bg-black/[.04] dark:border-white/[.145] dark:hover:bg-[#1a1a1a]"
                  >
                    Marquer comme lue
                  </button>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
