#!/usr/bin/env bash
# Sets the password Caddy asks for on CATO write actions (user "cato"): schedule edits,
# run now, pause, the test alert and the Steamworks login form. Since Step D3 (2026-10-23)
# Caddy runs on the ECS edge node and reads the hash from the secret cato/caddy/admin-hash.
# Run it anywhere with Docker and the AWS CLI login:
#
#   bash scripts/set-admin-password.sh
#
# The password is read twice with a hidden prompt. Only its bcrypt hash (base64) is stored;
# then the caddy service starts a new task to load it. The browser asks for the user and
# password on the first write and keeps them for the session.
# Note: the password is passed to `caddy hash-password` in a throwaway container for about a
# second, so it is briefly visible in the process list of this machine.
set -euo pipefail

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

hash=$(docker run --rm caddy:2-alpine caddy hash-password --plaintext "$pw" | tr -d '\r\n')
unset pw again
case "$hash" in
    '$2'*) ;;
    *) echo "Caddy did not return a bcrypt hash; nothing changed."; exit 1 ;;
esac

file=$(mktemp)
trap 'rm -f "$file"' EXIT
printf '%s' "$hash" | base64 -w0 > "$file"
aws secretsmanager put-secret-value --region eu-central-1 --secret-id cato/caddy/admin-hash \
    --secret-string "file://$file" >/dev/null
aws ecs update-service --region eu-central-1 --cluster cato --service caddy \
    --force-new-deployment >/dev/null
echo "Done. In about a minute write actions ask for user 'cato' and this password."
