#!/usr/bin/env python3
"""Seed months of player history straight into a local ClickHouse, to check the retention page.

The API rejects events older than a day, so this writes to the events table directly instead of
going through ingestion. Every player gets a first day somewhere in the history and comes back on
some of the following days, anonymous players (no persistent ID) are mixed in and must be ignored.
After inserting, the expected retention tables are printed, computed here in Python independently
of the ClickHouse query, so they can be compared with the Retention page (all times are UTC).

Usage: scripts/seed-retention-events.py <app id> [--seed 42] [--debug] [--clear]

The app id is the one in the dashboard URL (/<app id>/...), not the App Key.
Pass --clear to remove the events a previous run of this script seeded for the app before inserting.
"""

import argparse
import base64
import hashlib
import json
import random
import urllib.error
import urllib.parse
import urllib.request
from datetime import date, datetime, time, timedelta, timezone

SDK_VERSION = "aptabase-retention-seed@1.0.0"
EVENT_NAMES = ["level_started", "level_completed", "ball_dropped", "settings_opened", "shop_opened"]
DEBUG_TTL = timedelta(days=182)
RELEASE_TTL = timedelta(days=5 * 365)


def persistent_id(name: str) -> str:
    # Same format the game sends: a 64 character hex SHA-256 hash
    return hashlib.sha256(f"aptabase-retention-seed-{name}".encode()).hexdigest()


def clickhouse(url: str, user: str, password: str, query: str, body: bytes | None = None) -> str:
    token = base64.b64encode(f"{user}:{password}".encode()).decode()
    request = urllib.request.Request(
        f"{url}/?query={urllib.parse.quote(query)}",
        data=body if body is not None else b"",
        method="POST",
        headers={"Authorization": f"Basic {token}"},
    )
    try:
        with urllib.request.urlopen(request) as response:
            return response.read().decode()
    except urllib.error.HTTPError as e:
        raise SystemExit(f"ClickHouse request failed with {e.code}: {e.read().decode()}")


def bucket(day: date, unit: str) -> date:
    if unit == "week":
        return day - timedelta(days=day.weekday())  # weeks start on Monday
    if unit == "month":
        return day.replace(day=1)
    return day


def diff(start: date, end: date, unit: str) -> int:
    if unit == "week":
        return (end - start).days // 7
    if unit == "month":
        return (end.year - start.year) * 12 + end.month - start.month
    return (end - start).days


