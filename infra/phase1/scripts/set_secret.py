#!/usr/bin/env python3
"""Put a new value into a CATO secret without showing it.

    python3 infra/phase1/scripts/set_secret.py cato/collector/config WEBSHARE_PROXY_PASSWORD
    python3 infra/phase1/scripts/set_secret.py cato/backend/config SteamKit:Password
    python3 infra/phase1/scripts/set_secret.py cato/alerts/discord-webhook

With a key, the secret is a JSON object and only that key changes; without one, the whole
value is replaced. The value is read with a hidden prompt (twice), never from the command
line, so it stays out of the shell history and the screen. Needs the AWS CLI login of the
account (or the server's instance role) and Python with boto3. Secrets Manager keeps the
previous value as AWSPREVIOUS, so a mistake can be undone.
"""
import getpass
import json
import sys

import boto3

REGION = "eu-central-1"


def main() -> int:
    if len(sys.argv) not in (2, 3):
        print(__doc__)
        return 2
    secret_id = sys.argv[1]
    key = sys.argv[2] if len(sys.argv) == 3 else None
    if not secret_id.startswith("cato/"):
        print("Only cato/* secrets.")
        return 2

    value = getpass.getpass(f"New value for {secret_id}{' ' + key if key else ''}: ")
    if not value or value != getpass.getpass("Again: "):
        print("Empty or not the same; nothing changed.")
        return 1

    client = boto3.client("secretsmanager", region_name=REGION)
    if key is None:
        new = value
    else:
        current = client.get_secret_value(SecretId=secret_id).get("SecretString") or "{}"
        data = json.loads(current)
        if key not in data:
            print(f"{secret_id} has no key {key}; keys: {sorted(data)}")
            return 1
        data[key] = value
        new = json.dumps(data)

    version = client.put_secret_value(SecretId=secret_id, SecretString=new)["VersionId"]
    print(f"Updated {secret_id}{' ' + key if key else ''} (version {version[:8]}).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
