#!/usr/bin/env python3
"""Send a realistic mix of test events to a local Aptabase instance.

Simulates players with a persistent user ID, anonymous players without one, and players that only
send their ID after "logging in", spread over the last 23 hours (the API rejects events older than
a day), with some sessions in the last hour so they show up in the Live View. Most sessions end with
an app_shutdown event like the SDK sends, some don't, to cover both ways the Session Timeline ends.

Usage: scripts/send-test-events.py <app key> [--url https://localhost:5251] [--seed 42] [--debug]

Events are sent as release events unless --debug is passed, pick the matching mode in the dashboard.
"""

import argparse
import hashlib
import json
import random
import ssl
import time
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone

EVENT_NAMES = ["level_started", "level_completed", "ball_dropped", "settings_opened", "shop_opened"]
APP_VERSIONS = ["1.0.0", "1.1.0", "1.2.0"]
OS_LIST = [("Windows", "10.0.19045"), ("Windows", "10.0.22631"), ("Linux", "6.8"), ("macOS", "14.5")]
SHUTDOWN_CHANCE = 0.8  # finished sessions that end with app_shutdown
SHUTDOWN_CHANCE_LIVE = 0.4  # sessions in the last hour that were already closed


def persistent_id(name: str) -> str:
    # Same format the game sends: a 64 character hex SHA-256 hash
    return hashlib.sha256(f"aptabase-test-{name}".encode()).hexdigest()


def random_public_ip(rng: random.Random) -> str:
    # Avoid private/reserved first octets so GeoIP resolves them to a country
    first = rng.choice([23, 31, 45, 62, 77, 81, 89, 91, 101, 110, 146, 176, 185, 201])
    return f"{first}.{rng.randint(0, 255)}.{rng.randint(0, 255)}.{rng.randint(1, 254)}"


class Device:
    def __init__(self, rng: random.Random, label: str, user_id: str | None, login_after_first_session: bool = False):
        self.label = label
        self.user_id = user_id
        self.login_after_first_session = login_after_first_session
        self.ip = random_public_ip(rng)
        self.os_name, self.os_version = rng.choice(OS_LIST)
        self.app_version = rng.choice(APP_VERSIONS)


def build_session(
    rng: random.Random, device: Device, started_at: datetime, now: datetime, user_id: str | None, debug: bool
) -> list[dict]:
    # Numeric session IDs encode their start time, like the SDKs generate them
    session_id = str(int(started_at.timestamp()) * 100_000_000 + rng.randint(0, 99_999_999))
    system_props = {
        "isDebug": debug,
        "osName": device.os_name,
        "osVersion": device.os_version,
        "locale": "en-US",
        "appVersion": device.app_version,
        "engineName": "Godot",
        "engineVersion": "4.3",
        "sdkVersion": "aptabase-test-script@1.0.0",
    }
    if user_id:
        system_props["userId"] = user_id

    names = ["app_started"] + [rng.choice(EVENT_NAMES) for _ in range(rng.randint(1, 12))]
    # Most sessions end with the SDK's app_shutdown, the rest crashed or lost their last events (older
    # sessions) or are still being played (sessions in the last hour)
    is_live = now - started_at < timedelta(hours=1)
    if rng.random() < (SHUTDOWN_CHANCE_LIVE if is_live else SHUTDOWN_CHANCE):
        names.append("app_shutdown")

    events = []
    timestamp = started_at
    for name in names:
        # Live sessions can't have events in the future, cut them off at the current time
        if timestamp > now:
            break
        props = {"level": rng.randint(1, 20)} if name.startswith("level") else {}
        events.append({
            "timestamp": timestamp.isoformat().replace("+00:00", "Z"),
            "sessionId": session_id,
            "eventName": name,
            "systemProps": system_props,
            "props": props,
        })
        timestamp += timedelta(seconds=rng.randint(5, 90))
    return events


def send(url: str, app_key: str, device: Device, events: list[dict], ctx: ssl.SSLContext) -> None:
    request = urllib.request.Request(
        f"{url}/api/v0/events",
        data=json.dumps(events).encode(),
        method="POST",
        headers={"Content-Type": "application/json", "App-Key": app_key, "X-Real-Ip": device.ip},
    )
    try:
        with urllib.request.urlopen(request, context=ctx) as response:
            response.read()
    except urllib.error.HTTPError as e:
        raise SystemExit(f"Request failed with {e.code}: {e.read().decode()}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("app_key", help="App Key of the app to send events to, e.g. A-DEV-1234567890")
    parser.add_argument("--url", default="https://localhost:5251", help="Base URL of the API")
    parser.add_argument("--persistent", type=int, default=12, help="Players with a persistent user ID")
    parser.add_argument("--anonymous", type=int, default=8, help="Players without a persistent user ID")
    parser.add_argument("--late-login", type=int, default=3, help="Players whose first session has no ID yet")
    parser.add_argument("--debug", action="store_true", help="Send debug events (the dashboard's Debug mode) instead of release")
    parser.add_argument("--seed", type=int, default=None, help="Random seed, the same seed sends the same players")
    args = parser.parse_args()

    rng = random.Random(args.seed)
    ctx = ssl._create_unverified_context()  # local dev certificate
    now = datetime.now(timezone.utc)

    devices: list[Device] = []
    for i in range(args.persistent):
        devices.append(Device(rng, f"persistent-{i}", persistent_id(f"player-{i}")))
    # A couple of persistent players also play on a second device, they should still count as one user
    for i in range(min(2, args.persistent)):
        devices.append(Device(rng, f"persistent-{i}-second-device", persistent_id(f"player-{i}")))
    for i in range(args.anonymous):
        devices.append(Device(rng, f"anonymous-{i}", None))
    for i in range(args.late_login):
        devices.append(Device(rng, f"late-login-{i}", persistent_id(f"late-login-{i}"), login_after_first_session=True))

    sessions = events = 0
    for device in devices:
        starts = sorted(now - timedelta(minutes=rng.randint(60, 23 * 60)) for _ in range(rng.randint(1, 4)))
        # About a third of the devices are also playing right now, for the Live View
        if rng.random() < 0.35:
            starts.append(now - timedelta(minutes=rng.randint(1, 50)))

        for index, started_at in enumerate(starts):
            user_id = device.user_id
            if device.login_after_first_session and index == 0:
                user_id = None
            batch = build_session(rng, device, started_at, now, user_id, args.debug)
            send(args.url, args.app_key, device, batch, ctx)
            sessions += 1
            events += len(batch)
            time.sleep(0.05)

        print(f"{device.label:<32} {device.ip:<16} {len(starts)} session(s)")

    print()
    print(f"Sent {events} {'debug' if args.debug else 'release'} events in {sessions} sessions from {len(devices)} devices:")
    print(f"  {args.persistent} persistent players ({min(2, args.persistent)} of them on two devices)")
    print(f"  {args.late_login} players that only send their ID after their first session")
    print(f"  {args.anonymous} anonymous players")
    print("Events are written to ClickHouse in the background, give it a few seconds before refreshing.")


if __name__ == "__main__":
    main()
