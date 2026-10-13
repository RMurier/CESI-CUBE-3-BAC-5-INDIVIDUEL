"use client";

import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { apiConfig } from "./config";
import { decodeJwt } from "./jwt";

export interface AuthState {
  token: string;
  userId: string;
  email: string;
  displayName: string;
  roles: string[];
}

interface AuthContextValue {
  auth: AuthState | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, displayName: string) => Promise<void>;
  becomeSeller: () => Promise<void>;
  logout: () => void;
}

const STORAGE_KEY = "collector.auth";
const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [auth, setAuth] = useState<AuthState | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      // Hydratation volontairement différée au montage (pas dans l'initialiseur de useState) :
      // localStorage n'existe pas pendant le rendu serveur, le lire plus tôt provoquerait un
      // mismatch d'hydratation entre le HTML serveur et le premier rendu client.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      if (raw) setAuth(JSON.parse(raw) as AuthState);
    } catch {
      // stockage indisponible : on repart simplement sans session persistée.
    } finally {
      setLoading(false);
    }
  }, []);

  function persist(next: AuthState | null) {
    setAuth(next);
    try {
      if (next) localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
      else localStorage.removeItem(STORAGE_KEY);
    } catch {
      // idem : pas bloquant pour la session en cours.
    }
  }

  async function login(email: string, password: string) {
    const res = await fetch(`${apiConfig.identity}/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password }),
    });
    if (!res.ok) {
      throw new Error(res.status === 401 ? "Email ou mot de passe incorrect." : "Connexion impossible.");
    }
    const data = await res.json();
    const decoded = decodeJwt(data.accessToken);
    if (!decoded) throw new Error("Jeton reçu invalide.");
    persist({
      token: data.accessToken,
      userId: decoded.sub,
      email: decoded.email,
      displayName: decoded.display_name,
      roles: data.roles ?? [],
    });
  }

  async function register(email: string, password: string, displayName: string) {
    const res = await fetch(`${apiConfig.identity}/auth/register`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password, displayName }),
    });
    if (!res.ok) {
      throw new Error(res.status === 409 ? "Un compte existe déjà avec cet email." : "Inscription impossible.");
    }
    await login(email, password);
  }

  async function becomeSeller() {
    if (!auth) throw new Error("Connexion requise.");
    const res = await fetch(`${apiConfig.identity}/auth/become-seller`, {
      method: "POST",
      headers: { Authorization: `Bearer ${auth.token}` },
    });
    if (!res.ok) throw new Error("Impossible de devenir vendeur.");
    const data = await res.json();
    persist({ ...auth, roles: data.roles ?? auth.roles });
  }

  function logout() {
    persist(null);
  }

  return (
    <AuthContext.Provider value={{ auth, loading, login, register, becomeSeller, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth doit être utilisé à l'intérieur de <AuthProvider>.");
  return ctx;
}
