# ArmoryWiki

Encyclopedic firearms database. Work follows `PLAN.md` phase by phase; big decisions live in `docs/adr/`.

## Commands

```bash
cp .env.example .env                                    # once; set a real password
cd backend && dotnet tool restore                       # once; installs dotnet-ef 10
cd backend && dotnet user-secrets set ConnectionStrings:ArmoryWiki "Host=localhost;Port=5432;Database=armorywiki;Username=armorywiki;Password=<from .env>" --project src/ArmoryWiki.Api   # once
cd backend && dotnet user-secrets set Admin:Email admin@armorywiki.local --project src/ArmoryWiki.Api      # once; the admin
cd backend && dotnet user-secrets set Admin:Password "<12+ chars, upper, digit, symbol>" --project src/ArmoryWiki.Api   # account is created on startup
docker compose up -d                                    # Postgres 17 on :5432
cd backend && dotnet build                              # must stay at zero warnings (warnings are errors)
cd backend && dotnet run --project src/ArmoryWiki.Api   # http://localhost:5080; in Development it migrates + seeds on startup
cd backend && dotnet test                               # needs Docker running (Testcontainers Postgres)
cd backend && dotnet ef migrations add <Name> -p src/ArmoryWiki.Infrastructure -s src/ArmoryWiki.Api -o Persistence/Migrations
cd frontend && npm run dev                              # http://localhost:3000
cd frontend && npm run lint && npm run build
```

## Layout

- `backend/src/ArmoryWiki.Domain`: entities, enums, value objects. No dependencies.
- `backend/src/ArmoryWiki.Infrastructure`: DbContext, EF configurations, migrations, seeding, file store, importers.
- `backend/src/ArmoryWiki.Api`: Minimal API endpoints, DTOs, validators, CLI subcommands.
- `backend/tests/ArmoryWiki.Api.Tests`: integration tests (Testcontainers Postgres) and importer mapper tests.
- `frontend/`: Next.js 16 App Router + TypeScript + Tailwind. Read `frontend/AGENTS.md` before writing frontend code.
- Shared build settings (TFM, nullable, warnings-as-errors) live in `backend/Directory.Build.props`.

Three backend projects only. No Application layer, MediatR or repository pattern: endpoints use the `DbContext` directly.

## Conventions

Data:
- `int` identity keys plus a unique URL-safe `Slug` on every public entity (`Variant.Slug` is unique per firearm).
- Measurements are **metric** in the DB and API (mm, g, m/s, m, rounds/min). Imperial conversion only in `frontend/src/lib/units.ts`.
- Historical dates use the `FuzzyDate` owned type, never a bare `DateOnly`.
- `CreatedAt`/`UpdatedAt` (UTC) are set in `SaveChangesAsync`.
- Long-form text is Markdown in a `text` column.
- Public endpoints return `Published` rows only; admin endpoints opt out explicitly.
- Importers match on `WikidataId` only, never write a field listed in `LockedFields`, only fill empty fields on published content, and never change `Status` or delete anything.

Backend:
- File-scoped namespaces, `sealed` classes by default, records for DTOs.
- `async` all the way with `CancellationToken` passed through; no `.Result`/`.Wait()`.
- Never return EF entities from endpoints; project into DTOs.
- The API's DbContext is **no-tracking by default** (reads dominate; projecting owned `FuzzyDate`s requires it). Write paths call `AsTracking()`; seeders switch tracking on themselves.
- Admin endpoints live under `/api/admin` (group requires the `Admin` policy and the `X-XSRF-TOKEN` antiforgery header on writes). Writes to editable entities take `?version=` (the row's xmin) and answer 409 when stale; citations, images and videos are add/remove entries without versions. Reuse the shared helpers (`AdminEditing`, `FirearmEditing`, the generic `AdminResource` engine) rather than re-implementing them.
- Image files: `FileStore` + `ImageProcessor` (SkiaSharp, ADR-0006); anything touching one content hash's files takes the hash advisory lock (see `AdminMediaEndpoints`).
- Never run `dotnet ef migrations remove --no-build`: with a stale build it removes the previous (committed) migration instead.
- Endpoints live in `Api/Features/<Resource>/` with their DTOs, return `TypedResults`, and declare non-200 answers (`ProducesProblem`, `ProducesSlugMisses`) so the OpenAPI document stays complete for the frontend.
- Every migration gets a descriptive name. **Never edit a migration that has been applied.**
- Every new endpoint gets at least one integration test. No live-network tests.

Frontend:
- Server Components by default; `"use client"` only where interactivity is needed.
- No `any`. API types come only from the generated OpenAPI schema.
- Never read cookies or headers in public pages (keeps them static).

Content:
- Never copy copyrighted text. Imported prose comes only from Wikipedia (CC BY-SA, attributed); other sources are cited, not copied.
- No manufacturing, conversion or legal-circumvention content (PLAN.md §8).

Git: commit after each completed checklist group with a conventional-commit message (`feat:`, `fix:`, `chore:`, `docs:`).

## Agent skills

### Issue tracker

Issues live in GitHub Issues, managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default five-role vocabulary (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `GLOSSARY.md` + `docs/adr/` at repo root. See `docs/agents/domain.md`.
