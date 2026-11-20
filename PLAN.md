# ArmoryWiki — Implementation Plan

> A structured, encyclopedic database of firearms: design history, designers, specifications, cartridges and operating mechanisms, with filtered search, family trees and timelines.
>
> **For Claude Code:** Work through this plan one phase at a time. Each phase ends with a "Done when" checklist: verify every item (build, run, test) before starting the next phase. Don't skip ahead or add features that aren't listed here. If something in this plan is ambiguous, ask before guessing. Big decisions are recorded as ADRs in `docs/adr/`; don't contradict them silently.

---

## 1. Tech stack (fixed decisions)

| Layer | Choice |
|---|---|
| Backend | ASP.NET Core Web API, **.NET 10**, C# |
| ORM | EF Core 10 + **Npgsql** provider |
| Database | **PostgreSQL 17** (+ `pg_trgm` extension) |
| DB naming | `EFCore.NamingConventions` → snake_case tables/columns |
| Validation | FluentValidation |
| API docs | Built-in OpenAPI (`Microsoft.AspNetCore.OpenApi`) + Scalar UI in Development |
| Tests (backend) | xUnit + Testcontainers (PostgreSQL) + `WebApplicationFactory` |
| Frontend | **Next.js 16 (App Router, Cache Components) + TypeScript**, prerendered + cached for SEO |
| Styling | Tailwind CSS |
| Data fetching | Server Components fetch the API directly inside `"use cache"` functions (`cacheLife` + `cacheTag`); TanStack Query for client-side interactive parts (filters) |
| API client types | Generated from the OpenAPI spec with `openapi-typescript` |
| Search | Postgres full-text (weighted `tsvector`, `simple` config) + `pg_trgm` fuzzy name matching. **No Meilisearch** (ADR-0003) |
| Images | ImageSharp thumbnails; files on a Docker volume served by Caddy. **No MinIO** |
| Hosting | One VPS, Docker Compose, Caddy reverse proxy with automatic TLS, `/api` on the same origin as the site (ADR-0002) |
| CI/CD | GitHub Actions → images on GHCR → SSH deploy |

**Why Next.js and not plain Vite React:** the site's traffic will come from search engines, so firearm pages need to be rendered on the server.

**Content license:** prose is **CC BY-SA 4.0** (Wikipedia-compatible); see ADR-0001.

---

## 2. Repository layout

```
armory-wiki/
├── PLAN.md
├── CLAUDE.md                      # conventions + agent skills config
├── docs/adr/                      # architecture decision records
├── docker-compose.yml             # local dev: Postgres
├── deploy/
│   ├── docker-compose.prod.yml    # Phase 6: caddy, api, web, migrate, postgres
│   └── Caddyfile
├── .editorconfig
├── backend/
│   ├── ArmoryWiki.sln
│   ├── src/
│   │   ├── ArmoryWiki.Domain/         # entities, enums, value objects. No dependencies.
│   │   ├── ArmoryWiki.Infrastructure/ # DbContext, EF configurations, migrations, seeding, file store, importers
│   │   └── ArmoryWiki.Api/            # endpoints, DTOs, validators, Program.cs, CLI subcommands
│   └── tests/
│       └── ArmoryWiki.Api.Tests/      # integration tests (Testcontainers) + importer mapper tests (fixtures)
└── frontend/
    ├── package.json
    ├── src/
    │   ├── app/                   # Next.js routes
    │   ├── components/
    │   ├── lib/api/               # generated types + fetch wrapper
    │   └── lib/units.ts           # metric ⇄ imperial display helpers
    └── ...
```

Keep it to three backend projects. No separate Application layer, MediatR or repository pattern: endpoints use the `DbContext` directly, with query logic in small static/extension classes when it grows.

---

## 3. Domain model

### 3.1 Conventions
- All IDs: `int` identity primary keys. Every public entity also has a URL-safe `Slug` (e.g. `ak-47`, `john-moses-browning`). Slugs are unique per table, except `Variant.Slug`, which is unique **per firearm**.
- All measurements are stored in **metric**: mm, grams, m/s, metres, rounds/min. Conversion to imperial happens only in the frontend.
- Every entity has `CreatedAt` and `UpdatedAt` (UTC, set automatically in `SaveChangesAsync`).
- Long-form text (history, descriptions) is **Markdown** in a `text` column.
- **Publishing:** `Firearm`, `Person`, `Organization` and `Cartridge` have `Status` (Draft, Published). All public GET endpoints return Published rows only, via an EF global query filter that admin endpoints bypass with `IgnoreQueryFilters()`.
- **Import identity:** `Firearm`, `Person`, `Organization`, `Cartridge` and `Country` have `WikidataId` (nullable, unique, e.g. `Q37116`). Importers match on QID only, never on name (ADR-0004).
- **Overwrite protection:** importable entities have `LockedFields` (`text[]`). Any admin edit appends the names of the fields it changed; importers never write a locked field.

