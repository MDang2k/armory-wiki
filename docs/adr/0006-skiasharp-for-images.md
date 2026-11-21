# 6. SkiaSharp for image processing

Status: Accepted (2026-11-21)

## Context
The plan named ImageSharp for decoding uploads and producing thumbnails. From version 3.2, ImageSharp's build targets fail Release builds unless a Six Labors license key is configured, even for projects that qualify for its Apache 2.0 terms (open source, or under 1M USD revenue): open-source users register for a free community key. That would tie every Docker build to an external account and a CI secret. The last version without the check, 3.1.12, no longer receives fixes, which matters for code that decodes uploaded files.

## Decision
Use SkiaSharp (MIT, Google's Skia engine) with `SkiaSharp.NativeAssets.Linux.NoDependencies` for the Linux Docker image. Uploads are judged by decoding, EXIF orientation is applied with canvas transforms, and every stored file is re-encoded, which drops all metadata. All image handling sits behind `ImageProcessor` and `FileStore` in Infrastructure.

## Consequences
- No license key or account; Release builds work anywhere.
- A native library ships with the API image; pick SkiaSharp releases that have been out a couple of weeks.
- Switching libraries later touches only `ImageProcessor`.
