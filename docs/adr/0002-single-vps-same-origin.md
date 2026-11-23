# 2. Single VPS, same-origin deploy

Status: Accepted (2026-11-20)

## Context
The plan had no deployment target, yet hosting decides auth cookies, CORS, caching and where Postgres and files live. This is a solo project with modest traffic.

## Decision
Everything runs on one VPS with Docker Compose: Caddy (automatic TLS) in front of the Next.js app and the ASP.NET Core API, plus Postgres and a media volume. The API is served at `/api` on the same origin as the site. Next.js rewrites `/api` to the backend in development so dev is same-origin too. Images live on a Docker volume served by Caddy (no MinIO). Deploys go GitHub Actions → GHCR → SSH `docker compose pull && up -d`; EF migrations (and the reference seeder) run in a one-shot `migrate` container from the API image, never on API startup. Nightly `pg_dump` + media backups go offsite.

## Consequences
- No CORS configuration; Identity cookie auth is same-site.
- A single machine is a single point of failure; acceptable at this scale, mitigated by tested restores.
- Moving files to S3-compatible storage later means changing the one `FileStore` class.