### 3.2 Uncertain dates: `FuzzyDate` (owned type)
Historical dates are often approximate. Never use a single `DateOnly` for historical dates.

```csharp
public enum DatePrecision { Day, Month, Year, Decade }

public sealed class FuzzyDate          // EF owned type, columns prefixed e.g. design_date_*
{
    public int Year { get; set; }          // required: earliest/best-known year
    public int? Month { get; set; }
    public int? Day { get; set; }
    public int? YearTo { get; set; }       // for ranges, e.g. "1942–1944"
    public DatePrecision Precision { get; set; }
    public bool IsApproximate { get; set; } // "circa"; independent of precision ("c. 1940s")
}
```
Year filters use **overlap**: an item matches `[from, to]` when `Year <= to AND COALESCE(YearTo, Year) >= from`. Index `Year`.

### 3.3 Entities

**Firearm** (the base model, e.g. "AK-47")
- `Id`, `Slug`, `Name`, `AlternativeNames` (`string[]`, Postgres array), `WikidataId?`, `Status`, `LockedFields`
- `Tags` (many-to-many with `Tag`; replaces a single category; see below)
- `CountryOfOriginId?` → Country
- `DesignDate` (FuzzyDate?), `ProductionStart` (FuzzyDate?), `ProductionEnd` (FuzzyDate?)
- `OperatingMechanismId?` → OperatingMechanism
- `FeedSystem?` (enum: DetachableBoxMagazine, InternalMagazine, Belt, Drum, Tubular, Cylinder, StripperClip, EnBlocClip, Single, Other)
- `Summary` (short plain text, ≤ 500 chars), `DevelopmentHistory` (Markdown), `ServiceHistory` (Markdown)
- `WikipediaTitle?`, `WikipediaRevisionId?` (long?): set when prose was adapted from Wikipedia; drives the attribution footer
- Navigation: `Variants`, `Designers` (FirearmDesigner), `Manufacturers` (FirearmManufacturer), `RelationsFrom` / `RelationsTo` (FirearmRelation), `Adoptions`, `Media`, `Videos` (VideoReference)
- **Publish gate:** Draft → Published fails validation unless `CountryOfOriginId`, `DesignDate`, `OperatingMechanismId` are set and the base variant has a primary cartridge. There is no separate "action type": the mechanism tree plus `Variant.FiringModes` cover it.

**Variant** (holds the actual specifications)
- `Id`, `FirearmId`, `Slug` (unique per firearm), `Name`, `IsBaseVariant` (bool; exactly one per firearm)
- `IntroducedDate` (FuzzyDate?)
- Specs, all nullable: `WeightEmptyG`, `OverallLengthMm`, `OverallLengthFoldedMm`, `BarrelLengthMm`, `RateOfFireMinRpm`, `RateOfFireMaxRpm`, `MuzzleVelocityMs`, `EffectiveRangeM`, `MagazineCapacityStandard`
- `FiringModes` (flags enum: Single, Burst, Automatic)
- `ExtraSpecs` (`jsonb`, `Dictionary<string, string>`) for rare/odd specs
- Navigation: `Chamberings` (VariantCartridge)
- Creating a firearm always creates its base variant in the same transaction, so "exactly one" always holds. Enforced by a partial unique index on `(firearm_id) WHERE is_base_variant`.

**Tag** (controlled vocabulary, admin-managed; ADR-0005)
- `Id`, `Slug`, `Name`, `Group` (enum: Type, Role, Era)
- Examples: Type: Handgun, Revolver, SubmachineGun, Rifle, Carbine, AssaultRifle, BattleRifle, Shotgun, MachineGun; Role: DesignatedMarksman, Sniper, PersonalDefense; Era: WW1, WW2, ColdWar
- `FirearmTag` (`FirearmId`, `TagId`), PK (FirearmId, TagId)
- `WikidataClassTagMap` (`Qid`, `TagId`), PK (Qid, TagId): maps Wikidata "instance of" classes to tags during import

**Person** (designers)
- `Id`, `Slug`, `FullName`, `BirthDate` (FuzzyDate?), `DeathDate` (FuzzyDate?), `NationalityId?` → Country, `Biography` (Markdown), `WikidataId?`, `Status`, `LockedFields`

**Organization** (manufacturers, design bureaus, state arsenals)
- `Id`, `Slug`, `Name`, `CountryId?`, `Type` (enum: Company, DesignBureau, StateArsenal, Other), `Founded` (FuzzyDate?), `Description` (Markdown), `WikidataId?`, `Status`, `LockedFields`

