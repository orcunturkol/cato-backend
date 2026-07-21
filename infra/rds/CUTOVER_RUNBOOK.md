# CATO → RDS Cutover Runbook

Run every step below yourself, on the `catoptric-games` EC2 host unless noted
otherwise. Nothing in `infra/rds/` runs `terraform apply` or touches
production automatically — that's intentional.

## 1. Provision RDS (no downtime — do this any time before the cutover window)

From `cato-backend/infra/rds/` (needs AWS credentials configured, e.g. via
`aws configure`):

```bash
terraform init
terraform plan
terraform apply
```

Review the plan before typing `yes`. This takes ~10-15 minutes and does not
touch the running app. Note the outputs:

```bash
terraform output rds_address
terraform output db_password_ssm_parameter
```

## 2. Fetch the generated password

```bash
aws ssm get-parameter \
  --name "$(terraform output -raw db_password_ssm_parameter)" \
  --with-decryption --query Parameter.Value --output text
```

Keep this somewhere secure for the next steps — do not paste it into a chat
session or commit it anywhere.

## 3. (Optional) Rehearse the copy with no downtime

You can run `infra/rds/scripts/cutover.sh` once ahead of the real cutover to
time it and catch schema/extension issues early — it's safe to run more than
once since `pg_restore --clean --if-exists` drops and recreates objects each
time. The only downside is it does stop and restart the `api` container, so
only do this rehearsal during a moment that's already low-traffic, not
mid-cutover-window twice.

## 4. Cutover

From the `cato-backend` repo root on the EC2 host:

```bash
export RDS_HOST=$(cd infra/rds && terraform output -raw rds_address)
export RDS_DB_PASSWORD='<paste the password from step 2>'
./infra/rds/scripts/cutover.sh
```

The script stops `api`, dumps the local DB, restores it into RDS, and prints
row counts for both databases side by side. **Read that output and confirm
every table's count matches before continuing.**

If they match:

```bash
# Add to .env on the host:
echo "RDS_HOST=${RDS_HOST}" >> .env
echo "RDS_DB_PASSWORD=${RDS_DB_PASSWORD}" >> .env

docker compose up -d api
```

## 5. Smoke test

- Hit `/swagger` and a couple of real endpoints.
- Check Seq (`http://<host>:5341`) for connection or query errors.
- Confirm the app's own startup migration check
  (`db.Database.Migrate()` in `Program.cs`) finds no pending migrations —
  the dump already included the EF migrations history table, so this should
  be a no-op. If you want to check explicitly:
  `dotnet ef migrations list --project src/Cato.Infrastructure --startup-project src/Cato.API`
  (run against `RDS_HOST` via the same `ConnectionStrings__DefaultConnection`).
- Confirm the security group actually restricts access: from a machine that
  is NOT the EC2 instance, attempt `psql -h <rds-address> -U cato_user -d cato`
  and confirm it fails to connect (times out / connection refused), proving
  the RDS security group is doing its job.

## 6. Rollback

At any point before the old container/volume is removed (step 7), rollback
is just:

```bash
# Remove or comment out the two RDS_* lines added to .env, then:
docker compose up -d api
```

The original `postgres` container and its data volume are never modified or
deleted during cutover — only read from — so this always gets you back to
exactly where you started.

## 7. Cleanup (after RDS has run stable for a few days)

Remove the `postgres` and `postgres-cert-init` services (and their
`cato_pgdata` / `cato_pg_ssl` volumes) from `docker-compose.yml`, and drop the
now-unused `depends_on: postgres: condition: service_healthy` entry from the
`api` service. The `pgadmin` service has the same
`depends_on: postgres: condition: service_healthy` block — drop that entry
too. If you're keeping `pgadmin` around for RDS access, also update or remove
`pgadmin-servers.json`: it still pre-configures a server pointing at the
removed local database (`Host: postgres`, `Password: cato_dev_password`),
which will no longer resolve once the `postgres` container is gone.
