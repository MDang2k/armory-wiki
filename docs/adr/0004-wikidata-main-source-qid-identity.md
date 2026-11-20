# 4. Wikidata is the main content source; QIDs are import identity

Status: Accepted (2026-11-20)

## Context
A handful of hand-seeded firearms can't make a useful encyclopedia. Wikidata (CC0) has thousands of small-arms items with designers, manufacturers, countries, cartridges, "based on" links and some specs. Imports will be re-run, and admins will correct imported data.

## Decision
Importers (Wikidata, Commons images, Wikipedia prose, Forgotten Weapons videos via the YouTube Data API) run right after the admin API and before the frontend. `Firearm`, `Person`, `Organization`, `Cartridge` and `Country` carry a unique `WikidataId`, and importers match on QID only, never on name; hand-seeded rows carry their real QIDs. Each Wikidata item becomes one Firearm with a base variant; folding minor items into variants is a manual admin action. Imports land as Draft, and a publish gate requires country, design date, mechanism and a primary cartridge. Admin edits add field names to `LockedFields`, which importers never overwrite.

## Consequences
- Fields Wikidata lacks (notably operating mechanism) must be nullable and filled by hand before publishing.
- Wikidata's model/variant granularity differs from ours, so the admin UI needs a "merge into variant" tool.
- No per-field provenance beyond `LockedFields` and one Wikidata citation per entity; add more if conflicts become common.
