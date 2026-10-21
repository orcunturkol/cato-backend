#!/usr/bin/env python3
"""Put a new value into a CATO secret without showing it.

    python3 infra/phase1/scripts/set_secret.py cato/collector/config WEBSHARE_PROXY_PASSWORD
    python3 infra/phase1/scripts/set_secret.py cato/backend/config SteamKit:Password
    python3 infra/phase1/scripts/set_secret.py cato/alerts/discord-webhook
    python3 infra/phase1/scripts/set_secret.py cato/server/compose-env caddy/DUCKDNS_TOKEN

With a key, the secret is a JSON object and only that key changes ("service/NAME" for the
nested cato/server/compose-env); without one, the whole value is replaced. The value is read with a hidden prompt (twice), never from the command
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
        *parents, name = key.split("/")
        node = data
        for part in parents:
            node = node.get(part) if isinstance(node, dict) else None
        # Only an existing text value: replacing a whole object would break its reader.
        if not isinstance(node, dict) or not isinstance(node.get(name), str):
            paths = [f"{k}/{n}" if isinstance(v, dict) else k
                     for k, v in data.items() for n in (v if isinstance(v, dict) else [None])]
            print(f"{secret_id} has no text value at {key}; keys: {sorted(paths)}")
            return 1
        node[name] = value
        new = json.dumps(data)

    version = client.put_secret_value(SecretId=secret_id, SecretString=new)["VersionId"]
    print(f"Updated {secret_id}{' ' + key if key else ''} (version {version[:8]}).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