**Country**
- `Id`, `Slug` (unique; used by filters and URLs), `Name`, `IsoCode?` (informational only; historical states often have none), `IsHistorical` (bool), `WikidataId?`
- Importers auto-create unknown countries from their QID, with `IsHistorical = true` when Wikidata has a dissolution date.

**Cartridge**
- `Id`, `Slug`, `Name` (e.g. "7.62×39mm"), `AlternativeNames` (`string[]`), `WikidataId?`, `Status`, `LockedFields`
- `BulletDiameterMm`, `CaseLengthMm`, `OverallLengthMm` (nullable)
- `Type` (enum: Pistol, Intermediate, FullPowerRifle, Magnum, Shotgun, Rimfire, Other)
- `ParentCartridgeId` → Cartridge (self-reference, nullable)
- `DesignDate` (FuzzyDate?), `CountryOfOriginId?`, `Description` (Markdown)

**OperatingMechanism** (tree)
- `Id`, `Slug`, `Name`, `ParentId` → OperatingMechanism (nullable), `Path` (e.g. `/gas-operated/short-stroke-piston/`), `Explanation` (Markdown: a conceptual, encyclopedic explanation)
- Seed hierarchy, for example:
  - Manual → Bolt action, Lever action, Pump action, Break action
  - Blowback → Simple blowback, Advanced primer ignition, Roller-delayed, Lever-delayed, Gas-delayed
  - Recoil-operated → Short recoil, Long recoil
  - Gas-operated → Long-stroke piston, Short-stroke piston, Direct impingement, Gas trap
- Filtering by a mechanism **includes all its descendants** via `Path LIKE 'prefix%'`.
- Wikidata rarely has this; it is set manually and is part of the publish gate.

**SlugRedirect**
- `EntityType`, `OldSlug`, `EntityId`, PK (EntityType, OldSlug). Written whenever a slug changes. Detail endpoints that miss a slug check this table and return a redirect hint so the frontend issues a **301** to the current slug.

### 3.4 Join / relationship entities

| Entity | Fields | Key / rules |
|---|---|---|
| `FirearmDesigner` | `Id`, `FirearmId`, `PersonId?`, `OrganizationId?`, `Role` (enum: LeadDesigner, CoDesigner, Refinement) | Check: exactly one of PersonId / OrganizationId. Unique `NULLS NOT DISTINCT` (FirearmId, PersonId, OrganizationId, Role) |
| `FirearmManufacturer` | `Id`, `FirearmId`, `OrganizationId`, `From` (FuzzyDate?), `To` (FuzzyDate?), `IsLicensed` (bool) | Surrogate key: same org can appear for several periods |
| `VariantCartridge` | `VariantId`, `CartridgeId`, `IsPrimary` (bool) | PK (VariantId, CartridgeId); partial unique index `(variant_id) WHERE is_primary` |
| `FirearmRelation` | `FromFirearmId`, `ToFirearmId`, `Type` (enum: DerivedFrom, InfluencedBy, CompetedAgainst, Replaced), `Note` | PK (From, To, Type); check `From <> To` |
| `Adoption` | `Id`, `FirearmId`, `CountryId`, `From` (FuzzyDate?), `To` (FuzzyDate?), `Note` | Surrogate key: a country can re-adopt |

`FirearmRelation` direction: "AKM **DerivedFrom** AK-47" is stored as From=AKM, To=AK-47. "M16 **Replaced** M14" is stored as From=M16, To=M14.

