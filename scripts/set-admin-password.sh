#!/usr/bin/env bash
# Sets the password Caddy asks for on CATO write actions (user "cato"): schedule edits,
# run now, pause, the test alert and the Steamworks login form. Run it on the server:
#
#   cd ~/cato-backend && bash scripts/set-admin-password.sh
#
# The password is read twice with a hidden prompt. Only its bcrypt hash (base64) is
# written to .env as CATO_ADMIN_HASH; then Caddy is re-created to load it. The browser
# asks for the user and password on the first write and keeps them for the session.
# Note: the password is passed to `caddy hash-password` inside the Caddy container for
# about a second, so it is briefly visible in the process list of this host.
set -euo pipefail
cd "$(dirname "$0")/.."

read -rsp "New CATO admin password: " pw; echo
read -rsp "Again: " again; echo
if [ -z "$pw" ] || [ "$pw" != "$again" ]; then
    echo "Empty or not the same; nothing changed."
    exit 1
fi
if [ "${#pw}" -lt 12 ]; then
    echo "Use at least 12 characters; nothing changed."
    exit 1
fi

hash=$(docker compose exec -T caddy caddy hash-password --plaintext "$pw" | tr -d '\r\n')
unset pw again
case "$hash" in
    '$2'*) ;;
    *) echo "Caddy did not return a bcrypt hash; nothing changed."; exit 1 ;;
esac
encoded=$(printf '%s' "$hash" | base64 -w0)

python3 - "$encoded" <<'PY'
import os, sys
encoded = sys.argv[1]
lines = open(".env", encoding="utf-8").read().splitlines(keepends=True) if os.path.exists(".env") else []
lines = [l for l in lines if l.split("=", 1)[0].strip() != "CATO_ADMIN_HASH"]
if lines and not lines[-1].endswith("\n"):
    lines[-1] += "\n"
lines.append(f"CATO_ADMIN_HASH={encoded}\n")
fd = os.open(".env.tmp", os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
with os.fdopen(fd, "w", encoding="utf-8") as f:
    f.writelines(lines)
os.replace(".env.tmp", ".env")
PY

docker compose up -d caddy
echo "Done. Write actions now ask for user 'cato' and this password."
