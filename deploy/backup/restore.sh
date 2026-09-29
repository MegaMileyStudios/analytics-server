#!/bin/sh
# Restores an encrypted backup made by do-backup.sh into the Postgres + ClickHouse
# instances configured via DATABASE_URL / CLICKHOUSE_URL, then verifies the
# restored row counts against the backup's manifest.
#
# This REPLACES all data in both databases. Stop the app before running it.
# Runs inside the backup image, e.g. on the server:
#   docker compose stop app
#   docker compose run --rm \
#       -v /path/to/analytics-XXXX.tar.age:/restore/backup.tar.age:ro \
#       -v /path/to/backup-key.txt:/restore/key.txt:ro \
#       backup /backup/restore.sh /restore/backup.tar.age /restore/key.txt --yes
#   docker compose start app
set -eu -o pipefail

. /backup/common.sh

if [ "$#" -lt 2 ]; then
    echo "Usage: restore.sh <backup.tar.age> <age identity file> --yes" >&2
    exit 2
fi
backup_file="$1"
identity="$2"
if [ "${3:-}" != "--yes" ]; then
    echo "This replaces ALL data in the target databases. Re-run with --yes to confirm." >&2
    exit 2
fi

load_db_config
CH_UID="${CLICKHOUSE_UID:-101}"
name="restore-$(date -u +%Y%m%dT%H%M%SZ)"
workdir="/backups/$name"

cleanup() {
    rm -rf "$workdir"
}
trap cleanup EXIT

log "Decrypting and unpacking $backup_file"
mkdir -p "$workdir"
age -d -i "$identity" "$backup_file" | tar -C "$workdir" -xf -
chown -R "$CH_UID:$CH_UID" "$workdir"
jq -r '"Backup from \(.host), created \(.created_at) (Postgres \(.postgres.version), ClickHouse \(.clickhouse.version))"' "$workdir/manifest.json"

dump="$(find "$workdir/postgres" -name '*.dump' | head -n 1)"
log "Restoring Postgres database $PGDATABASE from $(basename "$dump")"
pg_restore --clean --if-exists --no-owner --exit-on-error --dbname="$PGDATABASE" "$dump"

log "Restoring ClickHouse database $CH_DATABASE"
for table in $(ch_query "SELECT name FROM system.tables WHERE database = '$CH_DATABASE' ORDER BY engine LIKE '%View' DESC FORMAT TSV"); do
    ch_query "DROP TABLE $CH_DATABASE.\`$table\` SYNC" > /dev/null
done
result="$(ch_query "RESTORE DATABASE $CH_DATABASE FROM File('$name/clickhouse/') ASYNC")"
restore_id="$(echo "$result" | cut -f1)"
while :; do
    status="$(ch_query "SELECT status, error FROM system.backups WHERE id = '$restore_id' FORMAT TSV")"
    case "$status" in
        RESTORED*) break ;;
        RESTORING*) sleep 2 ;;
        *) log "ClickHouse restore failed: $status"; exit 1 ;;
    esac
done

log "Verifying row counts against the manifest"
mismatches=0
jq -r '.row_counts[] | [.store, .table, .rows] | @tsv' "$workdir/manifest.json" > /tmp/expected-counts.tsv
while IFS="$(printf '\t')" read -r store table expected; do
    case "$store" in
        postgres)   actual="$(psql -XAtc "SELECT count(*) FROM $table")" ;;
        clickhouse) actual="$(ch_query "SELECT count() FROM $CH_DATABASE.\`$table\`")" ;;
        *) continue ;;
    esac
    if [ "$actual" = "$expected" ]; then
        log "  OK       $store $table: $actual rows"
    else
        log "  MISMATCH $store $table: expected $expected, got $actual"
        mismatches=$((mismatches + 1))
    fi
done < /tmp/expected-counts.tsv

if [ "$mismatches" -ne 0 ]; then
    log "Restore finished with $mismatches mismatching table(s)"
    exit 1
fi
log "Restore finished and verified"
