# 5. Controlled tags instead of a single category

Status: Accepted (2026-11-20)

## Context
The original single `Category` enum overlapped (Revolver vs Handgun; M4 is both Carbine and AssaultRifle; M14 is a BattleRifle also used as a marksman rifle). Many firearms don't fit one bucket, and arbitrary choices make filters look wrong to knowledgeable users.

## Decision
Replace `Category` with a controlled, admin-managed `Tag` table (`Slug`, `Name`, `Group`: Type / Role / Era), many-to-many with Firearm. A `WikidataClassTagMap` turns Wikidata "instance of" classes into tags at import time. Tags drive the faceted filter (multi-select, disjunctive counts). There is no separate primary category.

## Consequences
- Filters and facets work on tags; list items show tags rather than one category.
- Tag vocabulary needs light curation to avoid near-duplicates.
