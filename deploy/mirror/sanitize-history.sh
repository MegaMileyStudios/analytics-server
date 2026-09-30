#!/bin/sh
# The domain is passed in rather than written here, because this file is public.
# Usage: sanitize-history.sh <internal domain> <branch>
set -eu

domain="$1"
branch="$2"

DOMAIN_RE="$(printf '%s' "$domain" | sed 's/[.]/\\./g')"
export DOMAIN_RE

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

git log --format=%H -i -E --grep='^Reviewed-on: ' --grep="$DOMAIN_RE" "$branch" > "$tmp/matches"
if [ ! -s "$tmp/matches" ]; then
    echo "No commit messages mention the internal domain"
else
    while read -r commit; do
        echo "$commit"
        git rev-list --ancestry-path "$commit..$branch"
    done < "$tmp/matches" | sort -u > "$tmp/rewrite"
    
    git rev-list --parents "$branch" | awk -v set="$tmp/rewrite" '
        BEGIN { while ((getline c < set) > 0) rewrite[c] = 1 }
        ($1 in rewrite) { for (i = 2; i <= NF; i++) if (!($i in rewrite)) print $i }' | sort -u > "$tmp/boundary"

    cat > "$tmp/msg-filter.sh" <<'EOF'
in="$(mktemp)"
cat > "$in"
if grep -qiE "^Reviewed-on: |${DOMAIN_RE}" "$in"; then
    # 1. Gitea's trailer, 2. markdown links keep only their text,
    # 3. bare URLs, 4. any other mention of the host.
    sed -E \
        -e '/^Reviewed-on: /d' \
        -e "s#\[([^]]*)\]\([a-z]+://[^)]*${DOMAIN_RE}[^)]*\)#\1#gI" \
        -e "s#[a-z]+://[^[:space:]]*${DOMAIN_RE}[^[:space:])>]*#(internal link)#gI" \
        -e "s#[A-Za-z0-9.-]*${DOMAIN_RE}#(internal host)#gI" \
        "$in" | git stripspace
else
    # Passed through byte for byte.
    cat "$in"
fi
rm -f "$in"
EOF

    echo "Rewriting $(wc -l < "$tmp/rewrite") commits, $(wc -l < "$tmp/matches") of them with a new message"
    # shellcheck disable=SC2046
    FILTER_BRANCH_SQUELCH_WARNING=1 git filter-branch -f --msg-filter "sh $tmp/msg-filter.sh" \
        -- "$branch" --not $(cat "$tmp/boundary") > "$tmp/filter-branch.log" 2>&1 \
        || { cat "$tmp/filter-branch.log"; exit 1; }
fi

if git log --format=%B "$branch" | grep -qiF "$domain"; then
    echo "The internal domain is still mentioned in these commit messages, not publishing:"
    git log --format='%h %s' -i -F --grep="$domain" "$branch"
    exit 1
fi
if git grep -qiF "$domain" "$branch" -- .; then
    echo "The internal domain is mentioned in these files, not publishing:"
    git grep -liF "$domain" "$branch" -- .
    exit 1
fi
echo "History on $branch is clean"
