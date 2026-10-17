import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import LoginPage from "../login/page";
import RegisterPage from "../register/page";
import { useAuth } from "@/lib/auth-context";

const push = jest.fn();
jest.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));
jest.mock("@/lib/auth-context", () => ({ useAuth: jest.fn() }));

beforeEach(() => push.mockReset());

function fill(label: RegExp, value: string) {
  fireEvent.change(screen.getByLabelText(label), { target: { value } });
}

describe("LoginPage", () => {
  it("redirige vers /compte après connexion", async () => {
    const login = jest.fn().mockResolvedValue(undefined);
    (useAuth as jest.Mock).mockReturnValue({ login });
    render(<LoginPage />);
    fill(/Email/, "a@b.c");
    fill(/Mot de passe/, "secret123");
    fireEvent.click(screen.getByRole("button", { name: "Se connecter" }));
    await waitFor(() => expect(push).toHaveBeenCalledWith("/compte"));
    expect(login).toHaveBeenCalledWith("a@b.c", "secret123");
  });

  it("affiche l'erreur de connexion", async () => {
    (useAuth as jest.Mock).mockReturnValue({ login: jest.fn().mockRejectedValue(new Error("Mauvais mdp")) });
    render(<LoginPage />);
    fill(/Email/, "a@b.c");
    fill(/Mot de passe/, "x");
    fireEvent.click(screen.getByRole("button", { name: "Se connecter" }));
    expect(await screen.findByText("Mauvais mdp")).toBeInTheDocument();
  });

  it("message générique si l'erreur n'est pas une Error", async () => {
    (useAuth as jest.Mock).mockReturnValue({ login: jest.fn().mockRejectedValue("boom") });
    render(<LoginPage />);
    fill(/Email/, "a@b.c");
    fill(/Mot de passe/, "x");
    fireEvent.click(screen.getByRole("button", { name: "Se connecter" }));
    expect(await screen.findByText("Connexion impossible.")).toBeInTheDocument();
  });
});

describe("RegisterPage", () => {
  it("inscrit puis redirige", async () => {
    const register = jest.fn().mockResolvedValue(undefined);
    (useAuth as jest.Mock).mockReturnValue({ register });
    render(<RegisterPage />);
    fill(/Nom affiché/, "Alice");
    fill(/Email/, "a@b.c");
    fill(/Mot de passe/, "secret123");
    fireEvent.click(screen.getByRole("button", { name: "S'inscrire" }));
    await waitFor(() => expect(push).toHaveBeenCalledWith("/compte"));
    expect(register).toHaveBeenCalledWith("a@b.c", "secret123", "Alice");
  });

  it("affiche l'erreur d'inscription (Error puis autre)", async () => {
    const register = jest.fn().mockRejectedValueOnce(new Error("Déjà pris")).mockRejectedValueOnce("x");
    (useAuth as jest.Mock).mockReturnValue({ register });
    render(<RegisterPage />);
    fill(/Nom affiché/, "A");
    fill(/Email/, "a@b.c");
    fill(/Mot de passe/, "secret123");
    fireEvent.click(screen.getByRole("button", { name: "S'inscrire" }));
    expect(await screen.findByText("Déjà pris")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "S'inscrire" }));
    expect(await screen.findByText("Inscription impossible.")).toBeInTheDocument();
  });
});
