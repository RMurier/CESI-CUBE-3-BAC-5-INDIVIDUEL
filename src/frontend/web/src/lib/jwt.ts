export interface DecodedToken {
  sub: string;
  email: string;
  display_name: string;
  exp: number;
}

/** Décodage local du payload JWT (pas de vérification de signature : la validation a déjà été
 * faite côté serveur, on lit juste les claims pour l'affichage/identifiant utilisateur). */
export function decodeJwt(token: string): DecodedToken | null {
  try {
    const payload = token.split(".")[1];
    const base64 = payload.replace(/-/g, "+").replace(/_/g, "/");
    const json = decodeURIComponent(
      atob(base64)
        .split("")
        .map((c) => "%" + c.charCodeAt(0).toString(16).padStart(2, "0"))
        .join(""),
    );
    const decoded = JSON.parse(json);
    return {
      sub: decoded.sub,
      email: decoded.email,
      display_name: decoded.display_name,
      exp: decoded.exp,
    };
  } catch {
    return null;
  }
}
