#!/bin/sh
# Restore test: restores a backup into throwaway Postgres + ClickHouse containers
# and verifies the row counts against its manifest. Touches nothing but the
# containers/volume/network it creates itself, and removes those afterwards.
#
# Usage: deploy/backup/test-restore.sh <analytics-XXXX.tar.age> <age identity file>
# Exits 0 when the backup restores and verifies cleanly, non-zero otherwise.
set -eu

if [ "$#" -ne 2 ]; then
    echo "Usage: $0 <backup.tar.age> <age identity file>" >&2
    exit 2
fi

backup_file="$1"
identity="$2"

. "$(dirname "$0")/test-helpers.sh"

id="analytics-restore-test-$$"

cleanup() {
    docker rm -f "$id-restore" "$id-postgres" "$id-clickhouse" > /dev/null 2>&1 || true
    docker volume rm "$id-staging" > /dev/null 2>&1 || true
    docker network rm "$id" > /dev/null 2>&1 || true
}
trap cleanup EXIT

build_backup_image

echo "Starting throwaway Postgres + ClickHouse"
docker network create "$id" > /dev/null
docker volume create "$id-staging" > /dev/null
start_postgres "$id-postgres" "$id"
start_clickhouse "$id-clickhouse" "$id" "$id-staging"
wait_for_databases "$id-postgres" "$id-clickhouse"

echo "Restoring $(basename "$backup_file")"
docker create --name "$id-restore" --network "$id" \
    -e DATABASE_URL="Server=$id-postgres;Port=5432;User Id=aptabase;Password=$TEST_PASSWORD;Database=aptabase" \
    -e CLICKHOUSE_URL="Host=$id-clickhouse;Port=8123;Username=aptabase;Password=$TEST_PASSWORD" \
    -v "$id-staging:/backups" \
    "$TEST_BACKUP_IMAGE" /backup/restore.sh /tmp/backup.tar.age /tmp/key.txt --yes > /dev/null
docker cp "$backup_file" "$id-restore:/tmp/backup.tar.age"
docker cp "$identity" "$id-restore:/tmp/key.txt"
docker start -a "$id-restore"

echo "Restore test PASSED"
