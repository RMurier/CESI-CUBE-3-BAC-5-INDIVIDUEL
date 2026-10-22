import { render } from "@testing-library/react";
import { ClientErrorReporter } from "../ClientErrorReporter";
import { reportClientError } from "@/lib/api/logs";

jest.mock("@/lib/api/logs", () => ({ reportClientError: jest.fn() }));

describe("ClientErrorReporter", () => {
  it("remonte une erreur JS non gérée (window.onerror)", () => {
    render(<ClientErrorReporter />);

    const error = new Error("Boom");
    window.dispatchEvent(new ErrorEvent("error", { message: "Boom", filename: "app.tsx", error }));

    expect(reportClientError).toHaveBeenCalledWith("Boom", "app.tsx", error.stack);
  });

  it("remonte une promesse rejetée non gérée, avec le message de l'Error", () => {
    render(<ClientErrorReporter />);

    const reason = new Error("Rejected");
    const event = Object.assign(new Event("unhandledrejection"), { reason });
    window.dispatchEvent(event);

    expect(reportClientError).toHaveBeenCalledWith("Rejected", "unhandledrejection", reason.stack);
  });

  it("gère une promesse rejetée avec une valeur non-Error (converti en texte)", () => {
    render(<ClientErrorReporter />);

    const event = Object.assign(new Event("unhandledrejection"), { reason: "just a string" });
    window.dispatchEvent(event);

    expect(reportClientError).toHaveBeenCalledWith("just a string", "unhandledrejection", undefined);
  });

  it("retire ses listeners au démontage", () => {
    const { unmount } = render(<ClientErrorReporter />);
    unmount();

    (reportClientError as jest.Mock).mockClear();
    window.dispatchEvent(new ErrorEvent("error", { message: "Après démontage" }));

    expect(reportClientError).not.toHaveBeenCalled();
  });
});
