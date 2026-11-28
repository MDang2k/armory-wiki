import { describe, expect, it } from "vitest";
import type { Dto } from "./api/types";
import { NODE_HEIGHT, NODE_WIDTH, layoutTree, treeBranches, treeDepth } from "./familyTree";

const node = (slug: string, isRoot = false): Dto<"FamilyTreeNodeDto"> => ({ slug, name: slug.toUpperCase(), designDate: null, isRoot });
const edge = (source: string, target: string, isCopy = false): Dto<"FamilyTreeEdgeDto"> => ({ source, target, isCopy });

// AR-10 → AR-15 → M16 (root) → M4, and a licensed copy of the M16.
const tree: Dto<"FamilyTreeDto"> = {
  nodes: [node("ar-10"), node("ar-15"), node("m16", true), node("m4"), node("copy")],
  edges: [edge("ar-10", "ar-15"), edge("ar-15", "m16"), edge("m16", "m4"), edge("m16", "copy", true)],
  truncated: false,
};

describe("treeDepth", () => {
  it.each([
    [undefined, 3],
    ["1", 1],
    ["5", 5],
    ["0", 3],
    ["6", 3],
    ["2.5", 3],
    ["deep", 3],
    [["4", "2"], 4],
  ])("reads %j as %i", (value, depth) => {
    expect(treeDepth(value)).toBe(depth);
  });
});

describe("layoutTree", () => {
  it("puts what came first above what came from it", () => {
    const at = layoutTree(tree);
    expect(at.get("ar-10")!.y).toBeLessThan(at.get("ar-15")!.y);
    expect(at.get("ar-15")!.y).toBeLessThan(at.get("m16")!.y);
    expect(at.get("m16")!.y).toBeLessThan(at.get("m4")!.y);
    expect(at.get("m4")!.y).toBe(at.get("copy")!.y); // siblings share a row
  });

  it("never overlaps two firearms", () => {
    const boxes = [...layoutTree(tree).values()];
    for (const [i, a] of boxes.entries()) {
      for (const b of boxes.slice(i + 1)) {
        const apart = a.x + NODE_WIDTH <= b.x || b.x + NODE_WIDTH <= a.x || a.y + NODE_HEIGHT <= b.y || b.y + NODE_HEIGHT <= a.y;
        expect(apart).toBe(true);
      }
    }
  });
});

describe("treeBranches", () => {
  it("lists what the root came from and what came from it, nearest first", () => {
    const { root, ancestors, descendants } = treeBranches(tree);
    expect(root.slug).toBe("m16");
    expect(ancestors.map((b) => b.node.slug)).toEqual(["ar-15"]);
    expect(ancestors[0].branches.map((b) => b.node.slug)).toEqual(["ar-10"]);
    expect(descendants.map((b) => [b.node.slug, b.isCopy])).toEqual([
      ["m4", false],
      ["copy", true],
    ]);
  });

  it("lists each firearm once, even in a cycle", () => {
    const cycle: Dto<"FamilyTreeDto"> = {
      nodes: [node("a", true), node("b")],
      edges: [edge("a", "b"), edge("b", "a")],
      truncated: false,
    };
    const { ancestors, descendants } = treeBranches(cycle);
    expect([...ancestors, ...descendants].map((b) => b.node.slug)).toEqual(["b"]);
  });
});
