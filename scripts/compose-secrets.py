#!/usr/bin/env python3
"""Write each compose service's secret environment to /run/cato/<service>.env.

The values live in the Secrets Manager secret cato/server/compose-env, a JSON object
{service: {NAME: value}} (Caddy's DuckDNS token, Seq's first-run password, OGT's database
login and invite code). /run is tmpfs, so the files exist in memory only, readable by their
owner; docker compose reads them through env_file when it creates a container.
cato-compose-secrets.service runs this at boot; run it again after a rotation, then
recreate the service. Prints service and variable names, never a value.
"""

import json
import os
import re
import sys

import boto3

SECRET_ID = os.environ.get("CATO_COMPOSE_SECRET", "cato/server/compose-env")
OUT_DIR = os.environ.get("CATO_COMPOSE_SECRET_DIR", "/run/cato")
SERVICE = re.compile(r"[a-z0-9-]+")
NAME = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")


def main() -> int:
    region = os.environ.get("AWS_REGION", "eu-central-1")
    raw = boto3.client("secretsmanager", region_name=region).get_secret_value(SecretId=SECRET_ID)["SecretString"]
    services = json.loads(raw)
    os.makedirs(OUT_DIR, mode=0o700, exist_ok=True)
    for service, env in services.items():
        if not SERVICE.fullmatch(service) or not all(NAME.fullmatch(k) for k in env):
            print(f"[fail] {service!r}: bad service or variable name", file=sys.stderr)
            return 1
        # Single quotes keep a value literal in compose's env_file (no $ interpolation).
        if any("'" in v or "\n" in v for v in env.values()):
            print(f"[fail] {service}: a value holds a single quote or a line break", file=sys.stderr)
            return 1
        path = os.path.join(OUT_DIR, f"{service}.env")
        fd = os.open(path + ".tmp", os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            f.writelines(f"{k}='{v}'\n" for k, v in env.items())
        os.replace(path + ".tmp", path)
        print(f"[ok]   {path}: {', '.join(sorted(env))}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
