# Phase 1 AWS stack

The alerting parts, created for the old EC2 host and still used by the ECS worker
(`infra/phase2`):

| Resource | Purpose |
|---|---|
| SNS topic `cato-alerts` + email subscription | critical job alerts by email |
| Secret `cato/alerts/discord-webhook` | Discord webhook for the same alerts |
| CloudWatch alarm `cato-worker-heartbeat` | e-mails when the worker sends no `CATO/WorkerHeartbeat` for five minutes |

The old host's IAM role and instance profile `cato-ec2-host` were removed after the host was
terminated (2026-10-28); the ECS tasks have their own task roles in `infra/phase2`.

State lives in S3 (`cato-terraform-state-657566734223`, key `phase1/terraform.tfstate`)
with S3 native locking, so it does not depend on one machine.

## First time only: the state bucket

```sh
B=cato-terraform-state-657566734223
aws s3api create-bucket --bucket $B --region eu-central-1 \
  --create-bucket-configuration LocationConstraint=eu-central-1
aws s3api put-bucket-versioning --bucket $B --versioning-configuration Status=Enabled
aws s3api put-bucket-encryption --bucket $B --server-side-encryption-configuration \
  '{"Rules":[{"ApplyServerSideEncryptionByDefault":{"SSEAlgorithm":"AES256"}}]}'
aws s3api put-public-access-block --bucket $B --public-access-block-configuration \
  BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true
```

## Apply

```sh
terraform init
terraform plan -out phase1.plan
terraform apply phase1.plan
```

Then confirm the subscription: AWS sends an email to the alert address, and alerts
arrive only after its link is clicked.

## Steps outside Terraform

Set the Discord webhook from a file, so the URL appears in no command line or state:

```sh
aws secretsmanager put-secret-value --region eu-central-1 \
  --secret-id cato/alerts/discord-webhook --secret-string file://webhook.txt
```

The ECS worker turns the alerts on through its task env (`Alerts__*` in `backend_env`,
`infra/phase2/services.tf`), not a `.env` file. `systemd/cato-compose-secrets.service` and
`cato/server/compose-env` belonged to the old host's compose services.

**Apply the heartbeat alarm only after the worker sends the metric.** Until the first
`CATO/WorkerHeartbeat` data point exists, "missing data = breaching" alarms at once.
Check with `aws cloudwatch list-metrics --namespace CATO`, then
`terraform apply -target=aws_cloudwatch_metric_alarm.worker_heartbeat`.
