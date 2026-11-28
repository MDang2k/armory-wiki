import { describe, expect, it } from "vitest";
import { IMAGE_MAX_BYTES, imageFileProblem, moved } from "./images";

describe("imageFileProblem", () => {
  it("accepts JPEG, PNG and WebP up to the limit", () => {
    expect(imageFileProblem({ type: "image/jpeg", size: 1 })).toBeNull();
    expect(imageFileProblem({ type: "image/png", size: IMAGE_MAX_BYTES })).toBeNull();
    expect(imageFileProblem({ type: "image/webp", size: 5000 })).toBeNull();
  });

  it.each([
    [{ type: "image/gif", size: 10 }, "JPEG, PNG or WebP"],
    [{ type: "", size: 10 }, "JPEG, PNG or WebP"],
    [{ type: "image/jpeg", size: 0 }, "20 MB"],
    [{ type: "image/jpeg", size: IMAGE_MAX_BYTES + 1 }, "20 MB"],
  ])("refuses %j", (file, message) => {
    expect(imageFileProblem(file)).toContain(message);
  });
});

describe("moved", () => {
  it("swaps with the neighbour", () => {
    expect(moved([1, 2, 3], 1, -1)).toEqual([2, 1, 3]);
    expect(moved([1, 2, 3], 1, 1)).toEqual([1, 3, 2]);
  });

  it("stays put at the ends", () => {
    expect(moved([1, 2, 3], 0, -1)).toEqual([1, 2, 3]);
    expect(moved([1, 2, 3], 2, 1)).toEqual([1, 2, 3]);
  });
});
