import type { Dto } from "@/lib/api/types";

/**
 * An image with its attribution under it, always (GLOSSARY.md: *image*): caption, author, license, source. Our own
 * pre-sized WebP files, so a plain <img> (PLAN.md §5); the original opens on click.
 */
export function ImageFigure({
  image,
  size,
  alt,
  eager = false,
}: {
  image: Dto<"ImageDto">;
  size: "medium" | "thumb";
  alt: string;
  eager?: boolean;
}) {
  const scale = Math.min(1, (size === "medium" ? 1280 : 320) / image.width);
  return (
    <figure className="space-y-1">
      <a href={image.originalUrl}>
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src={size === "medium" ? image.mediumUrl : image.thumbUrl}
          alt={image.caption ?? alt}
          width={Math.round(image.width * scale)}
          height={Math.round(image.height * scale)}
          loading={eager ? "eager" : "lazy"}
          className="h-auto w-full rounded-sm bg-surface object-contain"
        />
      </a>
      <figcaption className="text-xs text-muted">
        {image.caption && <span className="block text-foreground">{image.caption}</span>}
        {image.author} ·{" "}
        <a href={image.license.url} rel="license noopener noreferrer">
          {image.license.name}
        </a>{" "}
        ·{" "}
        <a href={image.sourceUrl} rel="noopener noreferrer">
          Source
        </a>
      </figcaption>
    </figure>
  );
}
