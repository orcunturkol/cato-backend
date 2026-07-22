#!/usr/bin/env bash
set -euo pipefail

# Cuts CATO over from the local Postgres container to AWS RDS.
#
# Run this ON THE catoptric-games EC2 HOST, from the cato-backend repo root,
# during the agreed maintenance window. See CUTOVER_RUNBOOK.md for the full
# procedure this script is one part of.
#
# Required env vars:
#   RDS_HOST         terraform output rds_address
#   RDS_DB_PASSWORD  aws ssm get-parameter --name /cato/rds/db_password \
#                      --with-decryption --query Parameter.Value --output text
# Optional env vars:
#   DUMP_FILE  (default /tmp/cato.dump)
#   DB_NAME    (default cato)
#   DB_USER    (default cato_user)

: "${RDS_HOST:?Set RDS_HOST to the RDS endpoint}"
: "${RDS_DB_PASSWORD:?Set RDS_DB_PASSWORD from the SSM parameter}"

DUMP_FILE="${DUMP_FILE:-/tmp/cato.dump}"
DB_NAME="${DB_NAME:-cato}"
DB_USER="${DB_USER:-cato_user}"

echo "==> Stopping the api container so no new writes land during the copy"
docker compose stop api

echo "==> Dumping ${DB_NAME} from the local postgres container to ${DUMP_FILE}"
docker exec cato-postgres pg_dump -U "${DB_USER}" -d "${DB_NAME}" \
  --format=custom --no-owner --no-privileges -f "${DUMP_FILE}"

echo "==> Row counts in the SOURCE (local) database"
docker exec -i cato-postgres psql -U "${DB_USER}" -d "${DB_NAME}" \
  < infra/rds/scripts/row-counts.sql

echo "==> Restoring into RDS at ${RDS_HOST}"
docker run --rm -i --network host \
  -e PGPASSWORD="${RDS_DB_PASSWORD}" \
  -v "${DUMP_FILE}:${DUMP_FILE}:ro" \
  postgres:16 \
  pg_restore -h "${RDS_HOST}" -p 5432 -U "${DB_USER}" -d "${DB_NAME}" \
    --no-owner --no-privileges --clean --if-exists "${DUMP_FILE}"

echo "==> Row counts in the TARGET (RDS) database"
docker run --rm -i --network host \
  -e PGPASSWORD="${RDS_DB_PASSWORD}" \
  -v "$(pwd)/infra/rds/scripts/row-counts.sql:/tmp/row-counts.sql:ro" \
  postgres:16 \
  psql -h "${RDS_HOST}" -p 5432 -U "${DB_USER}" -d "${DB_NAME}" -f /tmp/row-counts.sql

echo
echo "==> Compare the two row-count blocks above, table by table, before continuing."
echo "==> If (and only if) they match: add these two lines to .env, then restart api:"
echo "        RDS_HOST=${RDS_HOST}"
echo '        RDS_DB_PASSWORD=<value you already have set>'
echo "        docker compose up -d api"
echo "==> Smoke-test the app before removing the old postgres container (see CUTOVER_RUNBOOK.md)."
