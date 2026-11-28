/** The API's limits (ImageProcessor): JPEG, PNG or WebP, up to 20 MB. Checked here first so a wrong file fails fast. */
export const IMAGE_TYPES = ["image/jpeg", "image/png", "image/webp"] as const;
export const IMAGE_MAX_BYTES = 20 * 1024 * 1024;

/** Why this file can't be uploaded, or null when it can. The API still decodes it and has the last word. */
export function imageFileProblem(file: { type: string; size: number }): string | null {
  if (!(IMAGE_TYPES as readonly string[]).includes(file.type)) {
    return "Choose a JPEG, PNG or WebP image.";
  }

  if (file.size === 0 || file.size > IMAGE_MAX_BYTES) {
    return `Images must be between 1 byte and ${IMAGE_MAX_BYTES / 1024 / 1024} MB.`;
  }

  return null;
}

/** The image ids in their new order after moving one up (-1) or down (+1); unchanged at either end. */
export function moved(ids: number[], index: number, by: -1 | 1): number[] {
  const target = index + by;
  if (target < 0 || target >= ids.length) {
    return ids;
  }

  const next = [...ids];
  [next[index], next[target]] = [next[target], next[index]];
  return next;
}
