import ReactMarkdown, { type Components } from "react-markdown";

// Prose is ours or adapted from Wikipedia; it keeps only what the importer writes. Raw HTML is never rendered
// (react-markdown escapes it without a rehype-raw plugin) and javascript: links are dropped by its URL transform.
const ALLOWED = ["p", "h2", "h3", "h4", "ul", "ol", "li", "a", "strong", "em"];

/** Headings shift down so prose sits under the page's own section headings (h2). */
const components: Components = {
  h2: ({ children }) => <h3 className="mt-5 mb-1 font-serif text-xl">{children}</h3>,
  h3: ({ children }) => <h4 className="mt-4 mb-1 font-semibold">{children}</h4>,
  h4: ({ children }) => <h5 className="mt-3 mb-1 font-semibold">{children}</h5>,
  p: ({ children }) => <p className="my-2">{children}</p>,
  ul: ({ children }) => <ul className="my-2 list-disc space-y-0.5 pl-6">{children}</ul>,
  ol: ({ children }) => <ol className="my-2 list-decimal space-y-0.5 pl-6">{children}</ol>,
  a: ({ href, children }) => {
    const external = href?.startsWith("http");
    return (
      <a href={href} {...(external ? { rel: "noopener noreferrer" } : {})}>
        {children}
      </a>
    );
  },
};

export function Markdown({ children }: { children: string }) {
  return (
    <ReactMarkdown allowedElements={ALLOWED} unwrapDisallowed components={components}>
      {children}
    </ReactMarkdown>
  );
}
