# 7. Variants carry the Wikidata QID of a merged firearm

Status: Accepted (2026-11-27)

## Context
ADR-0004 makes each Wikidata item one Firearm and leaves folding minor items into variants to an admin "merge into variant" tool. Merging deletes the source firearm, but the importer matches on `WikidataId` only: its next run would find the QID unclaimed and create the firearm again as a new draft, undoing the merge every night.

Alternatives considered: a list of "absorbed" QIDs on the target firearm (an identity with no row behind it), or letting imports recreate the draft and deleting it each time (endless chores).

## Decision
`Variant` gets an optional, unique `WikidataId`. Merging moves the source firearm's QID to the variant it becomes. The Wikidata importer treats a QID held by a variant as known: it creates no firearm for it and reports it as merged. Only drafts can be merged, so nothing published disappears; the old slug becomes a `SlugRedirect` to the target firearm.

## Consequences
- A QID is unique across firearms and variants together; the importer and the merge both check both tables.
- Later importers could fill variant fields (specs, dates) from the variant's QID; nothing does yet.
- Undoing a merge is manual: create the firearm again and move the QID back by hand.
