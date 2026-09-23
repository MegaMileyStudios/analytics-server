#!/usr/bin/env bash
# Downloads the latest GeoLite2-City database from MaxMind into this folder.
# Credentials are read from etc/geoip/.env, which needs MAXMIND_ACCOUNT_ID and MAXMIND_LICENSE_KEY.
# Docs: https://dev.maxmind.com/geoip/updating-databases/#directly-downloading-databases

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_FILE="$SCRIPT_DIR/.env"
EDITION_ID="GeoLite2-City"
TARGET="$SCRIPT_DIR/$EDITION_ID.mmdb"
BASE_URL="https://download.maxmind.com/geoip/databases/$EDITION_ID/download"

if [[ ! -f "$ENV_FILE" ]]; then
    echo "Missing $ENV_FILE, create it with MAXMIND_ACCOUNT_ID=... and MAXMIND_LICENSE_KEY=... from your MaxMind account." >&2
    exit 1
fi

set -a
# shellcheck source=/dev/null
source "$ENV_FILE"
set +a

if [[ -z "${MAXMIND_ACCOUNT_ID:-}" || -z "${MAXMIND_LICENSE_KEY:-}" ]]; then
    echo "MAXMIND_ACCOUNT_ID and MAXMIND_LICENSE_KEY must be set in $ENV_FILE" >&2
    exit 1
fi

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

download() {
    curl --fail --silent --show-error --location \
        --user "$MAXMIND_ACCOUNT_ID:$MAXMIND_LICENSE_KEY" \
        --output "$2" "$BASE_URL?suffix=$1"
}

echo "Downloading $EDITION_ID..."
download "tar.gz" "$TMP_DIR/$EDITION_ID.tar.gz"
download "tar.gz.sha256" "$TMP_DIR/$EDITION_ID.tar.gz.sha256"

echo "Verifying checksum..."
EXPECTED="$(cut -d ' ' -f 1 "$TMP_DIR/$EDITION_ID.tar.gz.sha256")"
ACTUAL="$(sha256sum "$TMP_DIR/$EDITION_ID.tar.gz" | cut -d ' ' -f 1)"
if [[ "$EXPECTED" != "$ACTUAL" ]]; then
    echo "Checksum mismatch (expected $EXPECTED, got $ACTUAL)" >&2
    exit 1
fi

# The archive contains a single dated folder, e.g. GeoLite2-City_20260922/GeoLite2-City.mmdb
tar -xzf "$TMP_DIR/$EDITION_ID.tar.gz" -C "$TMP_DIR"
MMDB="$(find "$TMP_DIR" -name "$EDITION_ID.mmdb" -type f | head -n 1)"
if [[ -z "$MMDB" ]]; then
    echo "Could not find $EDITION_ID.mmdb in the downloaded archive" >&2
    exit 1
fi

mv "$MMDB" "$TARGET"
echo "Updated $TARGET ($(basename "$(dirname "$MMDB")"))"
