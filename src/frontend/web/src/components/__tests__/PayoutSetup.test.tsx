import { render, screen } from "@testing-library/react";
import { PayoutSetup } from "../PayoutSetup";
import { useAuth } from "@/lib/auth-context";
import { getPayoutStatus } from "@/lib/api/orders";

jest.mock("@/lib/auth-context", () => ({ useAuth: jest.fn() }));
jest.mock("@/lib/api/orders", () => ({
  ...jest.requireActual("@/lib/api/orders"),
  getPayoutStatus: jest.fn(),
  startPayoutOnboarding: jest.fn(),
}));

beforeEach(() => {
  (useAuth as jest.Mock).mockReturnValue({ getToken: async () => "t" });
});

describe("PayoutSetup", () => {
  it("propose la configuration tant que le vendeur n'a rien fait", async () => {
    (getPayoutStatus as jest.Mock).mockResolvedValue({ configured: false, ready: false });
    render(<PayoutSetup />);
    expect(await screen.findByRole("button", { name: "Configurer mes paiements" })).toBeInTheDocument();
  });

  it("propose de terminer un onboarding inachevé", async () => {
    (getPayoutStatus as jest.Mock).mockResolvedValue({ configured: true, ready: false });
    render(<PayoutSetup />);
    expect(await screen.findByRole("button", { name: "Terminer la configuration" })).toBeInTheDocument();
  });

  it("confirme que le compte est actif et mentionne la commission de 5 %", async () => {
    (getPayoutStatus as jest.Mock).mockResolvedValue({ configured: true, ready: true });
    render(<PayoutSetup />);
    expect(await screen.findByRole("status")).toHaveTextContent("5 %");
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("affiche l'erreur si l'état est illisible", async () => {
    (getPayoutStatus as jest.Mock).mockRejectedValue(new Error("KO"));
    render(<PayoutSetup />);
    expect(await screen.findByText("KO")).toBeInTheDocument();
  });
});
