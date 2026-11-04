#!/bin/sh
# Dumps the OGT database only (pg_dump custom format) and uploads it to S3. The scheduler runs
# it at 01:00 UTC; the alarm cato-ogt-backup-missing watches for the "ogt-backup: ok" line.
#
#   ConnectionStrings__DefaultConnection  ogt-api's Npgsql string (Host, Port, Database,
#                                         Username, Password), injected from cato/ogt/config
#   BACKUP_BUCKET                         the bucket; the key is ogt/ogt-<UTC time>.dump
#   AWS_ENDPOINT_URL (optional)           an S3 stand-in for a local test
set -eu

on_exit() {
  rc=$?
  [ "$rc" -eq 0 ] || echo "ogt-backup: failed (exit $rc)" >&2
}
trap on_exit EXIT

: "${ConnectionStrings__DefaultConnection:?}" "${BACKUP_BUCKET:?}"

# One value of the connection string; the key is case-insensitive.
part() {
  printf '%s' "$ConnectionStrings__DefaultConnection" | tr ';' '\n' |
    grep -i "^ *$1 *=" | head -n 1 | cut -d= -f2- | sed 's/^ *//; s/ *$//'
}

PGHOST=$(part Host)
PGPORT=$(part Port)
PGDATABASE=$(part Database)
PGUSER=$(part Username)
PGPASSWORD=$(part Password)
export PGHOST PGPORT="${PGPORT:-5432}" PGDATABASE PGUSER PGPASSWORD
if [ -z "$PGHOST" ] || [ -z "$PGDATABASE" ] || [ -z "$PGUSER" ] || [ -z "$PGPASSWORD" ]; then
  echo "ogt-backup: the connection string has no Host, Database, Username or Password" >&2
  exit 1
fi

FILE=/tmp/ogt.dump
KEY="ogt/ogt-$(date -u +%Y-%m-%dT%H%MZ).dump"
pg_dump --format=custom --no-owner --no-privileges --file="$FILE"
pg_restore --list "$FILE" >/dev/null # the archive can be read back
aws s3 cp --only-show-errors "$FILE" "s3://$BACKUP_BUCKET/$KEY"
echo "ogt-backup: ok $KEY $(wc -c <"$FILE")"
