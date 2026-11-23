#!/usr/bin/env bash
# Nightly backup (PLAN.md Phase 6c): a fresh pg_dump plus the media volume into a restic repository (Backblaze B2
# in production), encrypted and deduplicated. Run from /opt/armorywiki by the systemd timer. Needs backup.env.
# The keys volume is left out on purpose: losing it only signs the admin out; leaking it would allow forged cookies.
set -euo pipefail
umask 077
cd "$(dirname "$0")"

PROJECT=${COMPOSE_PROJECT_NAME:-armorywiki}
RESTIC_IMAGE=restic/restic:0.19.1
HERE=$(pwd -W 2>/dev/null || pwd) # pwd -W: a Windows path when tried out under Git Bash
compose() { docker compose -f docker-compose.prod.yml -p "$PROJECT" "$@"; }

# Optional dead-man's switch (e.g. healthchecks.io): a ping on success, /fail on error, so a failing or missed
# night sends an alert instead of only a line in the journal.
HEALTHCHECK_URL=$(sed -n 's/^HEALTHCHECK_URL=//p' backup.env | tr -d '\r')
ping_healthcheck() { [ -z "$HEALTHCHECK_URL" ] || curl -fsS -m 10 --retry 3 "$HEALTHCHECK_URL$1" > /dev/null || true; }
trap 'ping_healthcheck /fail' ERR
# A fixed hostname: restic groups snapshots by host when it forgets old ones.
restic() {
  docker run --rm --env-file backup.env --hostname armorywiki \
    -v "${PROJECT}_media:/data/media:ro" \
    -v "$HERE/backups:/data/db:ro" \
    -v armorywiki-restic-cache:/root/.cache/restic \
    ${RESTIC_LOCAL_REPO:+-v "$RESTIC_LOCAL_REPO:/repo"} \
    "$RESTIC_IMAGE" "$@"
}

mkdir -p backups
# Custom format: compressed, and pg_restore can restore it table by table if ever needed.
# shellcheck disable=SC2016 # expanded by the shell inside the postgres container
compose exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > backups/db.dump.tmp
mv backups/db.dump.tmp backups/db.dump

# The repository is initialized once by hand (deploy/README.md): a mistyped one must fail, not start afresh.
restic backup --tag nightly /data
restic forget --tag nightly --keep-daily 7 --keep-weekly 4 --keep-monthly 6 --prune
if [ "$(date +%u)" = 7 ]; then
  restic check # Sundays: verify the repository's structure
fi
echo "Backup done: $(du -h backups/db.dump | cut -f1) database dump plus media."
ping_healthcheck ""
