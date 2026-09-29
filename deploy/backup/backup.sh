#!/bin/sh
# Runs one backup (do-backup.sh) with a lock, a log file, and a failure e-mail.
# This is what the schedule calls; run it manually with:
#   docker compose exec backup /backup/backup.sh
set -u -o pipefail

. /backup/common.sh

lock=/tmp/backup.lock
if ! mkdir "$lock" 2>/dev/null; then
    log "Another backup is already running, skipping this run"
    exit 0
fi
trap 'rmdir "$lock"' EXIT

run_log="$(mktemp)"
rm -f /tmp/backup-step

/backup/do-backup.sh 2>&1 | tee "$run_log"
status=$?

if [ "$status" -ne 0 ]; then
    failed_step="$(cat /tmp/backup-step 2>/dev/null || echo "unknown step")"
    log "Backup FAILED (exit $status) during: $failed_step"

    body="$(mktemp)"
    {
        echo "The scheduled analytics backup failed."
        echo
        echo "Host:        ${BACKUP_HOST_NAME:-$(hostname)}"
        echo "Time (UTC):  $(date -u +%Y-%m-%dT%H:%M:%SZ)"
        echo "Failed step: $failed_step"
        echo "Exit code:   $status"
        echo
        echo "Last 50 lines of the log:"
        echo "-------------------------"
        tail -n 50 "$run_log"
    } > "$body"
    /backup/notify.sh "Backup failed on ${BACKUP_HOST_NAME:-$(hostname)}" "$body"
    rm -f "$body"
fi

rm -f "$run_log"
exit "$status"
