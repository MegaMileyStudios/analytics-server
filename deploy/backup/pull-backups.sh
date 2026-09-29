#!/bin/sh
# Runs on the NAS: pulls new encrypted backups from the production server over SSH,
# using the restricted backup-pull account (read-only rrsync, see the
# "Backups & Restore" wiki page), and prunes local copies older than
# PULL_RETENTION_DAYS. Meant to be run from cron a few hours after the
# server's BACKUP_SCHEDULE.
#
# Configure via environment variables:
#   PULL_HOST            production server hostname (required)
#   PULL_PORT            SSH port (default 22)
#   PULL_USER            SSH user (default backup-pull)
#   PULL_KEY             SSH private key for that user (required)
#   PULL_DEST            local directory to store backups in (required)
#   PULL_RETENTION_DAYS  days of backups to keep locally (default 30)
set -eu

: "${PULL_HOST:?PULL_HOST is required}"
: "${PULL_KEY:?PULL_KEY is required}"
: "${PULL_DEST:?PULL_DEST is required}"
port="${PULL_PORT:-22}"
user="${PULL_USER:-backup-pull}"
retention="${PULL_RETENTION_DAYS:-30}"

mkdir -p "$PULL_DEST"

# The server side is chrooted to the backup directory by rrsync, so the remote path is relative to it.
# No --delete: pruning on the server must never remove the NAS copies.
rsync -a --ignore-existing \
    --include 'analytics-*.tar.age' --exclude '*' \
    -e "ssh -p $port -i $PULL_KEY -o BatchMode=yes" \
    "$user@$PULL_HOST:" "$PULL_DEST/"

find "$PULL_DEST" -maxdepth 1 -name 'analytics-*.tar.age' -mtime "+$((retention - 1))" -print -delete

echo "Pulled backups into $PULL_DEST, newest: $(ls -1 "$PULL_DEST"/analytics-*.tar.age 2>/dev/null | tail -n 1)"
