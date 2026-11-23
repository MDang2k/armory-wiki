"use client";

import { useState } from "react";
import type { Dto } from "@/lib/api/types";

/**
 * Approved videos, click to load (PLAN.md §5): a title card first, and the youtube-nocookie.com player only after
 * the reader asks for it, so nothing reaches YouTube before that.
 */
export function VideoList({ videos }: { videos: Dto<"VideoDto">[] }) {
  return (
    <ul className="grid gap-3 sm:grid-cols-2">
      {videos.map((video) => (
        <li key={video.youTubeVideoId}>
          <Video video={video} />
        </li>
      ))}
    </ul>
  );
}

function Video({ video }: { video: Dto<"VideoDto"> }) {
  const [playing, setPlaying] = useState(false);
  const year = video.publishedAt ? new Date(video.publishedAt).getUTCFullYear() : null;

  return (
    <figure className="space-y-1">
      <div className="aspect-video overflow-hidden rounded border border-border bg-surface">
        {playing ? (
          <iframe
            src={`https://www.youtube-nocookie.com/embed/${encodeURIComponent(video.youTubeVideoId)}?autoplay=1`}
            title={video.title}
            allow="autoplay; encrypted-media; picture-in-picture; fullscreen"
            referrerPolicy="strict-origin-when-cross-origin"
            className="size-full"
          />
        ) : (
          <button
            type="button"
            onClick={() => setPlaying(true)}
            className="flex size-full flex-col items-center justify-center gap-2 p-4 text-center"
          >
            <span aria-hidden className="flex size-12 items-center justify-center rounded-full bg-link text-xl text-background">
              ▶
            </span>
            <span className="text-sm">
              Play “{video.title}”
              <span className="block text-xs text-muted">Loads the player from YouTube</span>
            </span>
          </button>
        )}
      </div>
      <figcaption className="text-xs text-muted">
        {[video.channel, year].filter(Boolean).join(", ")} ·{" "}
        <a href={`https://www.youtube.com/watch?v=${encodeURIComponent(video.youTubeVideoId)}`} rel="noopener noreferrer">
          Watch on YouTube
        </a>
      </figcaption>
    </figure>
  );
}
