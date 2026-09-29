#!/bin/sh
# Shared helpers for test-restore.sh and test-roundtrip.sh. Sourced, not executed.
#
# Files are moved in and out of containers with `docker cp` instead of bind
# mounts, so the scripts also work when the Docker CLI runs inside a container
# that talks to the host's Docker daemon (like a CI job), where bind-mount
# paths would resolve on the host instead of where the script runs.

# Keep in sync with the images in deploy/docker-compose.dev.yml.
TEST_POSTGRES_IMAGE="postgres:15"
TEST_CLICKHOUSE_IMAGE="clickhouse/clickhouse-server:23.8.4.69"
TEST_BACKUP_IMAGE="analytics-backup:test"
TEST_PASSWORD="backup_test_pw"

test_helpers_dir="$(cd "$(dirname "$0")" && pwd)"

build_backup_image() {
    echo "Building backup image"
    docker build -q -t "$TEST_BACKUP_IMAGE" "$test_helpers_dir" > /dev/null
}

# Usage: start_postgres <container name> <network>
start_postgres() {
    docker run -d --name "$1" --network "$2" \
        -e POSTGRES_USER=aptabase -e POSTGRES_PASSWORD="$TEST_PASSWORD" -e POSTGRES_DB=aptabase \
        "$TEST_POSTGRES_IMAGE" > /dev/null
}

# Usage: start_clickhouse <container name> <network> <staging volume>
start_clickhouse() {
    docker create --name "$1" --network "$2" \
        --ulimit nofile=262144:262144 \
        -e CLICKHOUSE_USER=aptabase -e CLICKHOUSE_PASSWORD="$TEST_PASSWORD" \
        -v "$3:/backups" \
        "$TEST_CLICKHOUSE_IMAGE" > /dev/null
    docker cp "$test_helpers_dir/../clickhouse/backups.xml" "$1:/etc/clickhouse-server/config.d/backups.xml"
    docker start "$1" > /dev/null
}

# Usage: wait_for_databases <postgres container> <clickhouse container>
wait_for_databases() {
    echo "Waiting for the databases to be ready"
    for _ in $(seq 1 90); do
        if docker exec "$1" pg_isready -U aptabase -d aptabase > /dev/null 2>&1 \
            && docker exec "$2" clickhouse-client -u aptabase --password "$TEST_PASSWORD" -q "SELECT 1" > /dev/null 2>&1; then
            return 0
        fi
        sleep 1
    done
    echo "Databases did not become ready in time" >&2
    return 1
}
