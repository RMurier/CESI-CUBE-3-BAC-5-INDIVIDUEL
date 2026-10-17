/** @jest-environment node */
import { GET } from "../route";

describe("GET /api/health", () => {
  it("renvoie ok", async () => {
    const res = GET();
    expect(await res.json()).toEqual({ status: "ok" });
  });
});
