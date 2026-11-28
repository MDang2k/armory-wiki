"use client";

import { Background, Controls, MarkerType, ReactFlow, type Edge, type Node } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { useRouter } from "next/navigation";

export type GraphNode = { slug: string; name: string; year: string | null; isRoot: boolean; x: number; y: number };
export type GraphEdge = { source: string; target: string; isCopy: boolean };

/**
 * The family tree drawn (PLAN.md Phase 8a): positions come laid out from the server, so dagre never ships to
 * the browser. Pan and zoom; clicking a firearm opens it. The nested list under the graph says the same in text.
 */
export function FamilyGraph({ nodes, edges }: { nodes: GraphNode[]; edges: GraphEdge[] }) {
  const router = useRouter();
  const flowNodes: Node[] = nodes.map((n) => ({
    id: n.slug,
    position: { x: n.x, y: n.y },
    data: {
      label: (
        <span className="block leading-tight">
          <span className="block font-medium">{n.name}</span>
          {n.year && <span className="block text-xs opacity-70">{n.year}</span>}
        </span>
      ),
    },
    className: n.isRoot ? "family-root" : undefined,
    style: { width: 190, cursor: "pointer" },
    connectable: false,
    draggable: false,
  }));
  const flowEdges: Edge[] = edges.map((e) => ({
    id: `${e.source}->${e.target}`,
    source: e.source,
    target: e.target,
    type: "smoothstep",
    markerEnd: { type: MarkerType.ArrowClosed },
    style: e.isCopy ? { strokeDasharray: "6 4" } : undefined,
    label: e.isCopy ? "copy" : undefined,
  }));

  return (
    <div className="h-[70vh] min-h-96 rounded border border-border" aria-hidden="true">
      <ReactFlow
        nodes={flowNodes}
        edges={flowEdges}
        onNodeClick={(_, node) => router.push(`/firearms/${node.id}`)}
        nodesConnectable={false}
        nodesDraggable={false}
        elementsSelectable={false}
        colorMode="light" // our CSS variables do light and dark (globals.css); "system" mismatches between server and browser
        fitView
        minZoom={0.2}
        proOptions={{ hideAttribution: false }}
      >
        <Background />
        <Controls showInteractive={false} />
      </ReactFlow>
    </div>
  );
}
