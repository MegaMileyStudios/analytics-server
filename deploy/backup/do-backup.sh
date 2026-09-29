#!/bin/sh
# Creates one encrypted backup of Postgres + ClickHouse. Called by backup.sh,
# which takes care of logging, locking and failure e-mails.
set -eu -o pipefail

. /backup/common.sh
load_db_config

STAGING_ROOT=/backups
OUTPUT_DIR=/output
CH_UID="${CLICKHOUSE_UID:-101}"
RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-7}"

ts="$(date -u +%Y%m%dT%H%M%SZ)"
name="analytics-$ts"
staging="$STAGING_ROOT/$name"
final="$OUTPUT_DIR/$name.tar.age"
partial="$final.partial"

step() {
    echo "$*" > /tmp/backup-step
    log "$*"
}

cleanup() {
    rm -rf "$staging"
    rm -f "$partial"
}
trap cleanup EXIT

step "Preparing staging directory $staging"
mkdir -p "$staging/postgres"
# ClickHouse writes its part of the backup into this directory itself, as its own user.
chown "$CH_UID:$CH_UID" "$staging"

step "Dumping Postgres database $PGDATABASE"
pg_dump --format=custom --no-owner --file="$staging/postgres/$PGDATABASE.dump"

step "Backing up ClickHouse database $CH_DATABASE"
result="$(ch_query "BACKUP DATABASE $CH_DATABASE TO File('$name/clickhouse/') ASYNC")"
backup_id="$(echo "$result" | cut -f1)"
while :; do
    status="$(ch_query "SELECT status, error FROM system.backups WHERE id = '$backup_id' FORMAT TSV")"
    case "$status" in
        BACKUP_CREATED*) break ;;
        CREATING_BACKUP*) sleep 2 ;;
        *) log "ClickHouse backup failed: $status"; exit 1 ;;
    esac
done

step "Writing manifest"
# Row counts are taken from the backup artifacts themselves rather than the live
# databases, so they describe exactly what's in the archive even while the app
# keeps writing. restore.sh checks the restored databases against these.
counts="$staging/counts.tsv"
: > "$counts"
pg_restore --data-only --file=- "$staging/postgres/$PGDATABASE.dump" | awk '
    /^COPY / { table = $2; rows = 0; inside = 1; next }
    inside && /^\\\.$/ { printf "postgres\t%s\t%d\n", table, rows; inside = 0; next }
    inside { rows++ }' >> "$counts"
# Every MergeTree part has a count.txt (without a trailing newline). Empty tables
# have no data directory at all, so the table list comes from the metadata.
for table_sql in "$staging"/clickhouse/metadata/"$CH_DATABASE"/*.sql; do
    [ -f "$table_sql" ] || continue
    head -c 20 "$table_sql" | grep -q '^CREATE TABLE' || continue
    table="$(basename "$table_sql" .sql)"
    rows=0
    for count_file in "$staging"/clickhouse/data/"$CH_DATABASE"/"$table"/*/count.txt; do
        [ -f "$count_file" ] || continue
        rows=$((rows + $(cat "$count_file")))
    done
    printf "clickhouse\t%s\t%d\n" "$table" "$rows" >> "$counts"
done

jq -n \
    --arg created_at "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
    --arg host "${BACKUP_HOST_NAME:-$(hostname)}" \
    --arg postgres_version "$(psql -XAtc 'SHOW server_version')" \
    --arg postgres_database "$PGDATABASE" \
    --arg clickhouse_version "$(ch_query 'SELECT version()')" \
    --arg clickhouse_database "$CH_DATABASE" \
    --rawfile counts "$counts" \
    '{
        created_at: $created_at,
        host: $host,
        postgres: { version: $postgres_version, database: $postgres_database },
        clickhouse: { version: $clickhouse_version, database: $clickhouse_database },
        row_counts: ($counts | split("\n") | map(select(length > 0) | split("\t") | { store: .[0], table: .[1], rows: (.[2] | tonumber) }))
    }' > "$staging/manifest.json"
cat "$counts"

step "Encrypting archive to $final"
tar -C "$staging" -cf - . | age -r "$BACKUP_AGE_RECIPIENT" > "$partial"
if [ -n "${BACKUP_FILE_GID:-}" ]; then
    chgrp "$BACKUP_FILE_GID" "$partial"
fi
chmod 0640 "$partial"
mv "$partial" "$final"

step "Pruning backups older than $RETENTION_DAYS days"
find "$OUTPUT_DIR" -maxdepth 1 -name 'analytics-*.tar.age' -mtime "+$((RETENTION_DAYS - 1))" -print -delete

date -u +%Y-%m-%dT%H:%M:%SZ > "$OUTPUT_DIR/last-success"
log "Backup finished: $final ($(du -h "$final" | cut -f1))"
