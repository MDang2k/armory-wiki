# ArmoryWiki — Implementation Plan

> A structured, encyclopedic database of firearms: design history, designers, specifications, cartridges and operating mechanisms, with filtered search, family trees and timelines.
>
> **Vocabulary:** domain terms (firearm vs variant, design date, country of origin, designer roles, relation types, spec definitions) are defined in `GLOSSARY.md`. Use them exactly.
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
| Images | SkiaSharp (decode, orient, re-encode, WebP sizes; ADR-0006); files on a Docker volume served by Caddy. **No MinIO** |
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
│   │   ├── ArmoryWiki.Infrastructure/ # DbContext, EF configurations, migrations, seeding, file store
│   │   └── ArmoryWiki.Api/            # endpoints, DTOs, validators, Program.cs, importers (CLI subcommands)
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
- `FeedSystem?` (enum: DetachableBoxMagazine, InternalMagazine, Belt, Drum, Tubular, Cylinder, StripperClip, EnBlocClip, SingleShot, Other)
- `Summary` (short plain text, ≤ 500 chars), `DevelopmentHistory` (Markdown), `ServiceHistory` (Markdown)
- `WikipediaTitle?`, `WikipediaRevisionId?` (long?): set when prose was adapted from Wikipedia; drives the attribution footer
- Navigation: `Variants`, `Designers` (FirearmDesigner), `Manufacturers` (FirearmManufacturer), `RelationsFrom` / `RelationsTo` (FirearmRelation), `Adoptions`, `Media`, `Videos` (VideoReference)
- **Publish gate:** Draft → Published fails validation unless `CountryOfOriginId`, `DesignDate`, `OperatingMechanismId` are set and the base variant has a primary cartridge. There is no separate "action type": the mechanism tree plus `Variant.FiringModes` cover it.

**Variant** (holds the actual specifications)
- `Id`, `FirearmId`, `Slug` (unique per firearm), `Name`, `IsBaseVariant` (bool; exactly one per firearm)
- `IntroducedDate` (FuzzyDate?)
- Specs, all nullable: `WeightEmptyG`, `OverallLengthMm`, `OverallLengthFoldedMm`, `BarrelLengthMm`, `RateOfFireMinRpm`, `RateOfFireMaxRpm`, `MuzzleVelocityMps`, `EffectiveRangeM`, `MagazineCapacityStandard`
- `FiringModes` (flags enum: SemiAutomatic, Burst, Automatic; `None` for manually operated firearms)
- `ExtraSpecs` (`jsonb`, `Dictionary<string, string>`) for rare/odd specs
- Navigation: `Chamberings` (VariantCartridge)
- Creating a firearm always creates its base variant in the same transaction, so "exactly one" always holds. Enforced by a partial unique index on `(firearm_id) WHERE is_base_variant`.

**Tag** (controlled vocabulary, admin-managed; ADR-0005)
- `Id`, `Slug`, `Name`, `Group` (enum: Type, Role, Era)
- Examples: Type: Handgun, Revolver, SubmachineGun, Rifle, Carbine, AssaultRifle, BattleRifle, Shotgun, MachineGun; Role: DesignatedMarksman, Sniper, PersonalDefense; Era: WW1, WW2, ColdWar, PostColdWar (an era tag means significant service in that period)
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
| `FirearmDesigner` | `Id`, `FirearmId`, `PersonId?`, `OrganizationId?`, `Role` (enum: LeadDesigner, CoDesigner, Developer) | Check: exactly one of PersonId / OrganizationId. Unique `NULLS NOT DISTINCT` (FirearmId, PersonId, OrganizationId, Role) |
| `FirearmManufacturer` | `Id`, `FirearmId`, `OrganizationId`, `From` (FuzzyDate?), `To` (FuzzyDate?), `IsLicensed` (bool) | Surrogate key: same org can appear for several periods |
| `VariantCartridge` | `VariantId`, `CartridgeId`, `IsPrimary` (bool) | PK (VariantId, CartridgeId); partial unique index `(variant_id) WHERE is_primary` |
| `FirearmRelation` | `FromFirearmId`, `ToFirearmId`, `Type` (enum: DerivedFrom, CopyOf, InfluencedBy, CompetedAgainst, Replaced), `Note` | PK (From, To, Type); check `From <> To` |
| `Adoption` | `Id`, `FirearmId`, `CountryId`, `From` (FuzzyDate?), `To` (FuzzyDate?), `Note` | Surrogate key: a country can re-adopt |

`FirearmRelation` direction: "AKM **DerivedFrom** AK-47" is stored as From=AKM, To=AK-47. "M16 **Replaced** M14" is stored as From=M16, To=M14.

