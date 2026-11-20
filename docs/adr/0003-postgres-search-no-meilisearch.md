# 3. Postgres search instead of Meilisearch

Status: Accepted (2026-11-20)

## Context
The original plan added Meilisearch for typo-tolerant search, while also keeping Postgres full-text as a fallback: two search systems plus an index-sync mechanism. Expected data is thousands of firearms, not millions.

## Decision
Use Postgres only: a weighted generated `tsvector` (`simple` config; Name A, AlternativeNames B, Summary C) with a GIN index, plus `pg_trgm` indexes on names and aliases for fuzzy matches like "ak47" → "AK-47". Because `array_to_string` is not IMMUTABLE, the generated column uses an immutable SQL wrapper function created in the migration.

## Consequences
- One fewer service to run, sync and back up.
- Search-as-you-type quality is "good enough", not Meilisearch-grade. Revisit if real users complain or the dataset grows by orders of magnitude.
