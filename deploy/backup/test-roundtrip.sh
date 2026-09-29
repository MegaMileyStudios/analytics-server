#!/bin/sh
# Round-trip test of the backup code, used by the backup-roundtrip workflow:
# seeds throwaway Postgres + ClickHouse containers (ClickHouse with the real
# etc/clickhouse migrations), makes a backup with a throwaway age key, checks
# the manifest matches what was seeded, then restores it with test-restore.sh.
#
# Usage: deploy/backup/test-roundtrip.sh
# Needs only Docker. Removes everything it creates.
set -eu

script_dir="$(cd "$(dirname "$0")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
. "$script_dir/test-helpers.sh"

id="analytics-roundtrip-test-$$"
workdir="$(mktemp -d)"

cleanup() {
    docker rm -f "$id-postgres" "$id-clickhouse" "$id-backup" "$id-inspect" > /dev/null 2>&1 || true
    docker volume rm "$id-staging" "$id-output" > /dev/null 2>&1 || true
    docker network rm "$id" > /dev/null 2>&1 || true
    rm -rf "$workdir"
}
trap cleanup EXIT

build_backup_image

echo "Starting source Postgres + ClickHouse"
docker network create "$id" > /dev/null
docker volume create "$id-staging" > /dev/null
docker volume create "$id-output" > /dev/null
start_postgres "$id-postgres" "$id"
start_clickhouse "$id-clickhouse" "$id" "$id-staging"
wait_for_databases "$id-postgres" "$id-clickhouse"

ch() {
    docker exec -i "$id-clickhouse" clickhouse-client -u aptabase --password "$TEST_PASSWORD" --multiquery "$@"
}

echo "Applying ClickHouse migrations from etc/clickhouse"
for migration in "$repo_root"/etc/clickhouse/*.sql; do
    ch < "$migration"
done

echo "Seeding test data"
# Two inserts so the events table has more than one part, the ttl is in the
# future so ClickHouse doesn't expire the rows, and the props contain
# characters that tend to break escaping.
for batch in 1 2; do
    ch -q "INSERT INTO events (app_id, timestamp, event_name, session_id, string_props, numeric_props, ttl)
           SELECT concat('app_', toString(number % 3)), now() - number, 'level_started', toString(number),
                  '{\"name\":\"tab\\there, newline\\nhere; quote '' done\"}', '{\"score\":1.5}', now() + INTERVAL 1 YEAR
           FROM numbers($((batch * 500)))"
done
ch -q "INSERT INTO error_events (error_id, app_id, timestamp, error_message, ttl)
       SELECT toString(number), 'app_0', now() - number, 'Boom', now() + INTERVAL 1 YEAR FROM numbers(42)"
docker exec -i "$id-postgres" psql -v ON_ERROR_STOP=1 -q -U aptabase -d aptabase <<'SQL'
CREATE TABLE owners (id text PRIMARY KEY, name text NOT NULL);
CREATE TABLE apps (id text PRIMARY KEY, owner_id text NOT NULL REFERENCES owners(id), name text, props jsonb);
INSERT INTO owners VALUES ('o1', E'Multi\nline'), ('o2', E'Tab\there; semicolon'), ('o3', E'Back\\slash ''quote'' ünïcode');
INSERT INTO apps SELECT 'app_' || n, 'o' || (n % 3 + 1), E'App\n' || n, '{"a": [1, 2]}' FROM generate_series(1, 250) n;
SQL
expected="postgres public.owners 3
postgres public.apps 250
clickhouse events 1500
clickhouse error_events 42"

echo "Generating a throwaway age key"
docker run --rm --entrypoint age-keygen "$TEST_BACKUP_IMAGE" > "$workdir/key.txt" 2> /dev/null
recipient="$(grep -o 'age1[a-z0-9]*' "$workdir/key.txt")"

echo "Running backup.sh"
docker run --name "$id-backup" --network "$id" \
    -e DATABASE_URL="Server=$id-postgres;Port=5432;User Id=aptabase;Password=$TEST_PASSWORD;Database=aptabase" \
    -e CLICKHOUSE_URL="Host=$id-clickhouse;Port=8123;Username=aptabase;Password=$TEST_PASSWORD" \
    -e BACKUP_AGE_RECIPIENT="$recipient" \
    -e BACKUP_HOST_NAME=roundtrip-test \
    -v "$id-staging:/backups" \
    -v "$id-output:/output" \
    "$TEST_BACKUP_IMAGE" /backup/backup.sh
docker cp "$id-backup:/output/." "$workdir/output"
backup_file="$(find "$workdir/output" -name 'analytics-*.tar.age' | head -n 1)"
if [ -z "$backup_file" ]; then
    echo "backup.sh did not produce a backup file" >&2
    exit 1
fi
if find "$workdir/output" -name '*.partial' | grep -q .; then
    echo "backup.sh left a .partial file behind" >&2
    exit 1
fi

echo "Checking the manifest against the seeded data"
# Without this a backup that lost data would still pass test-restore.sh, since
# the manifest would then agree with the (equally incomplete) restore.
docker create --name "$id-inspect" "$TEST_BACKUP_IMAGE" sh -c \
    "age -d -i /tmp/key.txt /tmp/backup.tar.age | tar -xOf - ./manifest.json | jq -r '.row_counts[] | \"\(.store) \(.table) \(.rows)\"'" > /dev/null
docker cp "$backup_file" "$id-inspect:/tmp/backup.tar.age"
docker cp "$workdir/key.txt" "$id-inspect:/tmp/key.txt"
manifest_counts="$(docker start -a "$id-inspect")"
echo "$manifest_counts"
missing=0
while IFS= read -r line; do
    if ! echo "$manifest_counts" | grep -qxF "$line"; then
        echo "Manifest is missing expected row count: $line" >&2
        missing=1
    fi
done <<EOF
$expected
EOF
[ "$missing" -eq 0 ]

"$script_dir/test-restore.sh" "$backup_file" "$workdir/key.txt"

echo "Round-trip test PASSED"