Designers can be people or organizations (Wikidata's "designed by" often points at a design bureau). Pages render them together, e.g. "M. Kalashnikov; Izhmash design bureau".

### 3.5 Sources, citations, images and videos (Phase 3)

**Source**: `Id`, `Type` (enum: Book, Article, Website, Patent, Manual, Video, Dataset), `Title`, `Author?`, `Publisher?`, `Year?`, `Url?`, `Isbn?`

**Citation**: `Id`, `SourceId`, exactly one of `FirearmId?` / `VariantId?` / `PersonId?` / `OrganizationId?` / `CartridgeId?` (check constraint, cascading deletes), `FieldName?` (an API field name), `Locator?` (free text: "p. 112", "claim 3", "12:40", a deep URL). Every imported entity gets one citation to the Wikidata source (Phase 4).

**Image** (GLOSSARY.md: *image*): `Id`, exactly one of `FirearmId?` / `PersonId?` / `OrganizationId?` / `CartridgeId?` (check constraint), `SortOrder` (the first image is the thumbnail), `ContentHash` (SHA-256 of the upload; storage key), `OriginalExtension`, `Caption?`, `Author` (required), `License` (required, fixed list: `CC0-1.0`, `PD`, `CC-BY-2.0|2.5|3.0|4.0`, `CC-BY-SA-2.0|2.5|3.0|4.0`), `SourceUrl` (required), `Width`, `Height`, `CommonsFileName?` (unique when set). Files: a cleaned "original" (orientation applied, then re-encoded in its own format, so no EXIF/GPS ever reaches disk; PNG stays lossless) plus WebP `thumb` (320 px) and `medium` (1280 px), one folder per content hash, shared by images with identical uploads.

**Video** (GLOSSARY.md: *video*): `Id`, `FirearmId`, `YouTubeVideoId` (unique per firearm), `Title`, `Channel?`, `PublishedAt?`, `Status` (Suggested, Approved, Rejected). Only Approved videos are public. Videos an admin adds by hand start Approved (title typed in; no live YouTube lookup). Embedded, never re-hosted.

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
- `GET /api/firearms/{slug}`: full detail: all fields, variants with specs and chamberings, designers with roles, manufacturers, adoptions, direct relations (both directions), approved videos, citations, Wikipedia attribution fields. Unknown slug → check `SlugRedirect` → **301** with `Location: /api/firearms/<current-slug>` (the frontend fetches with `redirect: "manual"` and turns it into `permanentRedirect()`), else 404. Approved videos and citations join the DTO in Phase 3, when those entities exist.
- `GET /api/firearms/{slug}/family-tree?depth=3`: nodes + edges of `DerivedFrom` relations up and down from this firearm (for React Flow). Traversal keeps a visited set: imported relations may contain cycles.
- `GET /api/firearms/compare?slugs=a,b,c`: up to 4 base variants with specs side by side, in the requested order. 400 for none or more than 4; 404 if any slug is unknown or draft (renamed slugs are not followed).
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
| `/` | Search box, number of firearms, browse by type tag / era / country (counts from facets) |
| `/firearms` | Filter sidebar (multi-select checkboxes with disjunctive facet counts) + results grid. **All filter state lives in URL query params.** |
| `/firearms/[slug]` | Infobox (specs table per variant, variant switcher), designers, cartridge links, mechanism link, development history, service history, adoptions, related firearms, image gallery with attribution, approved videos (embedded), citations as footnotes, Wikipedia attribution footer when applicable |
| `/firearms/[slug]/family-tree` | React Flow graph |
| `/compare?slugs=…` | Side-by-side spec table; a search adds firearms (up to 4), each column can be removed; reached from a "Compare" link on firearm pages |
| `/people`, `/organizations`, `/cartridges` | Alphabetical, paged lists (no search yet) |
| `/people/[slug]` | Biography + design timeline |
| `/organizations/[slug]` | Description + firearms designed / manufactured |
| `/cartridges/[slug]` | Specs, parent/child cartridges, firearms chambered in it |
| `/mechanisms`, `/mechanisms/[slug]` | Tree + explanation + firearms using it |
| `/timeline` | Horizontal timeline with filters |
| `/admin/...` | Phase 7 |

- Detail pages: Next 16 **Cache Components** (`cacheComponents: true` in `next.config.ts`). Data loaders are `"use cache"` functions with `cacheLife('hours')` and `cacheTag(...)`, plus `generateMetadata` for title/description/OpenGraph. **No `generateStaticParams`:** pages render per request from cached data, awaiting `params` inside `<Suspense>` so the shell prerenders, and `next build` never needs the API. `/firearms` renders its results on the server from `searchParams` inside `<Suspense>`, through a loader keyed by the normalized filters (cached for minutes); client components only rewrite the URL.
- **Cache tags:** every loader tags the entity it shows (`firearm:<slug>`, `person:<slug>`, …), each entity type it embeds (`firearms`, `people`, `organizations`, `cartridges`, `mechanisms`, `reference`) and `all`. Route segment configs like `export const revalidate` are not allowed with Cache Components; read `node_modules/next/dist/docs/01-app/02-guides/migrating-to-cache-components.md` before Phase 5.
- **On-demand revalidation:** a secret-protected route `POST /api/revalidate` (Next route handler; the API calls it inside the Docker network at `http://web:3000/api/revalidate`, and Caddy does not route it publicly) takes a list of tags and expires them at once (`revalidateTag(tag, { expire: 0 })`: the next visit sees the edit, never stale content). The API calls it after every admin write commits (the entity's tag and its type tag, plus the old slug's tag on a rename) and once at the end of each import run (`all`). URL and secret come from configuration; unset means no call; a failed call is logged and never fails the write.
- **Units:** every measurement shows both systems, metric first: "870 mm (34.3 in)", "4.3 kg (9.5 lb)". No toggle, so no client state and no flash after hydration.
- **Images:** plain `<img>` with the stored WebP sizes (thumb 320, medium 1280), width/height set, lazy-loaded; no `next/image` optimizer.
- **Videos:** click to load: a title card with a play button; the `youtube-nocookie.com` iframe is created only on click, so nothing reaches YouTube before that.
- **Prose links** stay external links to en.wikipedia.org; mapping them to our own pages is later work.
- **Citations:** a citation tied to a field puts a numbered superscript next to that field; every citation is listed under "Sources".
- **Look:** dense and encyclopedic, close to Wikipedia: system fonts, light and dark following the OS, Tailwind only, no component library.
- **Redirects:** API detail endpoints answer a renamed slug with **301** + `Location`; fetch with `redirect: "manual"` and call `permanentRedirect()` to the new slug.
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
- [ ] **Reference data** (`ReferenceDataSeeder`: mechanism tree, tags, Wikidata class → tag map) inserted when missing after every migration, in every environment
- [ ] **Sample seeder** (runs in Development on startup if DB is empty) using hand-written seed data under `Infrastructure/Seed/`. Every seeded firearm, person, organization, cartridge and country carries its **real Wikidata QID**. Look each QID up; never guess one. All seeded rows are `Published`.
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
- [ ] Full-text search via the `tsvector` column (`WebSearchToTsQuery` with `simple`, ranked by `ts_rank`), falling back to trigram **`word_similarity`** (`word_similarity(q, search_names) >= 0.5`; a function call, so no index use, fine at this scale; switch to the `<%` operator if it gets slow) on name/aliases when FTS finds nothing. Plain `similarity()` is too low on the concatenated column ("ak47" vs AK-47 scores 0.10; `word_similarity` scores 0.60). Credits whose Person/Organization is Draft come back with a null party under the filters, so skip them in projections
- [ ] Family tree: load relations iteratively up to `depth` (max 5) with a visited set; return `{ nodes: [...], edges: [...] }`
- [ ] Pagination + `pageSize` clamp; `SlugRedirect` lookup then 404 ProblemDetails for unknown slugs
- [ ] OpenAPI document + Scalar UI in Development
- [ ] Cache headers per §4
- [ ] Integration tests for: each filter alone, multi-value filters, filter combinations, disjunctive facets, full-text search, trigram fallback ("ak47" → AK-47), mechanism descendants, design-year overlap (incl. `YearTo` ranges), pagination edges, family tree for AR-10 → AR-15 → M16 → M4, slug redirect, 404s, drafts never returned

**Done when:** `dotnet test` is green and every endpoint is browsable in Scalar.

### Phase 3: Admin API, sources and media
Built in four slices (3a–3d), each tested, reviewed and committed on its own. Terms follow GLOSSARY.md: *admin*, *publish requirements*, *unpublish*, *locked field*.

Rules shared by every slice:
- Admin endpoints live under `/api/admin`, behind one `Admin` policy on the group. Admin DTOs carry `status`, `lockedFields`, `version` and `missingForPublish`, and admin reads bypass the Published filter. Public routes never change behaviour based on who is asking.
- Writes are `PUT` with the full representation; there is no `PATCH`. The server diffs old and new state, and every changed field is added to `LockedFields`.
- Lock keys mirror API field names: core fields as `designDate`, variant fields as `variants/{variantId}/weightEmptyG` (id, not slug), whole lists as `tags`, `designers`, `manufacturers`, `relations`, `adoptions`, `chamberings/{variantId}`, plus `baseVariant`. `DELETE /api/admin/{entity}/{id}/locks/{key}` unlocks without changing the value.
- Optimistic concurrency on Postgres `xmin`: every admin DTO of an editable entity has `version`, every `PUT` sends it, and a mismatch answers 409 ProblemDetails carrying the current version. Citations, images and videos are add/remove entries attached to a subject: they take no version and don't bump the subject's.
- Write handlers load with `AsTracking()` (the API context is no-tracking by default).
- FluentValidation on every write DTO: slug format `^[a-z0-9]+(-[a-z0-9]+)*$`; firearm slugs `facets` and `compare` are reserved because they are API routes; positive measurements; `FuzzyDate` consistency (Month 1–12, Day valid for the month, `YearTo >= Year`, a Decade starts at a year ending in 0).
- Slugs are generated from the name when not supplied: lowercase, `×` → `x`, accents removed, Cyrillic transliterated, every other run of non-alphanumerics → `-`, trimmed; collisions get `-2`, `-3`, … A slug change writes a `SlugRedirect` for entities with a public page (firearms, people, organizations, cartridges, mechanisms); variants, countries and tags have none, so their renames write none. A live slug always beats a redirect: taking a slug deletes any redirect with that old slug, including renaming back to it.
- Deleting: firearms and their child rows only while Draft; people, organizations, cartridges, countries, mechanisms and tags only when nothing references them (409 listing the references). Deleting also removes the entity's slug redirects and image files.
- Creating something locks every field the admin supplied (an admin chose those values); later imports fill only what was left empty.
- `POST .../publish` enforces the publish requirements from GLOSSARY.md (400 listing what is missing); `POST .../unpublish` returns content to Draft.

**3a: auth and firearms**
- [x] ASP.NET Core Identity inside `ArmoryWikiDbContext` (same migrations, snake_case tables). Own endpoints only, no `MapIdentityApi` (it exposes public registration): `POST /api/admin/auth/login`, `POST /api/admin/auth/logout`, `GET /api/admin/auth/me`, `GET /api/admin/auth/antiforgery`. Lockout after 5 failed logins; login rate-limited per IP. Cookie `HttpOnly`, `Secure`, `SameSite=Strict`.
- [x] Antiforgery token header required on every unsafe `/api/admin` request, checked by one group filter. Login is the one exception: there is no session yet, and the cookie is SameSite=Strict.
- [x] One `Admin` role. The admin user is created from configuration (`Admin:Email`, `Admin:Password`) in every environment when missing: user secrets in Development, `.env` in production.
- [x] Firearm admin endpoints: list (`status`, `q`, sort by `updatedAt` newest first, `missingForPublish` per item), detail, create (creates the base variant in the same transaction), `PUT` core fields, variant CRUD at `/variants/{variantId}`, `POST /variants/{variantId}/make-base`, replace-the-list `PUT`s for tags, designers, manufacturers, relations, adoptions and chamberings, publish, unpublish, delete, unlock.
- [x] A *derived from* or *copy of* link that would create a cycle is rejected (400 naming the loop).
- [x] The base variant cannot be deleted; promote another variant first.
- [x] Tests: 401 when not logged in; 400 without the antiforgery header; validation 400s; 409 on a stale version; locks appended, and unlock; slug redirect written, and the live-slug-wins rules; cycle rejection; make-base; publish requirements; delete rules; happy path from create to publish to visible on the public API.

**3b: other entities and reference data**
- [x] Admin endpoints (list, detail, create, `PUT`, delete; publish/unpublish and unlock for entities with a status and locks: people, organizations, cartridges) for people, organizations, cartridges, countries, tags and mechanisms. One generic engine with a spec per resource (`Features/Admin/Resources`). A cartridge's parent links never loop.
- [x] Moving a mechanism to another parent, or changing its slug, rewrites the `Path` of it and all its descendants in one transaction; a mechanism cannot move beneath itself; only an unused leaf can be deleted.
- [x] `reference_seed_log` table: `ReferenceDataSeeder` inserts only keys it has never seeded before, so admin deletions and renames stick.
- [x] Tests for each.

**3c: sources and citations**
- [x] Source and Citation entities (§3.5) and migration; admin CRUD for sources; citations listed and added per subject at `/api/admin/{firearms|variants|people|organizations|cartridges}/{id}/citations`, removed with `DELETE /api/admin/citations/{id}`. Citations belong to their subject: they never block deleting it and are deleted with it. A cited source cannot be deleted (409 naming what cites it). `fieldName` is checked for shape, not against the real field list.
- [x] Public firearm, person, organization and cartridge detail include their citations (a firearm's include its variants', marked with the variant), in the order added.
- [x] Tests.

**3d: images and videos**
- [x] Image and Video entities and migration (named after the glossary terms).
- [x] A single `FileStore` class writing to a configured directory (Docker volume). Upload: `POST /api/admin/{firearms|people|organizations|cartridges}/{id}/images` as multipart (file plus caption, author, license, sourceUrl). JPEG, PNG or WebP up to 20 MB and 40 MP, verified by decoding; EXIF orientation applied, metadata dropped by re-encoding; original plus WebP thumb (320 px) and medium (1280 px), named by content hash. Upload, delete and cleanup of one hash are serialized with an advisory lock.
- [x] `GET`/`PUT .../{owner}/{id}/images/order` per owner; `PUT /api/admin/images/{imageId}` edits details and `DELETE /api/admin/images/{imageId}` removes the files unless another image shares the content hash (flat routes, like citations). Deleting an owner removes its image files too.
- [x] The API serves `/media/...` in Development only; Caddy serves it in production.
- [x] Public detail DTOs include images (with author, license and source) and approved videos; the list's `thumbnailUrl` is the first image's thumb.
- [x] Videos: an admin adds one by YouTube link (Approved); approve, reject and delete endpoints; per-firearm list.
- [x] Tests, including an acceptance test that walks the Phase 3 "Done when" through HTTP.

**Done when:** an admin can, through the API, create a firearm with variants, designers, chamberings, relations, an uploaded image and a citation, publish it, and see it on the public API; all tests are green.

### Phase 4: Importers
Wikidata is the main content source (ADR-0004). All importers are CLI subcommands of the API project (`dotnet run --project src/ArmoryWiki.Api -- <command> [--dry-run]`), run by hand (on the VPS as one-shot `docker compose run` containers). No scheduler.

Built in four slices (4a–4d), each tested, reviewed and committed on its own. Terms follow GLOSSARY.md: *import*, *draft*, *locked field*, *empty weight*.

Rules shared by every importer:
- **What an import may write** (GLOSSARY.md *import*): new entities land as Draft. On a **draft**, an import refreshes every unlocked field and adds missing list entries (never removes one). On **published** content it only fills fields and lists that are empty. It never changes `Status`, and never deletes or unpublishes anything. Locked fields are never written; lists are locked as a whole under their 3a lock keys (`tags`, `designers`, `manufacturers`, `relations`, `chamberings/{variantId}`, …).
- Idempotent upserts keyed on QID / Commons file name / (firearm, YouTube video ID).
- Each importer is split into **fetch** (HTTP → raw JSON/HTML) and **map** (pure functions raw → entities). Mappers are tested with saved fixture files (plain API JSON); no live-network tests.
- CLI subcommands dispatched from the API's `Program.cs` before the web host starts. Each command takes an advisory lock, so a second run of the same importer exits at once.
- Items are saved one at a time; a row-version conflict with an admin edit skips that item and logs it (the next run picks it up).
- `--dry-run` does the real run inside one transaction and rolls it back, so its counts (created, updated, unchanged, skipped) are exact. It still fetches.
- **Run report:** a console summary plus the full report as JSON in `import-reports/<command>-<utc timestamp>.json` (gitignored): skipped values and why, merged or vanished QIDs, missing labels, rejected licenses, conflicts. No database table until the Phase 7 admin UI needs one.
- Wikimedia requests send `User-Agent: ArmoryWiki-importer/1.0 (<project URL>)` from configuration (`Import:Contact`, default the GitHub repository URL), never a personal address. Requests are sequential, with `maxlag` and backoff on 429/503.
- After each batch, call the frontend revalidate hook. Phase 5 adds it together with the hook itself (4a ships no empty stub).

**4a: shared plumbing and `import-wikidata`**
- [x] Plumbing (`Api/Import/`): command runner, HTTP client (User-Agent, `maxlag`, backoff, and re-asking for entities dropped from answers over the API's 12 MB size limit), advisory lock, run report, dry run.
- [x] **Discovery:** SPARQL returns QIDs only. An item qualifies when its "instance of" **or** "subclass of" chain reaches a type class in `WikidataClassTagMap` (an include list: assault rifle, battle rifle, carbine, rifle, pistol, revolver, submachine gun, machine gun, shotgun, sniper rifle; add "rifle" and any missing small-arms class the dry run shows). QIDs in the map itself are excluded, as are individual specimens (instances of a weapon or firearm model), weapon and firearm *families*, and artillery models; an item needs at least one of manufacturer, ammunition or designer. The first live run finds about 2,260 items. About ten cannons still slip in through "machine gun" (AK-630, M61 Vulcan); excluding everything that reaches "artillery" would also drop the M134 Minigun, so admins delete those drafts in review.
- [x] **Details:** `wbgetentities` in batches of 50 (claims, labels, aliases, sitelinks, redirects). A merged item (redirect) has its stored QID moved to the target, unless another entity already holds it (logged). Deleted or no-longer-matching QIDs are logged, never touched. Items without an English label (or the language-neutral `mul` label many items now use instead) are skipped and logged. Claims that point at a merged item are followed to it. Stored firearms are checked for merges; a stored person, organization or cartridge whose item was merged would come back as a new draft.
- [x] **Firearm mapping:** each item becomes one Firearm with an auto-created base variant. English label → name and slug; aliases → alternative names; inception → design date (Wikidata precision → `FuzzyDate`; Julian-calendar days converted to Gregorian); English sitelink → `WikipediaTitle`; the item's own "instance of"/"subclass of" values → type tags via the map ("rifle" and "semi-automatic rifle" added to it); "based on" → *derived from* (the 3a loop check applies; a looping link is logged and skipped).
- [x] **Designers:** "designed by" → a Person when the target is a human, otherwise an Organization. One designer is the **lead designer**; several are all **co-designers** (an admin picks the lead).
- [x] **Country of origin:** used only when Wikidata has exactly one value; several are logged and left empty. Unknown countries are auto-created from their QID, `IsHistorical = true` when Wikidata has a dissolution date; countries are never updated (they have no locks).
- [x] **Ammunition:** every value becomes a chambering on the base variant; **primary** only when there is exactly one.
- [x] **Specs** on the base variant, normalized to metric when the unit QID is known (skipped and logged otherwise): length, barrel length, muzzle velocity, rate of fire (one value → min = max), effective range. Mass goes into `ExtraSpecs` as `weight (unspecified)` with its unit; no firearm item on Wikidata states whether its mass is empty, so `WeightEmptyG` is never imported. Several values for one spec are logged and skipped. A value removed on Wikidata stays here.
- [x] Operating mechanism and production dates are **not** imported (admins set the mechanism in review). Firearm items on Wikidata have no mechanism property (none among the 25 most used), so there is nothing to map yet.
- [x] **Related entities, one level deep**, created as drafts: people (label, birth, death, nationality), organizations (label, founding date, country), cartridges (label, aliases, design date, country, parent cartridge when that one was imported too), countries. Their own references are not followed further.
- [x] **Citations:** the reference seeder adds one Source "Wikidata" (Dataset, `https://www.wikidata.org`). Every imported entity gets one citation to it with the item URL as locator, added only when it has none yet.
- [x] Tests: mapper fixtures for AK-47, M1 Garand and a firearm with a historical country; unit and qualifier handling; Testcontainers tests proving a re-import is idempotent, respects `LockedFields`, only fills empty fields on published content, matches seeded firearms by QID without duplicating them, that a dry run writes nothing, and that a second concurrent run is refused. Import tests each get their own freshly seeded database.

**4b: `import-commons`**
- [x] For firearms with a Wikidata image (`P18`; every value, usually one): fetch file metadata from the Commons API (POST, batches of 50); allow only CC0, Public Domain, CC BY, CC BY-SA (versions 2.0–4.0, the ones in `ImageLicenses`), skip and log others. Country-ported licenses ("CC BY-SA 2.0 fr") are different licenses and are refused too; the first live dry run refused about 65 of them out of ~1,620 files. A file whose license requires attribution but names no author is skipped; PD and CC0 files without one get "Unknown author".
- [x] Download Commons' 2560 px-wide rendition (never the original, never hotlinked) and run it through `ImageProcessor`/`FileStore` under the hash advisory lock. Store author (HTML stripped), license, file-page URL as `SourceUrl`, `CommonsFileName`.
- [x] Idempotent on `CommonsFileName` (unique: a file already illustrating another subject is skipped). Only firearms with **no** image get any, drafts included, so an image an admin removed or replaced is not brought back over theirs (an admin who deletes a draft's only image gets it back on the next run). More images are added by hand. A dry run reads the metadata only: it neither downloads nor writes files.
- [x] Tests: license allow/deny mapping and attribution stripping from fixtures; re-run adds nothing; refused licenses, missing files and undecodable files are skipped and leave no files behind; a dry run writes nothing. Live: 18 images for the 15 seeded firearms, a second run unchanged; a dry run after the full Wikidata import would add 1,474 images.

**4c: `import-wikipedia`**
- [x] For firearms with a `WikipediaTitle`: look up the latest revisions in batches of 50 (action API, redirects followed) and download only articles whose revision differs from `WikipediaRevisionId` and where the import may still write something; fetch the Parsoid HTML via the core REST API (`/w/rest.php/v1/page/{title}/with_html`) and convert it with our own small converter: a tokenizer plus paragraphs, headings, lists, links (to en.wikipedia.org), bold and italic, Markdown-escaped. Tables, infoboxes, references, images, hatnotes and other boxes are dropped, except multi-column list boxes (`div-col`), which hold the users lists. Headings with nothing under them are dropped.
- [x] The lead's first paragraph → `Summary` as plain text, cut at a sentence to its 500-character column; sections whose heading mentions History / Development / Design / Background / Origins → `DevelopmentHistory`, and Service / Operational / Combat / Usage / Users / Deployment (checked first, so "Service history" is service) → `ServiceHistory`, each under its own `##` heading; "See also", Variants, Civilian use and the rest dropped. `WikipediaRevisionId` records the revision read, for CC BY-SA attribution; an admin who locks it keeps the firearm out of the import.
- [x] Draft/published and lock rules as above. Firearms only for now.
- [x] Tests: converter and section mapping from saved article HTML (M1 Garand, trimmed); locks, a pinned revision, missing articles; a second run downloads nothing. Live: 40 articles in 33 s, a second run unchanged in 3 s.

**4d: `import-fw-videos`**
- [x] List the Forgotten Weapons channel uploads via the YouTube Data API (`channels?forHandle=@ForgottenWeapons` → uploads playlist → `playlistItems`, 50 per page, about 60 quota units of 10,000 a day; `YouTube:ApiKey` from configuration, never logged; the command stops with a clear message when it is missing; 429/500/503 are retried). Private and deleted uploads are skipped; the publish date is the video's, in UTC.
- [x] Match each title against firearm names and alternative names (drafts included; only names that identify one firearm: at least three letters or digits, not a tag name such as "Carbine", and held by no other firearm) with `word_similarity` ≥ 0.6, keeping only matches within 0.25 of the best one (so an "AR-10" title does not also suggest the AR-15), at most 3 firearms per video; write `Video` rows as `Suggested`, storing title, channel and published date only (no descriptions, no copied text, no scraping of forgottenweapons.com).
- [x] Idempotent on (firearm, YouTube video ID) including **rejected** rows, so a rejected suggestion never returns.
- [x] Tests: matching against fixture uploads (two pages, a private video, an unmatched title, a near-miss name); a rejected video is not re-suggested; a dry run writes nothing; no key stops the command. Not run live yet: it needs a YouTube Data API key.

**Done when** (checked after 4a; met: a live run on a fresh database created 2,215 firearms and matched 14 of the 15 seeded ones by QID, and a second run reported everything unchanged): a dry run reports sensible counts; a real run against a fresh DB imports firearms without duplicating seeded ones; a second run changes nothing; tests are green. 4b–4d each end with their tests green and a dry run reporting sensible counts.

### Phase 5: Frontend, read-only
Built in four slices (5a–5d), each tested, reviewed and committed on its own. §5 holds the rendering, caching and display decisions.

**5a: foundation**
- [x] Generate TS types from `/openapi/v1.json` into `src/lib/api/schema.d.ts` (`npm run gen:api`); typed fetch wrapper with base URL from `API_BASE_URL`, `redirect: "manual"` for slug redirects (`permanentRedirect()`); loaders return `Loaded<T>` (ok / moved / missing) because they cannot redirect from inside the cache, and pages `settle()` it. The API now types integers strictly (no "number | string" in the schema)
- [x] `rewrites` for `/api/*` and `/media/*` in development (§5)
- [x] Layout: header with search (goes to `/firearms?q=…`; firearms only), nav (Firearms, Cartridges, Mechanisms, People, Organizations; Timeline waits for Phase 8), footer with the content license; loading, error and not-found states
- [x] `src/lib/units.ts` (mm⇄in, g⇄lb/oz, m/s⇄ft/s, m⇄yd) and fuzzy-date formatting ("1947", "c. 1947", "March 1942", "23 January 1855", "1950s", "1942–1944"), with Vitest
- [x] `/api/revalidate` route handler (secret from env, compared in constant time; 401/400 otherwise) and the API side: an EF interceptor records the tags of each successful save, the admin group sends them after the request (a failed write sends nothing), and import runs send `all` (§5)

**5b: firearms list and home**
- [x] `/firearms`: multi-select filter sidebar driven by `/facets` (type, role and era tags, country, mechanism; design decade is single-choice because the API takes one year range), URL-synced state, debounced text search, sort, pagination, removable filter chips (designer, manufacturer and cartridge filters arrive by link from other pages); filter ⇄ query-string helpers with Vitest. Options are plain links, so filtering works without JavaScript; only the search box is a client component. Small screens show results first, then the filters
- [x] `/`: search box, firearm count, browse by type tag, era, role and country. Data parts render per request (`connection()` inside Suspense) from cached loaders, so `next build` never calls the API. Checked end to end: an admin rename through the API shows on the home page at the next request

**5c: firearm detail**
- [x] Infobox and specs per variant with a variant switcher; designers, cartridges, mechanism; development and service history via `react-markdown` (no raw HTML); adoptions; related firearms; image gallery with author and license under each image; approved videos (click to load); citations as footnotes; Wikipedia attribution linking the exact revision (helper with Vitest); "Compare" link. Footnote numbering is a tested helper too. **Status codes:** with Cache Components the shell streams first, so an unknown or draft slug answers 200 with the not-found page and `noindex`, and a renamed slug answers 200 with an instant meta refresh to the new one (Next 16 documents this; Google treats it as a permanent redirect). True 404/308 would need a per-request slug lookup in Proxy, not worth it now. The importer now also encodes "(" in links (Wikipedia titles like "Blowback (arms)" broke the Markdown)

**5d: everything else**
- [x] `/people`, `/organizations`, `/cartridges` (lists, 50 a page) and their detail pages (each with "filter firearms by this …" links into `/firearms`); `/mechanisms` (tree) and `/mechanisms/[slug]` (breadcrumb, kinds, firearms); `/compare` (an add-firearm search calling the public list API from the browser, remove links, `noindex`)
- [x] `generateMetadata` on every detail page; `sitemap.ts` (Published only, per request from cached data) and `robots.ts`, absolute URLs from `SITE_URL`
- [x] Responsive down to 375px width (checked page by page: no sideways scrolling)

Tests: Vitest for `lib/` logic only (units, dates, filter query strings, attribution link); no component or browser tests. The "Done when" is checked by clicking through the running site.

**Done when** (met: a crawler starting at `/` reached all 59 published seeded entities with no error or not-found links, and the sitemap lists them; the admin-edit check passed in 5b): every seeded entity is reachable by clicking from the home page, filters round-trip via URL, an admin edit shows on the page after revalidation without waiting for the hour, `npm run build` and `npm run lint` pass, and Vitest is green.

### Phase 6: Deploy
Built in three slices (6a–6c), each tested, reviewed and committed on its own. Everything is built and proven locally first (Caddy with its own local certificate on `localhost`); going live is the last step, once the VPS and domain exist (a Hetzner CX22, Ubuntu 24.04, is plenty).

**6a: images and the production stack**
- [x] Dockerfiles, multi-stage and non-root: the API on the ASP.NET runtime image (Debian, so SkiaSharp's Linux assets work), listening on 8080; the frontend as Next `output: "standalone"` on `node:22-slim` with `API_BASE_URL=http://api:8080`. The API publishes for `linux-x64` only (386 MB instead of 1 GB: otherwise every platform's SkiaSharp library ships); both images carry a health check
- [x] A `migrate` CLI subcommand of the API (EF migrations, then `ReferenceDataSeeder`), run as a one-shot `docker compose run --rm api migrate` before `up`; **never** migrate on API startup in production. Imports run the same way (`docker compose run --rm api import-wikidata`), with reports in a mounted `import-reports/` folder
- [x] `deploy/docker-compose.prod.yml`: `caddy`, `web`, `api`, `postgres` (no published ports), media and Postgres volumes, health checks, a fixed network subnet
- [x] `deploy/Caddyfile`: automatic TLS; `/api/*` → api (so `/api/revalidate` is not public: the API calls `http://web:3000/api/revalidate` inside the network); `/media/*` → the media volume with long, immutable cache headers (files are named by content hash); everything else → web. Only Caddy publishes ports (80, 443)
- [x] `UseForwardedHeaders` trusting only the compose subnet (`KnownNetworks`), so the per-IP login rate limit and lockout see real client addresses (otherwise every visitor shares one bucket and anyone can keep the admin locked out)
- [x] `/healthz` on the API for container health checks (database reachable). Data-protection keys persist on a volume, so a redeploy does not sign the admin out
- [x] Secrets via a `.env` on the VPS (never committed; `deploy/.env.example`): admin credentials, DB password, revalidate secret (same value in the API and the frontend), YouTube API key, `SITE_URL` (the frontend refuses to start without it). The import contact is the public project URL, not a secret
- [x] Checked locally: the whole stack up through Caddy on `https://localhost` (a dump of the dev DB loaded into it), migrate, browse, HTTP redirecting to HTTPS, `/api/revalidate` and `/healthz` not public, an admin login and edit through Caddy revalidating the page (so forwarded headers work), an import run whose images Caddy serves from the shared volume with immutable cache headers

**6b: CI/CD**
- [x] GitHub Actions (`.github/workflows/ci.yml`, actions pinned to commit SHAs (release tag in a comment), checked with actionlint; superseded pull-request runs are cancelled; images and deploys are skipped when only documentation changed): build and test (backend with Docker for Testcontainers; frontend lint, Vitest, build) on pull requests and `main`
- [x] On `main`: build both images and push them to GHCR as **public** packages (the server pulls without credentials; CI pushes with `GITHUB_TOKEN`), then SSH to the VPS: copy `docker-compose.prod.yml` and `Caddyfile` to `/opt/armorywiki` (the repo is not cloned there), make sure `import-reports/` exists owned by uid 1654 (the API's user), `pull`, `run --rm api migrate`, `up -d`. SSH key, host and user are Actions secrets. A few seconds of downtime per deploy is accepted; no staging environment. Deploys pin `IMAGE_TAG` to the commit SHA (`latest` is pushed too, for manual commands), never overlap, and the host key comes from a `DEPLOY_KNOWN_HOSTS` secret (no trust on first use). Until the `DEPLOY_*` secrets exist the deploy job only reports that it skipped. First run on GitHub: all jobs green in about 5 minutes (backend tests 95 s, images about 160 s each); the SSH deploy itself runs once the VPS exists. The images carry the repository as their source label, so GHCR made them public along with the repository (checked: an anonymous pull of both manifests answers 200)

**6c: backups**
- [x] Nightly `restic` (encrypted, deduplicated) from a systemd timer on the VPS: a fresh `pg_dump` plus the media volume to Backblaze B2; retention 7 daily, 4 weekly, 6 monthly, a structural check on Sundays (`deploy/backup.sh`, restic from its pinned Docker image, so the server needs nothing but Docker; `deploy/systemd/` timer). CI copies the scripts with each deploy. The `keys` volume (data-protection keys, unencrypted at rest) is left out: losing it only signs the admin out, while leaking it would let someone forge admin cookies
- [x] A documented restore script (`deploy/restore-check.sh`) that brings a snapshot back into a scratch database and media folder and compares counts with the live data; running it on the VPS is part of "Done when". Checked locally against a filesystem restic repository: two backups (the second added only the changed dump), a restore matching every table and all 54 media files, and a wrong password failing loudly
- [x] `deploy/README.md`: server setup checklist (`ufw` allowing 22/80/443 only, key-only SSH, no root login, `unattended-upgrades`, Docker), first deploy, imports, backups and restore. No Ansible or Terraform; uptime monitoring (e.g. UptimeRobot's free tier) optional

Next's data cache stays in the frontend container's memory: a deploy or restart starts it cold, which the API absorbs easily at this traffic. No Redis.

**Done when:** the site is live on its domain over HTTPS, a push to `main` deploys automatically, and a backup has been restored into a scratch DB successfully.

### Phase 7: Admin UI
Built in five slices (7a–7e), each tested, reviewed and committed on its own. Decisions that hold for all of them:

- `/admin` lives in the same Next app: thin Server Component shells (`noindex`) around client components that call the API from the browser through `src/lib/admin/client.ts` (relative URLs, antiforgery token on writes). The browser sends the admin cookie itself (`/api` is same-origin: Caddy in production, the Next proxy in dev); public pages stay static and untouched
- TanStack Query for loading and invalidating after writes. react-hook-form + zod: schemas written by hand and typed against the generated input DTOs (drift fails `tsc`); zod checks shape and required fields only, and the API's `ValidationProblem` errors are mapped onto the fields by one shared helper, so publish requirements, slug rules and ranges stay in the API
- A 409 (stale `?version=`) shows "Changed elsewhere since you opened it" with a Reload button; the unsaved input stays on screen until the admin chooses. No merge view
- Login at `/admin/login?returnTo=`; the admin layout checks `/me`, any 401 goes to login and back; logout in the header. The antiforgery token is fetched after login, kept in memory, and refetched once (with one retry of the write) if the API rejects it
- Tailwind and native elements (`<dialog>` confirmations for delete, unpublish and merge; `<details>`), no component library. Metric-only inputs labelled with their unit
- Markdown fields: a textarea with Write/Preview tabs, the preview rendered by the same `react-markdown` setup as the public pages
- `FuzzyDate` input: one text field parsed as you type (`1942`, `1942-03`, `1942-03-15`, `1940s`, `1942–1944` with `-` or `–`, a `c.` prefix), showing its interpretation ("circa 1940s") or an error; the parser lives in `lib/`
- Locked fields show a lock icon with Unlock (`DELETE …/locks/{key}`), so the admin sees why an import no longer updates them
- Related entities are picked with one shared autocomplete over the admin list endpoints (`?q=`, debounced, top 10, Draft/Published badge; drafts allowed: the publish requirements decide). A missing entity is created from a "New …" link that opens its form in a new tab, never inline
- Tests: Vitest for `lib/` (zod schemas, server-error mapping, FuzzyDate parsing), an integration test for every new endpoint; no browser tests. "Done when" is checked by clicking through the running site

**7a: shell, firearm list and details**
- [x] Admin layout, login, logout, session and antiforgery handling; the shared building blocks above. In development the antiforgery cookie follows the request scheme (the API is plain HTTP behind the Next proxy); production keeps it HTTPS-only
- [x] Firearm list doubling as the draft queue: status filter defaulting to Draft, search, columns name, Wikidata link, updated and "missing for publish", a "ready to publish" filter (`?ready=true` on the admin list, a query kept in step with the publish rule by a test); publish and unpublish per row (list rows carry their version), publish disabled while anything is missing. No bulk publish
- [x] Create a firearm; the firearm details form (names, dates, country, mechanism, prose). Checked by clicking through: signed-out redirect and back, a failed login, create with a client-side date error, an unknown mechanism reported on its field, save, unlock, a stale version kept on screen then reloaded, unpublish and publish from the list, delete

**7b: the rest of the firearm editor**
- [x] One page of sections, each with its own Save and unsaved marker (mirroring the API's separate writes), and a warning before leaving with unsaved sections: variants (add, edit, make base, delete) with their chamberings, tags, designers, manufacturers, adoptions, relations. Lists are replaced whole, so their rows sit in one section with Add and Remove; API messages show on the section. Variant fields and lists show their locks (`variants/{id}/{field}`, `chamberings/{id}`, `designers`…), and slash keys unlock as path segments (tested). The leave warning covers closing, reloading and in-app links. Checked by clicking through: variant specs with a field error then saved, a variant field unlocked, ammunition with the primary mark kept when a row above it is removed, a variant added, made base and the old one deleted, tags saved, an unknown designer reported, a relation saved

**7c: other entities and sources**
- [x] List and edit forms for people, organizations, cartridges, countries, tags and mechanisms (publish where they have it). One generic list and editor under `/admin/<resource>`, drawn from a spec per resource whose fields are keyed by every field of the API input (a field added or removed in the API fails `tsc`), round-trip tested per resource. List rows carry their version for per-row publishing
- [x] Sources, and citations on every entity that takes them (firearms, variants, people, organizations, cartridges): pick a source by title or author, optionally narrow to one field, give a page or chapter. A source in use can't be deleted. Checked by clicking through: a source created, a person created (an unknown nationality reported on its field), cited, published and seen on the public page with its source, unpublished and deleted (its citation with it), the source deleted

**7d: images and videos**
- [x] Image upload, one file at a time (drag-drop or picker): type and size checked in the browser against the API's limits, license (from the API's options), author and source URL required, caption optional, a preview; edit details, reorder and delete afterwards. On firearms, people, organizations and cartridges. The license options come from a new `GET /api/admin/images/licenses` (tested), so the UI keeps no copy
- [x] `GET /api/admin/videos?status=Suggested` (paged, with each video's firearm name and slug) and a review screen that approves or rejects across all firearms (and lists approved or rejected ones to change a decision). Checked by clicking through: a GIF refused in the browser, a PNG uploaded with a preview, a non-http source reported on its field, a second image moved first, a caption edited, both deleted; a suggested video rejected and found under Rejected

**7e: merge into variant** ([ADR-0007](docs/adr/0007-variants-carry-wikidata-qids.md))
- [x] `Variant.WikidataId` (optional, unique; a QID is unique across firearms and variants), and the Wikidata importer treats a QID held by a variant as known and reports it as merged (tested); links to it ("based on") lead to the firearm holding the variant
- [x] `POST /api/admin/firearms/{id}/merge-into/{targetId}?version=&targetVersion=` (and a `GET` preview from the same plan), drafts only, in one transaction: the source's base variant specs and chamberings become a new variant of the target carrying the source's QID; images, citations and videos move to the target; designers, manufacturers, adoptions and tags join the target's (duplicates dropped); relations from other firearms are re-pointed to the target (self-relations and duplicates dropped); the source's prose is discarded; a `SlugRedirect` sends the old slug to the target; the source is deleted
- [x] The merge screen: pick the target, preview what moves and what is dropped, confirm. Relations other firearms hold on the source that would duplicate or loop are dropped and listed; the source's other variants move too. Checked by clicking through: a draft with a QID merged into another, landing on the target with the variant marked as merged

**Done when** (met across 7a–7e, each slice clicked through in the browser): an admin can create a complete firearm page with images and citations, and review imported drafts, entirely through the UI.

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
