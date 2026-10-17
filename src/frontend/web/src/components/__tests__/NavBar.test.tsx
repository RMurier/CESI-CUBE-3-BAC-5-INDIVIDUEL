import { fireEvent, render, screen } from "@testing-library/react";
import { NavBar } from "../NavBar";
import { useAuth } from "@/lib/auth-context";

const push = jest.fn();
jest.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));
jest.mock("@/lib/auth-context", () => ({ useAuth: jest.fn() }));

describe("NavBar", () => {
  it("affiche connexion / inscription pour un visiteur", () => {
    (useAuth as jest.Mock).mockReturnValue({ auth: null, logout: jest.fn() });
    render(<NavBar />);
    expect(screen.getByText("Connexion")).toBeInTheDocument();
    expect(screen.getByText("Inscription")).toBeInTheDocument();
    expect(screen.queryByText("Notifications")).not.toBeInTheDocument();
  });

  it("affiche le nom et déconnecte puis redirige", () => {
    const logout = jest.fn();
    (useAuth as jest.Mock).mockReturnValue({ auth: { displayName: "Alice" }, logout });
    render(<NavBar />);
    expect(screen.getByText("Notifications")).toBeInTheDocument();
    fireEvent.click(screen.getByText("Déconnexion"));
    expect(logout).toHaveBeenCalled();
    expect(push).toHaveBeenCalledWith("/");
  });
});
