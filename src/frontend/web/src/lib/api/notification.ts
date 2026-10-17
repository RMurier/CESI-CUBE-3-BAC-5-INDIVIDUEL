import { apiConfig } from "../config";

export interface UserNotification {
  id: string;
  userId: string;
  message: string;
  read: boolean;
  createdAt: string;
}

export async function subscribe(userId: string, keyword: string): Promise<void> {
  const res = await fetch(`${apiConfig.notification}/subscriptions`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ userId, keyword }),
  });
  if (!res.ok) throw new Error("Impossible de créer l'abonnement.");
}

export async function listNotifications(userId: string): Promise<UserNotification[]> {
  const res = await fetch(`${apiConfig.notification}/notifications/${userId}`, { cache: "no-store" });
  if (!res.ok) throw new Error("Impossible de charger les notifications.");
  return res.json();
}

export async function markRead(id: string): Promise<void> {
  const res = await fetch(`${apiConfig.notification}/notifications/${id}/read`, { method: "POST" });
  if (!res.ok) throw new Error("Impossible de marquer la notification comme lue.");
}
