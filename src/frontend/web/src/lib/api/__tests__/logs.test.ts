import { reportClientError } from "../logs";

const fetchMock = jest.fn();
beforeEach(() => {
  fetchMock.mockReset();
  global.fetch = fetchMock as unknown as typeof fetch;
});

describe("logs api", () => {
  it("poste le message, la source et la stack vers Identity", async () => {
    fetchMock.mockResolvedValue({ ok: true });
    await reportClientError("Boom", "app.tsx", "Error: Boom\n at x");
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toContain("/logs/client-error");
    const body = JSON.parse(init.body);
    expect(body).toMatchObject({ message: "Boom", source: "app.tsx", stackTrace: "Error: Boom\n at x" });
    expect(body.url).toBe(window.location.href);
  });

  it("n'échoue jamais même si fetch rejette (best-effort)", async () => {
    fetchMock.mockRejectedValue(new Error("network down"));
    await expect(reportClientError("Boom")).resolves.toBeUndefined();
  });
});
