#!/bin/sh
# Shared helpers for the backup scripts. Sourced, not executed.

log() {
    echo "[backup] $(date -u +%Y-%m-%dT%H:%M:%SZ) $*"
}

# Reads a key from a .NET-style connection string ("Key=Value;Key=Value"),
# matching the key case-insensitively.
# Usage: conn_get "$DATABASE_URL" "User Id"
conn_get() {
    echo "$1" | tr ';' '\n' | awk -v key="$2" '
        BEGIN { key = tolower(key) }
        {
            i = index($0, "=")
            if (i == 0) next
            k = tolower($0); k = substr(k, 1, i - 1)
            gsub(/^[ \t]+|[ \t]+$/, "", k)
            if (k == key) { print substr($0, i + 1); exit }
        }'
}

# Resolves Postgres + ClickHouse connection details from the same
# DATABASE_URL / CLICKHOUSE_URL the app itself uses.
load_db_config() {
    PGHOST="$(conn_get "$DATABASE_URL" "Server")"
    [ -n "$PGHOST" ] || PGHOST="$(conn_get "$DATABASE_URL" "Host")"
    PGPORT="$(conn_get "$DATABASE_URL" "Port")"
    PGPORT="${PGPORT:-5432}"
    PGUSER="$(conn_get "$DATABASE_URL" "User Id")"
    [ -n "$PGUSER" ] || PGUSER="$(conn_get "$DATABASE_URL" "Username")"
    PGPASSWORD="$(conn_get "$DATABASE_URL" "Password")"
    PGDATABASE="$(conn_get "$DATABASE_URL" "Database")"
    export PGHOST PGPORT PGUSER PGPASSWORD PGDATABASE

    CH_HOST="$(conn_get "$CLICKHOUSE_URL" "Host")"
    CH_PORT="$(conn_get "$CLICKHOUSE_URL" "Port")"
    CH_PORT="${CH_PORT:-8123}"
    CH_PROTOCOL="$(conn_get "$CLICKHOUSE_URL" "Protocol")"
    CH_PROTOCOL="${CH_PROTOCOL:-http}"
    CH_USER="$(conn_get "$CLICKHOUSE_URL" "Username")"
    CH_USER="${CH_USER:-default}"
    CH_PASSWORD="$(conn_get "$CLICKHOUSE_URL" "Password")"
    CH_DATABASE="$(conn_get "$CLICKHOUSE_URL" "Database")"
    CH_DATABASE="${CH_DATABASE:-default}"
}

# Runs a query against ClickHouse over HTTP and prints the result.
ch_query() {
    curl -sS --fail-with-body \
        -u "$CH_USER:$CH_PASSWORD" \
        --data-binary "$1" \
        "$CH_PROTOCOL://$CH_HOST:$CH_PORT/"
}
