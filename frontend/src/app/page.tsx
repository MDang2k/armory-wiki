import Link from "next/link";

// Placeholder until slice 5b builds the home page (search, counts, browse by tag, era and country).
export default function Home() {
  return (
    <div className="space-y-3">
      <h1 className="font-serif text-3xl">ArmoryWiki</h1>
      <p className="text-muted">
        An encyclopedia of firearms: their designs, the people and organizations behind them, the cartridges they fire
        and how they work.
      </p>
      <p>
        <Link href="/firearms">Browse firearms</Link>
      </p>
    </div>
  );
}
