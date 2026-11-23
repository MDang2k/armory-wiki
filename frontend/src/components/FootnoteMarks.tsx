import { footnoteId } from "@/lib/citations";

/** Superscript links to the Sources list: "[2][4]". */
export function FootnoteMarks({ numbers }: { numbers: number[] }) {
  if (numbers.length === 0) {
    return null;
  }

  return (
    <sup className="ml-0.5 text-[0.7em] whitespace-nowrap">
      {numbers.map((n) => (
        <a key={n} href={`#${footnoteId(n)}`} aria-label={`Source ${n}`}>
          [{n}]
        </a>
      ))}
    </sup>
  );
}
