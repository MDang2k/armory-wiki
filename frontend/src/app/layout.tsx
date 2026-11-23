import type { Metadata } from "next";
import Link from "next/link";
import "./globals.css";

export const metadata: Metadata = {
  title: { default: "ArmoryWiki", template: "%s · ArmoryWiki" },
  description: "Encyclopedic database of firearms: history, designers, specifications, cartridges and mechanisms.",
};

// Timeline joins in Phase 8. "Designers" is a role (GLOSSARY.md), so people and organizations are listed apart.
const NAV = [
  { href: "/firearms", label: "Firearms" },
  { href: "/cartridges", label: "Cartridges" },
  { href: "/mechanisms", label: "Mechanisms" },
  { href: "/people", label: "People" },
  { href: "/organizations", label: "Organizations" },
] as const;

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className="h-full">
      <body className="flex min-h-full flex-col text-[15px] leading-relaxed">
        <header className="border-b border-border bg-surface">
          <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-x-6 gap-y-2 px-4 py-3">
            <Link href="/" className="font-serif text-xl font-semibold text-foreground! no-underline">
              ArmoryWiki
            </Link>
            <nav aria-label="Main" className="flex flex-wrap gap-x-4 gap-y-1 text-sm">
              {NAV.map((item) => (
                <Link key={item.href} href={item.href}>
                  {item.label}
                </Link>
              ))}
            </nav>
            {/* A plain GET form: works before hydration and without JavaScript. Firearms only (PLAN.md Phase 5). */}
            <form action="/firearms" role="search" className="w-full sm:ml-auto sm:w-auto">
              <label htmlFor="site-search" className="sr-only">
                Search firearms
              </label>
              <input
                id="site-search"
                name="q"
                type="search"
                placeholder="Search firearms"
                className="w-full rounded border border-border bg-background px-3 py-1.5 text-sm sm:w-64"
              />
            </form>
          </div>
        </header>
        <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-6">{children}</main>
        <footer className="border-t border-border bg-surface text-xs text-muted">
          <div className="mx-auto max-w-6xl space-y-1 px-4 py-4">
            <p>
              Text is available under the{" "}
              <a href="https://creativecommons.org/licenses/by-sa/4.0/" rel="license">
                Creative Commons Attribution-ShareAlike 4.0
              </a>{" "}
              license; text adapted from Wikipedia says so on its page. Images are available under the license shown
              with each of them.
            </p>
            <p>An encyclopedia of firearms history and design. It does not cover manufacturing, conversion or modification.</p>
          </div>
        </footer>
      </body>
    </html>
  );
}
