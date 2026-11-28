import type { Metadata } from "next";
import { VideoReview } from "./VideoReview";

export const metadata: Metadata = { title: "Videos" };

export default function VideosPage() {
  return <VideoReview />;
}
