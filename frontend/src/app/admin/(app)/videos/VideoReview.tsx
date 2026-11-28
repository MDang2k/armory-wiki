"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { Notice, buttonClass, inputClass, primaryButtonClass } from "@/components/admin/ui";
import { adminApi } from "@/lib/admin/client";
import { ApiProblem, unwrap } from "@/lib/admin/problems";
import type { Dto } from "@/lib/api/types";

const PAGE_SIZE = 25;
type Status = Dto<"VideoStatus">;
type Item = Dto<"AdminVideoListItemDto">;

/**
 * The video review screen (PLAN.md Phase 7d): the importer's suggested Forgotten Weapons videos across all
 * firearms, approved or rejected one by one. Approved videos show on the firearm's public page; rejected ones
 * stay recorded so the importer never suggests them again.
 */
export function VideoReview() {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<Status>("Suggested");
  const [page, setPage] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const query = { status, page, pageSize: PAGE_SIZE };
  const key = ["admin", "videos", query] as const;
  const list = useQuery({
    queryKey: key,
    queryFn: async () => unwrap(await adminApi.GET("/api/admin/videos", { params: { query } })),
    placeholderData: keepPreviousData,
  });

  const review = useMutation({
    mutationFn: async ({ item, approve }: { item: Item; approve: boolean }) => {
      const params = { params: { path: { videoId: item.id } } };
      return unwrap(
        approve
          ? await adminApi.POST("/api/admin/videos/{videoId}/approve", params)
          : await adminApi.POST("/api/admin/videos/{videoId}/reject", params),
      );
    },
    onMutate: () => setError(null),
    onError: (e, { item }) => {
      if (!(e instanceof ApiProblem && e.status === 401)) {
        setError(`${item.video.title}: ${e instanceof Error ? e.message : "could not reach the API."}`);
      }
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ["admin", "videos"] }),
  });

  const total = list.data?.totalCount ?? 0;
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));
  return (
    <div className="space-y-4">
      <h1 className="font-serif text-2xl">Videos</h1>
      <div className="flex flex-wrap items-end gap-3 text-sm">
        <label className="space-y-1">
          <span className="block font-medium">Show</span>
          <select
            value={status}
            onChange={(e) => {
              setStatus(e.target.value as Status);
              setPage(1);
            }}
            className={inputClass}
          >
            <option value="Suggested">Suggested, to review</option>
            <option value="Approved">Approved</option>
            <option value="Rejected">Rejected</option>
          </select>
        </label>
      </div>
      {error && <Notice>{error}</Notice>}
      {list.isError && <Notice>Could not load videos: {list.error.message}</Notice>}
      {list.data && (
        <>
          <p className="text-sm text-muted" aria-live="polite">
            {total} {total === 1 ? "video" : "videos"}
            {list.isFetching ? " · updating…" : ""}
          </p>
          {list.data.items.length === 0 && status === "Suggested" && <p className="text-sm text-muted">Nothing to review.</p>}
          <ul className="space-y-2">
            {list.data.items.map((item) => (
              <li key={item.id} className="flex flex-wrap items-center gap-3 border-b border-border pb-2 text-sm">
                <div className="min-w-64 flex-1">
                  <a href={`https://www.youtube.com/watch?v=${item.video.youTubeVideoId}`} target="_blank" rel="noopener noreferrer">
                    {item.video.title}
                  </a>
                  <div className="text-muted">
                    {[item.video.channel, item.video.publishedAt && new Date(item.video.publishedAt).toLocaleDateString()].filter(Boolean).join(" · ")}
                    {" · for "}
                    <Link href={`/admin/firearms/${item.firearmId}`}>{item.firearmName}</Link>
                  </div>
                </div>
                {item.status !== "Approved" && (
                  <button type="button" className={primaryButtonClass} disabled={review.isPending} onClick={() => review.mutate({ item, approve: true })}>
                    Approve
                  </button>
                )}
                {item.status !== "Rejected" && (
                  <button type="button" className={buttonClass} disabled={review.isPending} onClick={() => review.mutate({ item, approve: false })}>
                    Reject
                  </button>
                )}
              </li>
            ))}
          </ul>
          {pages > 1 && (
            <nav aria-label="Pages" className="flex items-center gap-3 text-sm">
              <button type="button" className={buttonClass} disabled={page <= 1} onClick={() => setPage(page - 1)}>
                Previous
              </button>
              <span>
                Page {page} of {pages}
              </span>
              <button type="button" className={buttonClass} disabled={page >= pages} onClick={() => setPage(page + 1)}>
                Next
              </button>
            </nav>
          )}
        </>
      )}
    </div>
  );
}
