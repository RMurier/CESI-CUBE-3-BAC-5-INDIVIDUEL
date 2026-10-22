import { apiConfig } from "../config";

/** Remonte vers Omnia (via Identity, qui relaie) : aucune donnée de carte/mot de passe ici, juste le message d'erreur. */
export async function reportClientError(message: string, source?: string, stackTrace?: string): Promise<void> {
  try {
    await fetch(`${apiConfig.identity}/logs/client-error`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ message, source, stackTrace, url: window.location.href }),
    });
  } catch {
    // Best-effort : un rapport d'erreur qui échoue ne doit jamais devenir une 2e erreur visible.
  }
}
