# ArmoryWiki

An encyclopedic database of firearms: design history, designers, specifications, cartridges and operating mechanisms, with filtered search, family trees and timelines.

The site is an encyclopedia. It covers history, specifications, service use and how mechanisms work conceptually. Manufacturing instructions, conversion guides and anything that helps circumvent the law are out of scope (see [PLAN.md §8](PLAN.md#8-content-scope-for-guidelines-and-moderation)).

## Status

Early development, built phase by phase from [PLAN.md](PLAN.md).

| Phase | Scope | Status |
|---|---|---|
| 0 | Repository and infrastructure | Done |
| 1 | Domain model, database, development seed | Done |
| 2 | Read API | Done |
| 3 | Admin API, sources and media | Done |
| 4 | Importers (Wikidata, Commons, Wikipedia, Forgotten Weapons videos) | Done (the video import awaits a YouTube API key for its first live run) |
| 5 | Frontend, read-only | Done |
| 6 | Deploy | Next |
| 7 | Admin UI | |
| 8 | Exploration: family tree, timeline, compare | |

## Stack

- **Backend:** ASP.NET Core Web API on .NET 10, EF Core 10 with Npgsql, PostgreSQL 17
- **Search:** Postgres full-text search plus `pg_trgm` fuzzy matching (no separate search engine)
- **Frontend:** Next.js 16 (App Router, Cache Components), TypeScript, Tailwind CSS
- **Tests:** xUnit with Testcontainers (real Postgres), Vitest for the frontend
- **Hosting (planned):** one VPS running Docker Compose behind Caddy

## Getting started

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), [Node.js](https://nodejs.org) 20.9 or later, and [Docker](https://www.docker.com/).

1. Create your local environment file and set a real password in it:

   ```bash
   cp .env.example .env
   ```

2. Start Postgres:

   ```bash
   docker compose up -d
   ```

3. Install the EF Core CLI and store the connection string, using the password from `.env`:

   ```bash
   cd backend
   dotnet tool restore
   dotnet user-secrets set ConnectionStrings:ArmoryWiki "Host=localhost;Port=5432;Database=armorywiki;Username=armorywiki;Password=<from .env>" --project src/ArmoryWiki.Api
   ```

4. Run the API. In Development it applies migrations and seeds sample data on startup:

   ```bash
   dotnet run --project src/ArmoryWiki.Api
   ```

   It listens on http://localhost:5080. The OpenAPI document is at `/openapi/v1.json`.

5. Run the frontend in another terminal:

   ```bash
   cd frontend
   npm install
   npm run dev
   ```

   It listens on http://localhost:3000.

### Tests

```bash
cd backend && dotnet test     # needs Docker running; starts a throwaway Postgres
cd frontend && npm run lint && npm run build
```

## Repository layout

```
backend/
  src/ArmoryWiki.Domain/          entities, enums, FuzzyDate
  src/ArmoryWiki.Infrastructure/  DbContext, EF configurations, migrations, seed
  src/ArmoryWiki.Api/             Minimal API endpoints and importers (CLI commands)
  tests/ArmoryWiki.Api.Tests/     integration tests against real Postgres
frontend/                         Next.js app
docs/adr/                         architecture decision records
PLAN.md                           implementation plan, phase by phase
CLAUDE.md                         conventions and commands for coding agents
```

## Data model in brief

- A **firearm** is a base model (AK-47). Its specifications live on **variants**, and exactly one variant is the base variant.
- Measurements are stored in **metric**; the frontend converts to imperial for display.
- Historical dates use **FuzzyDate**, which can be known only to the month, year or decade, can be approximate, and can be a range.
- **Operating mechanisms** form a tree, and filtering by a mechanism includes everything beneath it.
- Firearms carry **tags** (type, role, era) instead of a single category.
- Every entity has a **Draft/Published** status. Public reads only ever see Published rows.

## Data sources

Wikidata is the main source of structured data, and entities are matched to it by QID ([ADR-0004](docs/adr/0004-wikidata-main-source-qid-identity.md)). Images come from Wikimedia Commons with their original license and attribution. Prose may be adapted from Wikipedia with attribution to the exact revision. Copyrighted sources, such as books or Forgotten Weapons videos, are cited or linked, never copied.

## Decisions

The major design choices are recorded in [docs/adr/](docs/adr/):

1. [Prose is licensed CC BY-SA 4.0](docs/adr/0001-cc-by-sa-content-license.md)
2. [Single VPS, same-origin deploy](docs/adr/0002-single-vps-same-origin.md)
3. [Postgres search instead of Meilisearch](docs/adr/0003-postgres-search-no-meilisearch.md)
4. [Wikidata is the main content source](docs/adr/0004-wikidata-main-source-qid-identity.md)
5. [Controlled tags instead of a single category](docs/adr/0005-tags-instead-of-category.md)
6. [SkiaSharp for image processing](docs/adr/0006-skiasharp-for-images.md)

## License

- **Source code:** [MIT](LICENSE).
- **Article text and other prose:** [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/).
- **Images:** the license of their source, shown next to each image.