def shift(start: date, count: int, unit: str) -> date:
    if unit == "week":
        return start + timedelta(weeks=count)
    if unit == "month":
        months = start.year * 12 + start.month - 1 + count
        return date(months // 12, months % 12 + 1, 1)
    return start + timedelta(days=count)


def print_expected(players: dict[str, set[date]], today: date, unit: str, cohorts: int, periods: int) -> None:
    last = bucket(today, unit)
    first = shift(last, -(cohorts - 1), unit)
    table: dict[date, list[set[str]]] = {}
    for uid, days in players.items():
        cohort = bucket(min(days), unit)
        if not first <= cohort <= last:
            continue
        row = table.setdefault(cohort, [set() for _ in range(periods + 1)])
        for day in days:
            n = diff(cohort, bucket(day, unit), unit)
            if n <= periods:
                row[n].add(uid)

    prefix = unit[0].upper()
    label = {"day": "daily", "week": "weekly", "month": "monthly"}[unit]
    print(f"\nExpected {label} retention ({cohorts} cohorts, {prefix}0..{prefix}{periods}), players and % of the cohort:")
    print(f"{'Cohort':<12}{'Users':>6}  " + "".join(f"{prefix}{n:<9}" for n in range(periods + 1)))
    for cohort in sorted(table):
        row = table[cohort]
        size = len(row[0])
        cells = []
        for n, uids in enumerate(row):
            if shift(cohort, n, unit) > today:
                cells.append(f"{'':<10}")  # period hasn't started yet
            else:
                cells.append(f"{f'{len(uids)} {round(100 * len(uids) / size)}%':<10}")
        print(f"{cohort.isoformat():<12}{size:>6}  " + "".join(cells))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("app_id", help="App id from the dashboard URL")
    parser.add_argument("--url", default="http://localhost:8123", help="ClickHouse HTTP URL")
    parser.add_argument("--user", default="aptabase", help="ClickHouse user")
    parser.add_argument("--password", default="aptabase_pw", help="ClickHouse password")
    parser.add_argument("--players", type=int, default=150, help="Players with a persistent user ID")
    parser.add_argument("--anonymous", type=int, default=30, help="Players without a persistent user ID, must not be counted")
    parser.add_argument("--history", type=int, default=120, help="Days of history to generate")
    parser.add_argument("--debug", action="store_true", help="Seed debug events (the dashboard's Debug mode) instead of release")
    parser.add_argument("--clear", action="store_true", help="Remove events seeded by an earlier run for this app first")
    parser.add_argument("--seed", type=int, default=None, help="Random seed, the same seed and day give the same players")
    args = parser.parse_args()

    rng = random.Random(args.seed)
    app_id = f"{args.app_id}_DEBUG" if args.debug else args.app_id
    now = datetime.now(timezone.utc).replace(microsecond=0)
    today = now.date()

    if args.clear:
        clickhouse(args.url, args.user, args.password,
                   f"ALTER TABLE events DELETE WHERE app_id = '{app_id}' AND sdk_version = '{SDK_VERSION}' SETTINGS mutations_sync = 1")

    # Each player has a first day and a "stickiness" that fades over time, so later periods retain fewer players
    players: dict[str, set[date]] = {}
    anonymous: list[set[date]] = []
    for i in range(args.players + args.anonymous):
        first_day = today - timedelta(days=rng.randint(0, args.history))
        stickiness = rng.uniform(0.05, 0.7)
        days = {first_day}
        for n in range(1, (today - first_day).days + 1):
            if rng.random() < stickiness * 0.97 ** n:
                days.add(first_day + timedelta(days=n))
        if i < args.players:
            players[persistent_id(f"player-{i}")] = days
        else:
            anonymous.append(days)

    rows = []
    ttl = DEBUG_TTL if args.debug else RELEASE_TTL

    def add_day(day: date, uid: str | None) -> None:
        # 1 to 3 sessions per active day with a few events each, never in the future
        latest = now if day == today else datetime.combine(day, time(23, 59, 59), timezone.utc)
        user_id = hashlib.sha256(f"{uid}-{day}-{rng.random()}".encode()).hexdigest()[:40]
        for _ in range(rng.randint(1, 3)):
            started_at = datetime.combine(day, time(0), timezone.utc) + timedelta(seconds=rng.randint(0, int((latest - datetime.combine(day, time(0), timezone.utc)).total_seconds())))
            session_id = str(int(started_at.timestamp()) * 100_000_000 + rng.randint(0, 99_999_999))
            timestamp = started_at
            for name in ["app_started"] + [rng.choice(EVENT_NAMES) for _ in range(rng.randint(0, 4))]:
                if timestamp > latest:
                    break
                rows.append({
                    "app_id": app_id,
                    "timestamp": timestamp.strftime("%Y-%m-%d %H:%M:%S"),
                    "event_name": name,
                    "user_id": user_id,
                    "session_id": session_id,
                    "persistent_user_id": uid,
                    "os_name": "Windows",
                    "os_version": "10.0.22631",
                    "locale": "en-US",
                    "app_version": "1.2.0",
                    "engine_name": "Godot",
                    "engine_version": "4.3",
                    "sdk_version": SDK_VERSION,
                    "country_code": "NL",
                    "string_props": "{}",
                    "numeric_props": "{}",
                    "ttl": (timestamp + ttl).strftime("%Y-%m-%d %H:%M:%S"),
                })
                timestamp += timedelta(seconds=rng.randint(5, 90))

    for uid, days in players.items():
        for day in days:
            add_day(day, uid)
    for days in anonymous:
        for day in days:
            add_day(day, None)

    for start in range(0, len(rows), 10_000):
        body = "\n".join(json.dumps(row) for row in rows[start:start + 10_000]).encode()
        clickhouse(args.url, args.user, args.password, "INSERT INTO events FORMAT JSONEachRow", body)

    print(f"Inserted {len(rows)} {'debug' if args.debug else 'release'} events for {args.players} persistent "
          f"and {args.anonymous} anonymous players over {args.history} days into app {app_id}.")

    print_expected(players, today, "day", cohorts=30, periods=14)
    print_expected(players, today, "week", cohorts=12, periods=8)
    print_expected(players, today, "month", cohorts=4, periods=3)


if __name__ == "__main__":
    main()
