import dagre from "@dagrejs/dagre";
import type { Dto } from "./api/types";

type Tree = Dto<"FamilyTreeDto">;
type Node = Dto<"FamilyTreeNodeDto">;

export const NODE_WIDTH = 190;
export const NODE_HEIGHT = 58;

/** The URL depth: 1–5, default 3 (the API's own limits). */
export function treeDepth(value: string | string[] | undefined): number {
  const n = Number(Array.isArray(value) ? value[0] : value);
  return Number.isInteger(n) && n >= 1 && n <= 5 ? n : 3;
}

/**
 * Top-left positions for each node (by slug), laid out by dagre top to bottom: what a firearm came from above
 * it, what came from it below (PLAN.md Phase 8a).
 */
export function layoutTree(tree: Tree): Map<string, { x: number; y: number }> {
  const graph = new dagre.graphlib.Graph();
  graph.setGraph({ rankdir: "TB", nodesep: 24, ranksep: 56 });
  graph.setDefaultEdgeLabel(() => ({}));
  for (const node of tree.nodes) {
    graph.setNode(node.slug, { width: NODE_WIDTH, height: NODE_HEIGHT });
  }

  for (const edge of tree.edges) {
    graph.setEdge(edge.source, edge.target);
  }

  dagre.layout(graph);
  return new Map(
    tree.nodes.map((node) => {
      const { x, y } = graph.node(node.slug);
      return [node.slug, { x: x - NODE_WIDTH / 2, y: y - NODE_HEIGHT / 2 }]; // dagre gives centres
    }),
  );
}

/** One firearm in the text version of the tree, with the firearms one more step away. */
export type Branch = { node: Node; isCopy: boolean; branches: Branch[] };

/**
 * The family as two nested lists from the root: what it came from (upwards) and what came from it (downwards).
 * Each firearm appears once, at its first and nearest place, so cycles and shared ancestors stay finite.
 */
export function treeBranches(tree: Tree): { root: Node; ancestors: Branch[]; descendants: Branch[] } {
  const bySlug = new Map(tree.nodes.map((n) => [n.slug, n]));
  const root = tree.nodes.find((n) => n.isRoot) ?? tree.nodes[0];
  const seen = new Set([root.slug]);

  // Breadth-first, so a firearm reachable at two distances is listed at the nearer one.
  const grow = (direction: "up" | "down"): Branch[] => {
    const top: Branch[] = [];
    let level: { slug: string; into: Branch[] }[] = [{ slug: root.slug, into: top }];
    while (level.length > 0) {
      const next: typeof level = [];
      for (const { slug, into } of level) {
        const links = tree.edges.filter((e) => (direction === "up" ? e.target === slug : e.source === slug));
        for (const link of links) {
          const other = direction === "up" ? link.source : link.target;
          const node = bySlug.get(other);
          if (!node || seen.has(other)) {
            continue;
          }

          seen.add(other);
          const branch: Branch = { node, isCopy: link.isCopy, branches: [] };
          into.push(branch);
          next.push({ slug: other, into: branch.branches });
        }
      }

      level = next;
    }

    return top;
  };

  const ancestors = grow("up");
  const descendants = grow("down");
  return { root, ancestors, descendants };
}
