import { describe, expect, it } from "vitest";
import { loginHref, safeReturnTo } from "./returnTo";

describe("safeReturnTo", () => {
  it.each(["/admin", "/admin/firearms/12", "/admin/firearms?status=Draft&q=ak"])("keeps admin page %j", (path) => {
    expect(safeReturnTo(path)).toBe(path);
  });

  it.each([null, "", "/", "/firearms", "/administrator", "https://evil.test/admin", "//evil.test/admin", "/admin\\..\\x", "/admin/login?returnTo=/x"])(
    "falls back for %j",
    (path) => {
      expect(safeReturnTo(path)).toBe("/admin/firearms");
    },
  );

  it("survives the round trip through loginHref", () => {
    const here = "/admin/firearms?status=Draft&q=ak 47";
    const returnTo = new URL(loginHref(here), "https://x.test").searchParams.get("returnTo");
    expect(safeReturnTo(returnTo)).toBe(here);
  });
});