Designers can be people or organizations (Wikidata's "designed by" often points at a design bureau). Pages render them together, e.g. "M. Kalashnikov; Izhmash design bureau".

### 3.5 Sources, citations and media (Phase 3)

**Source**: `Id`, `Type` (enum: Book, Article, Website, Patent, Manual, Video, Dataset), `Title`, `Author`, `Publisher`, `Year`, `Url`, `Isbn`
**Citation**: `Id`, `SourceId`, `EntityType`, `EntityId`, `FieldName?`, `Page?`. Every imported entity gets one Citation to the Wikidata source.

**Media**: `Id`, `FirearmId?`, `PersonId?`, `StorageKey`, `Caption`, `Author`, `License` (short name, e.g. "CC BY-SA 4.0"), `SourceUrl` (e.g. the Commons file page), `Width`, `Height`, `CommonsFileName?` (unique when set). **License, Author and SourceUrl are required.** Originals plus thumbnail and medium sizes (ImageSharp) are stored on the file-store volume.

**VideoReference**: `Id`, `FirearmId`, `YouTubeVideoId` (unique per firearm), `Title`, `Channel`, `PublishedAt`, `Status` (enum: Suggested, Approved, Rejected). Only `Approved` videos are public. Embedded, never re-hosted.

### 3.6 Later (not scheduled; do **not** create)
`User` (public registration), `Revision`, `Conflict`, and the public wiki-editing flow. Revisit when there is real outside interest in contributing.

### 3.7 Indexes
- Unique: every `Slug` (Variant: per firearm), `Country.Slug`, every `WikidataId`, `Media.CommonsFileName`
- B-tree: `Firearm.DesignDate.Year`, `Firearm.CountryOfOriginId`, `Firearm.OperatingMechanismId`, `FirearmTag.TagId`, `OperatingMechanism.Path`, `Firearm.Status`
- Partial unique: base variant per firearm, primary cartridge per variant
- **Full-text:** generated `tsvector` column on Firearm with GIN index, `simple` config, weights Name **A**, AlternativeNames **B**, Summary **C**. Postgres generated columns require IMMUTABLE expressions and `array_to_string` is only STABLE, so the migration first creates an `immutable` SQL wrapper function (e.g. `armorywiki_immutable_array_to_string(text[])`) and the column uses it. Write this column and function with raw SQL in the migration; `HasGeneratedTsVectorColumn` can't express arrays.
- **Trigram:** `pg_trgm` GIN index on `lower(Name)` (and on the joined alternative names via the same wrapper), so "ak47" finds "AK-47".

---

## 4. API (Phase 2 scope)

Base path `/api`. JSON, camelCase. Paged responses use:

```json
{ "items": [...], "page": 1, "pageSize": 24, "totalCount": 312 }
```

Errors use ProblemDetails (RFC 9457). Use **Minimal API** endpoint groups, one file per resource (`FirearmEndpoints.cs` etc.). Public endpoints return Published rows only.

### Filters (multi-select)
Filter params accept comma-separated values: **OR within** a param, **AND across** params. Example: `?tag=carbine,assault-rifle&country=soviet-union,russia` = (carbine OR assault rifle) AND (USSR OR Russia).

### Firearms
- `GET /api/firearms`: list/search. Query params (all optional, all combinable):
  - `q` (full-text + trigram fallback), `tag` (tag slugs), `country` (country slugs), `designer` (person or organization slugs), `manufacturer` (org slugs), `cartridge` (cartridge slugs), `mechanism` (mechanism slugs, include descendants), `designedFrom` / `designedTo` (year, overlap semantics), `sort` (`name`, `designYear`, `-designYear`, `relevance` default when `q` is set), `page`, `pageSize` (max 100)
  - Returns `FirearmListItemDto`: slug, name, tags, country, designYear + precision + isApproximate, mechanism name, primary cartridge name (base variant's primary chambering), thumbnail URL (null when no media)
- `GET /api/firearms/{slug}`: full detail: all fields, variants with specs and chamberings, designers with roles, manufacturers, adoptions, direct relations (both directions), approved videos, citations, Wikipedia attribution fields. Unknown slug → check `SlugRedirect` → `{ "redirectTo": "<slug>" }` with 301-equivalent semantics for the frontend, else 404.
- `GET /api/firearms/{slug}/family-tree?depth=3`: nodes + edges of `DerivedFrom` relations up and down from this firearm (for React Flow). Traversal keeps a visited set: imported relations may contain cycles.
- `GET /api/firearms/compare?slugs=a,b,c`: up to 4 base variants with specs side by side
- `GET /api/firearms/facets`: counts per tag (grouped), country, mechanism, decade. Accepts the same filters as the list endpoint. **Disjunctive faceting:** each facet's counts apply every filter *except its own*, so selecting "carbine" doesn't zero out the other tag counts.

### Other resources
- `GET /api/people`, `GET /api/people/{slug}` (includes firearms designed, sorted by design year)
- `GET /api/organizations`, `GET /api/organizations/{slug}` (firearms designed and manufactured)
- `GET /api/cartridges`, `GET /api/cartridges/{slug}` (includes parent/child cartridges, firearms chambered in it)
- `GET /api/mechanisms` (full tree), `GET /api/mechanisms/{slug}` (explanation + firearms using it or a descendant)
- `GET /api/countries`, `GET /api/tags`
- `GET /api/timeline?from=1900&to=1960&country=&tag=`: minimal items (slug, name, designYear, yearTo, tags) for the timeline view

### Caching
Server-side fetches from Next.js are cached by Next (`"use cache"` + tags, §5), so API responses consumed server-side send `Cache-Control: no-store` or a short private value. Only endpoints the browser calls directly (facets, list during client-side filtering) send `Cache-Control: public, max-age=60`.

Write endpoints come in Phase 3 (admin).

---

## 5. Frontend routes

| Route | Content |
|---|---|
| `/` | Search box, featured firearms, browse by tag/era/country |
| `/firearms` | Filter sidebar (multi-select checkboxes with disjunctive facet counts) + results grid. **All filter state lives in URL query params.** |
| `/firearms/[slug]` | Infobox (specs table per variant, variant switcher), designers, cartridge links, mechanism link, development history, service history, adoptions, related firearms, image gallery with attribution, approved videos (embedded), citations as footnotes, Wikipedia attribution footer when applicable |
| `/firearms/[slug]/family-tree` | React Flow graph |
| `/compare?slugs=…` | Side-by-side spec table |
| `/people/[slug]` | Biography + design timeline |
| `/organizations/[slug]` | Description + firearms designed / manufactured |
| `/cartridges/[slug]` | Specs, parent/child cartridges, firearms chambered in it |
| `/mechanisms`, `/mechanisms/[slug]` | Tree + explanation + firearms using it |
| `/timeline` | Horizontal timeline with filters |
| `/admin/...` | Phase 7 |

- Detail pages: Next 16 **Cache Components** (`cacheComponents: true` in `next.config.ts`). Data loaders are `"use cache"` functions with `cacheLife('hours')` and `cacheTag(...)` (`firearm:<slug>`, `firearms:list`, `person:<slug>`, …), plus `generateMetadata` for title/description/OpenGraph. Route segment configs like `export const revalidate` are not allowed with Cache Components; read `node_modules/next/dist/docs/01-app/02-guides/migrating-to-cache-components.md` before Phase 5.
- **On-demand revalidation:** a secret-protected route `POST /api/revalidate` (Next route handler; Caddy routes it to Next, not the API) takes a list of tags. The API calls it after every admin write and after each import batch.
- **Units:** the server always renders **metric**. The Metric/Imperial toggle is a small client component that stores the choice in `localStorage` and swaps displayed values after hydration. Never read cookies in Server Components for this; it would make pages dynamic.
- **Redirects:** when the API returns `redirectTo`, the page calls `permanentRedirect()` to the new slug.
- **Attribution:** firearm pages with `WikipediaTitle` show "Text adapted from Wikipedia: <link to that revision>, CC BY-SA 4.0". The site footer states the CC BY-SA 4.0 license for prose.
- **Dev proxy:** `next.config` `rewrites` forward `/api/*` (except `/api/revalidate`) to the backend in development, so the browser always talks to one origin. No CORS needed.

---

## 6. Phases

### Phase 0: Repository and infrastructure
- [ ] Create folder structure from §2, `.editorconfig`, `.gitignore` (dotnet + node)
- [ ] `docker-compose.yml` with `postgres:17` (volume, healthcheck, port 5432, credentials via `.env` with a committed `.env.example`)
- [ ] Backend solution with the three projects and the test project, project references: Api → Infrastructure → Domain
- [ ] Enable nullable reference types, `TreatWarningsAsErrors` on all projects
- [ ] Next.js app in `frontend/` with TypeScript, Tailwind, ESLint, `src/` dir, App Router
- [ ] Extend `CLAUDE.md` with: how to start infra, run API, run frontend, run tests, add a migration; the conventions from §3.1 and §7; "never edit applied migrations"

**Done when:** `docker compose up -d` gives a healthy Postgres; `dotnet build` passes with zero warnings; `npm run dev` serves the Next.js start page.

### Phase 1: Domain model and database
- [ ] Entities, enums, `FuzzyDate`, `Tag`, `SlugRedirect` from §3.1–3.4 in `ArmoryWiki.Domain` (not §3.5 yet)
- [ ] `ArmoryWikiDbContext` with one `IEntityTypeConfiguration<T>` class per entity in `Infrastructure/Persistence/Configurations/`
- [ ] snake_case naming, `jsonb` for `ExtraSpecs`, Postgres arrays for `AlternativeNames` and `LockedFields`, enums stored as strings
- [ ] Global query filter for `Status == Published` on Firearm, Person, Organization, Cartridge
- [ ] Indexes from §3.7, including check constraints, partial unique indexes, the immutable wrapper function, the weighted `tsvector` column + GIN index, and `pg_trgm` indexes (raw SQL in the migration where EF can't express it)
- [ ] Automatic `CreatedAt`/`UpdatedAt` in `SaveChangesAsync`
- [ ] Initial migration
- [ ] **Seeder** (runs in Development on startup if DB is empty) using hand-written seed data under `Infrastructure/Seed/`. Every seeded firearm, person, organization, cartridge and country carries its **real Wikidata QID**. Look each QID up; never guess one. All seeded rows are `Published`.
  - Countries (incl. historical: USSR, East Germany, West Germany, Czechoslovakia, Yugoslavia, Nazi Germany / German Reich)
  - Full operating mechanism tree from §3.3 with short explanations, and `Path` populated
  - Tags from §3.3 plus initial `WikidataClassTagMap` rows for the obvious classes (assault rifle, battle rifle, carbine, submachine gun, pistol, revolver, machine gun, shotgun, sniper rifle)
  - ~11 cartridges (9×19mm Parabellum, .45 ACP, 7.62×39mm, 5.45×39mm, 5.56×45mm NATO, .223 Remington, 7.62×51mm NATO, .308 Winchester, 7.62×54mmR, .30-06 Springfield, 7.92×57mm Mauser) with parent links where applicable
  - ~6 designers (John Browning, Mikhail Kalashnikov, Eugene Stoner, Hugo Schmeisser, John Garand, Georg Luger)
  - ~15 firearms with base variants, tags and correct relations, e.g. AK-47, AKM, AK-74, SKS, M1 Garand, M14, AR-10, AR-15, M16, M4 carbine, StG 44, Browning Hi-Power, M1911, Luger P08, Mauser Kar98k
  - Seed data must be factually correct; use `IsApproximate = true` where dates are uncertain, and leave specs null rather than inventing values.

**Done when:** migration applies to a fresh DB; seeder populates it; integration tests (Testcontainers) confirm seeded counts, that querying mechanism "gas-operated" by `Path` returns firearms with short-stroke, long-stroke and DI mechanisms, that a second base variant or primary cartridge violates the partial unique index, and that Draft rows are hidden by the global filter.

### Phase 2: Read API
- [ ] All GET endpoints from §4 with DTOs (records) in `Api/Features/<Resource>/`
- [ ] Use projections (`Select` into DTOs). No returning entities, no lazy loading.
- [ ] Filtering implemented as composable `IQueryable<Firearm>` extension methods (one per filter), each taking a list of values
- [ ] Disjunctive facet counts (§4)
- [ ] Full-text search via the `tsvector` column (`WebSearchToTsQuery` with `simple`, ranked by `ts_rank`), falling back to trigram similarity on name/aliases when FTS finds nothing
- [ ] Family tree: load relations iteratively up to `depth` (max 5) with a visited set; return `{ nodes: [...], edges: [...] }`
- [ ] Pagination + `pageSize` clamp; `SlugRedirect` lookup then 404 ProblemDetails for unknown slugs
- [ ] OpenAPI document + Scalar UI in Development
- [ ] Cache headers per §4
- [ ] Integration tests for: each filter alone, multi-value filters, filter combinations, disjunctive facets, full-text search, trigram fallback ("ak47" → AK-47), mechanism descendants, design-year overlap (incl. `YearTo` ranges), pagination edges, family tree for AR-10 → AR-15 → M16 → M4, slug redirect, 404s, drafts never returned

**Done when:** `dotnet test` is green and every endpoint is browsable in Scalar.

### Phase 3: Admin API, sources and media
- [ ] ASP.NET Core Identity with cookie auth; a single `Admin` role; admin user seeded from configuration (user secrets in Development)
- [ ] POST/PUT/DELETE endpoints for all entities so far, protected by the `Admin` policy; admin reads bypass the Published filter
- [ ] Creating a firearm creates its base variant in the same transaction
- [ ] Every admin update appends changed field names to `LockedFields`
- [ ] Slug changes write a `SlugRedirect` row; slug auto-generated from name when not supplied, with collision suffix (`-2`)
- [ ] Publish endpoint enforcing the publish gate (§3.3); admin list endpoint exposes "missing for publish" fields
- [ ] FluentValidation for all write DTOs (slug format `^[a-z0-9]+(-[a-z0-9]+)*$`, positive measurements, `FuzzyDate` consistency: Month 1–12, Day valid for month, `YearTo >= Year`)
- [ ] **Source**, **Citation**, **Media**, **VideoReference** entities from §3.5 + migration + admin endpoints (incl. approve/reject for videos)
- [ ] File store: a single `FileStore` class writing to a configured directory (Docker volume); image upload endpoint generates thumbnail + medium (ImageSharp)
- [ ] Antiforgery/CSRF protection for cookie-authenticated writes
- [ ] Tests: unauthorized → 401/403, validation failures → 400 ProblemDetails, publish gate, LockedFields appended, slug redirect written, happy paths

**Done when:** an admin can create a firearm with variants, designers, chamberings, relations, an uploaded image and a citation via the API, publish it, and tests are green.

### Phase 4: Importers
Wikidata is the main content source (ADR-0004). All importers are CLI subcommands of the API project (`dotnet run --project src/ArmoryWiki.Api -- <command> [--dry-run]`), run by hand (on the VPS as one-shot `docker compose run` containers). No scheduler.

Shared rules:
- Idempotent upserts keyed on QID / Commons file name / YouTube video ID. Never write a field listed in `LockedFields`.
- Everything new lands as `Status = Draft`.
- Each importer is split into **fetch** (HTTP → raw JSON/HTML) and **map** (pure functions raw → entities). Mappers are tested with saved fixture files; no live-network tests.
- `--dry-run` prints counts and a sample without writing.
- Wikimedia requests send a descriptive User-Agent with a **project** contact address from configuration (`Import:ContactEmail`), not a personal one. Respect rate limits (sequential requests, backoff on 429).
- After each batch, call the frontend revalidate hook (no-op until Phase 5 exists).

- [ ] **`import-wikidata`**: SPARQL for small arms only: items whose "instance of" is a subclass of firearm, **excluding** artillery, autocannon, grenade launcher, air gun, flare gun, toy and individual-specimen classes, and requiring at least one of inception, manufacturer or ammunition. Maps: label, aliases, inception (with Wikidata precision → `FuzzyDate`), designer (human → Person, otherwise Organization), manufacturer, country of origin (auto-create Country), ammunition (→ Cartridge + primary chambering on the base variant), "based on" → `DerivedFrom` relation, "instance of" → tags via `WikidataClassTagMap`, and specs where present (mass, length, barrel length, muzzle velocity, rate of fire) normalized to metric. Skip any value whose unit is unknown or ambiguous. Each Wikidata item becomes one Firearm with an auto-created base variant. Adds a Citation to the Wikidata source per entity.
- [ ] **`import-commons`**: for firearms with a Wikidata image, fetch file metadata from the Commons API; allow only CC0, Public Domain, CC BY, CC BY-SA (any version), skip and log others; copy the file into the file store (never hotlink), generate sizes, store author (HTML stripped), license, file-page URL. Max 5 images per firearm, main image first.
- [ ] **`import-wikipedia`**: for firearms with an English Wikipedia sitelink, fetch article HTML via the Wikipedia REST API, convert to Markdown, map sections by heading (History / Development / Design → `DevelopmentHistory`; Service / Usage / Combat / Users → `ServiceHistory`), drop infoboxes, references, "See also" and everything else; store `WikipediaTitle` + `WikipediaRevisionId`. Firearms only for now.
- [ ] **`import-fw-videos`**: list the Forgotten Weapons channel uploads via the YouTube Data API (API key from configuration), fuzzy-match titles against firearm names and aliases, write `VideoReference` rows as `Suggested`. No scraping of forgottenweapons.com and no copied text.
- [ ] Tests: mapper fixtures for AK-47, M1 Garand and a firearm with a historical country; one Testcontainers test proving re-import is idempotent and respects `LockedFields`; seeded firearms are matched by QID, not duplicated.

**Done when:** a dry run reports sensible counts; a real run against a fresh DB imports firearms without duplicating seeded ones; a second run changes nothing; tests are green.

### Phase 5: Frontend, read-only
- [ ] Generate TS types from `/openapi/v1.json` into `src/lib/api/schema.d.ts` (`npm run gen:api` script); typed fetch wrapper with base URL from `API_BASE_URL` env var and fetch tags
- [ ] `rewrites` for `/api/*` in development (§5)
- [ ] Layout: header with search, nav (Firearms, Cartridges, Mechanisms, Designers, Timeline), units toggle (client component), footer with content license
- [ ] All routes from §5 except `/admin`, `/timeline` and `/firearms/[slug]/family-tree` (Phase 8)
- [ ] `/firearms`: multi-select filter sidebar driven by `/facets`, URL-synced state, debounced text search, pagination
- [ ] Firearm detail: variant switcher that updates the specs table; Markdown rendering via `react-markdown` (no raw HTML); image gallery with attribution and license under each image; approved videos as `youtube-nocookie.com` embeds; citations as footnotes; Wikipedia attribution
- [ ] `"use cache"` data loaders with `cacheLife` + `cacheTag`, and a `/api/revalidate` route handler (secret from env) calling `revalidateTag`; wire the API's revalidate client to it
- [ ] Slug redirects via `permanentRedirect()`
- [ ] `src/lib/units.ts`: mm⇄in, g⇄lb/oz, m/s⇄ft/s, m⇄yd, with unit tests (Vitest)
- [ ] `generateMetadata` on every detail page; `sitemap.ts` (Published only) and `robots.ts`
- [ ] Loading and error states (`loading.tsx`, `error.tsx`, `not-found.tsx`)
- [ ] Responsive down to 375px width

**Done when:** every seeded entity is reachable by clicking from the home page, filters round-trip via URL, an admin edit shows on the page after revalidation without waiting for the hour, `npm run build` and `npm run lint` pass, and Vitest is green.

### Phase 6: Deploy
- [ ] Dockerfiles for API and frontend (multi-stage, non-root)
- [ ] `deploy/docker-compose.prod.yml`: `caddy`, `web`, `api`, `postgres`, one-shot `migrate` (runs EF migrations via an idempotent migration bundle before `api` starts; **never** migrate on API startup in production), media volume
- [ ] `deploy/Caddyfile`: automatic TLS; `/api/revalidate` → web; `/api/*` → api; `/media/*` → media volume with long cache headers; everything else → web
- [ ] GitHub Actions: build + test on PR; on `main`, build images → push to GHCR → SSH to the VPS → `docker compose pull && docker compose up -d`
- [ ] Secrets via a `.env` on the VPS (never committed); admin credentials, DB password, revalidate secret, YouTube API key, import contact email
- [ ] Nightly backups: `pg_dump` + media volume to offsite storage (e.g. Backblaze B2); document and test a restore

**Done when:** the site is live on its domain over HTTPS, a push to `main` deploys automatically, and a backup has been restored into a scratch DB successfully.

### Phase 7: Admin UI
- [ ] `/admin` routes: login, list + edit forms for all entities (react-hook-form + zod), Markdown editor for prose fields, a `FuzzyDate` input component, tag picker
- [ ] Draft queue with "missing for publish" column and a publish button
- [ ] Image upload with required license/author/source fields
- [ ] Source + citation editor
- [ ] Video suggestion review screen (approve/reject)
- [ ] **Merge into variant** tool: turn an imported Firearm (e.g. M4A1) into a Variant of another (M4), moving chamberings and specs, writing a `SlugRedirect`, and deleting the old firearm

**Done when:** an admin can create a complete firearm page with images and citations, and review imported drafts, entirely through the UI.

### Phase 8: Exploration features
- [ ] Family tree page with React Flow (dagre layout, top-to-bottom, clickable nodes)
- [ ] `/timeline` (vis-timeline or a custom SVG), filters for country/tag, clicking an item opens the firearm
- [ ] Compare page with differences highlighted

**Done when:** each feature works against the imported dataset, not just the seed, and `npm run build` passes.

### Later (unscheduled)
- Public registration, `Contributor` / `TrustedEditor` roles, `Revision` entity with moderation queue, history and rollback, rate limiting, contribution guidelines (§8)
- Wikipedia prose import for people, organizations and cartridges
- Additional manual sources: patents, US public-domain military manuals, museum open-access collections, pre-1931 catalogs on the Internet Archive (entered as `Source` + `Citation`)

---

## 7. Conventions (copy the essentials into CLAUDE.md)

- Backend: file-scoped namespaces, `sealed` classes by default, records for DTOs, `async` all the way with `CancellationToken` passed through, no `.Result`/`.Wait()`.
- Never return EF entities from endpoints; always project into DTOs.
- Public endpoints return Published rows only; admin endpoints opt out explicitly.
- Every migration gets a descriptive name; never edit a migration that has been applied.
- Every new endpoint gets at least one integration test. Importers are tested through their mappers with fixtures; no live network in tests.
- Frontend: Server Components by default; `"use client"` only where interactivity is needed. No `any`. API types come only from the generated schema. Never read cookies or headers in public pages (keeps them prerendered/cached).
- Measurements are metric in the DB and the API; conversion happens only in `units.ts`.
- Never copy copyrighted text. Imported prose comes only from Wikipedia (CC BY-SA, attributed); other sources are cited, not copied.
- Commit after each completed checklist group with a conventional-commit message (`feat:`, `fix:`, `chore:`).

## 8. Content scope (for guidelines and moderation)

The site is an **encyclopedia**: history, designers, specifications, service use, and conceptual explanations of how mechanisms work. The following are out of scope and must be rejected (in admin review now, in moderation later): manufacturing instructions, machining drawings/dimensions for building parts, modification or conversion guides (e.g. to automatic fire), and instructions for circumventing legal restrictions. This also applies to imported Wikipedia text: strip any such passages during review.

## 9. Local development commands (target state)

```bash
docker compose up -d                                    # Postgres
cd backend && dotnet run --project src/ArmoryWiki.Api   # http://localhost:5080, Scalar at /scalar
cd backend && dotnet test
cd backend && dotnet ef migrations add <Name> -p src/ArmoryWiki.Infrastructure -s src/ArmoryWiki.Api
cd backend && dotnet run --project src/ArmoryWiki.Api -- import-wikidata --dry-run
cd frontend && npm run dev                              # http://localhost:3000 (proxies /api to the backend)
cd frontend && npm run gen:api                          # regenerate API types (API must be running)
```
