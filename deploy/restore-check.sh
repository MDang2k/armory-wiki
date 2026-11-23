#!/usr/bin/env bash
# Proves a backup restores (PLAN.md Phase 6c): brings a snapshot (default: latest) back into a scratch database
# (armorywiki_restore_check, in the running Postgres) and a scratch folder (./restore-check), then compares row
# and file counts with the live data. Never touches the live database or the media volume.
#   ./restore-check.sh [snapshot-id]     KEEP=1 ./restore-check.sh   (keep the scratch database for a look)
set -euo pipefail
cd "$(dirname "$0")"

SNAPSHOT=${1:-latest}
PROJECT=${COMPOSE_PROJECT_NAME:-armorywiki}
RESTIC_IMAGE=restic/restic:0.19.1
SCRATCH_DB=armorywiki_restore_check
HERE=$(pwd -W 2>/dev/null || pwd)
compose() { docker compose -f docker-compose.prod.yml -p "$PROJECT" "$@"; }
psql_in() { compose exec -T postgres sh -c "psql -U \"\$POSTGRES_USER\" -d $1 -At -c \"$2\""; }

rm -rf ./restore-check
mkdir -p restore-check
# As the calling user, so the restored files are ours to delete (as root they would not be). RESTORE_AS overrides
# it, e.g. RESTORE_AS=0:0 for a local-filesystem repository that root created.
docker run --rm --env-file backup.env --hostname armorywiki --user "${RESTORE_AS:-$(id -u):$(id -g)}" -e RESTIC_CACHE_DIR=/tmp/restic-cache \
  -v "$HERE/restore-check:/restore" \
  ${RESTIC_LOCAL_REPO:+-v "$RESTIC_LOCAL_REPO:/repo"} \
  "$RESTIC_IMAGE" restore "$SNAPSHOT" --target /restore

compose exec -T postgres sh -c "dropdb -U \"\$POSTGRES_USER\" --if-exists $SCRATCH_DB && createdb -U \"\$POSTGRES_USER\" $SCRATCH_DB"
compose exec -T postgres sh -c "pg_restore -U \"\$POSTGRES_USER\" -d $SCRATCH_DB --no-owner --exit-on-error" < restore-check/data/db/db.dump

# shellcheck disable=SC2016 # expanded inside the container
live_db=$(compose exec -T postgres sh -c 'echo "$POSTGRES_DB"' | tr -d '\r')
status=0
for table in firearms people organizations cartridges images citations; do
  live=$(psql_in "$live_db" "SELECT count(*) FROM $table" | tr -d '\r')
  restored=$(psql_in "$SCRATCH_DB" "SELECT count(*) FROM $table" | tr -d '\r')
  echo "$table: live $live, restored $restored"
done
live_files=$(docker run --rm -v "${PROJECT}_media:/m:ro" busybox:1.37 find /m -type f | wc -l)
restored_files=$(find restore-check/data/media -type f | wc -l)
echo "media files: live $live_files, restored $restored_files"
[ "$restored_files" -gt 0 ] || [ "$live_files" -eq 0 ] || status=1
[ "$(psql_in "$SCRATCH_DB" 'SELECT count(*) FROM tags' | tr -d '\r')" -gt 0 ] || status=1 # reference data: an empty restore has none

if [ "${KEEP:-0}" != 1 ]; then
  compose exec -T postgres sh -c "dropdb -U \"\$POSTGRES_USER\" $SCRATCH_DB"
  rm -rf ./restore-check
fi

# Counts may differ by what changed since the snapshot; an empty or failed restore is what fails this check.
[ "$status" = 0 ] && echo "Restore check passed." || echo "Restore check FAILED."
exit "$status"
