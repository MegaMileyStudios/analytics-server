#!/bin/sh
# Entrypoint of the backup sidecar. Idles when BACKUP_ENABLED isn't "true",
# otherwise validates the config and runs backup.sh on BACKUP_SCHEDULE.
set -eu

. /backup/common.sh

# One-off commands, e.g. `docker compose run --rm backup /backup/restore.sh ...`
if [ "$#" -gt 0 ]; then
    exec "$@"
fi

if [ "${BACKUP_ENABLED:-false}" != "true" ]; then
    log "Backups are disabled (BACKUP_ENABLED is not 'true'), idling"
    exec sleep infinity
fi

errors=""
require() {
    eval "value=\${$1:-}"
    [ -n "$value" ] || errors="$errors\n- $1 is not set"
}
require BACKUP_AGE_RECIPIENT
require DATABASE_URL
require CLICKHOUSE_URL

case "${BACKUP_RETENTION_DAYS:-7}" in
    ''|*[!0-9]*|0) errors="$errors\n- BACKUP_RETENTION_DAYS must be a whole number of at least 1" ;;
esac

if [ -n "${BACKUP_AGE_RECIPIENT:-}" ] && ! echo | age -r "$BACKUP_AGE_RECIPIENT" > /dev/null 2>&1; then
    errors="$errors\n- BACKUP_AGE_RECIPIENT is not a valid age public key"
fi

if [ -n "$errors" ]; then
    body="$(mktemp)"
    {
        echo "The analytics backup container on ${BACKUP_HOST_NAME:-$(hostname)} has BACKUP_ENABLED=true but its configuration is invalid, so NO backups will be made until this is fixed:"
        printf '%b\n' "$errors"
    } > "$body"
    cat "$body"
    /backup/notify.sh "Backups misconfigured on ${BACKUP_HOST_NAME:-$(hostname)}" "$body"
    # Idle instead of exiting so restart: always doesn't send this mail in a loop.
    exec sleep infinity
fi

schedule="${BACKUP_SCHEDULE:-0 3 * * *}"
echo "$schedule /backup/backup.sh" > /tmp/crontab
log "Backups enabled, schedule: '$schedule' (UTC), retention: ${BACKUP_RETENTION_DAYS:-7} days"
exec supercronic -passthrough-logs /tmp/crontab
