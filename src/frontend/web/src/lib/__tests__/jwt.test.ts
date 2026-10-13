import { decodeJwt } from "../jwt";

function makeToken(payload: Record<string, unknown>): string {
  const base64url = (value: string) =>
    Buffer.from(value, "utf-8").toString("base64").replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");

  const header = base64url(JSON.stringify({ alg: "HS256", typ: "JWT" }));
  const body = base64url(JSON.stringify(payload));
  return `${header}.${body}.fake-signature`;
}

describe("decodeJwt", () => {
  it("extrait sub/email/display_name/exp depuis un JWT bien formé", () => {
    const token = makeToken({
      sub: "11111111-1111-1111-1111-111111111111",
      email: "ada@example.com",
      display_name: "Ada",
      exp: 1999999999,
    });

    expect(decodeJwt(token)).toEqual({
      sub: "11111111-1111-1111-1111-111111111111",
      email: "ada@example.com",
      display_name: "Ada",
      exp: 1999999999,
    });
  });

  it("gère les caractères accentués encodés en UTF-8 dans le payload", () => {
    const token = makeToken({
      sub: "id",
      email: "e@example.com",
      display_name: "Éléonore",
      exp: 1,
    });

    expect(decodeJwt(token)?.display_name).toBe("Éléonore");
  });

  it("retourne null pour un jeton invalide", () => {
    expect(decodeJwt("pas-un-jwt")).toBeNull();
    expect(decodeJwt("")).toBeNull();
  });
});
