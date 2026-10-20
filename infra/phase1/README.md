# Phase 1 AWS stack

The AWS parts that today's EC2 host needs before the move to ECS:

| Resource | Purpose |
|---|---|
| SNS topic `cato-alerts` + email subscription | critical job alerts by email |
| Secret `cato/alerts/discord-webhook` | Discord webhook for the same alerts |
| IAM role and instance profile `cato-ec2-host` | lets the host publish alerts, read and rotate secrets under `cato/*`, and send `CATO` metrics |

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

Attach the role to the EC2 host (no restart), and allow one extra metadata hop so
Docker containers on the host can use the role too:

```sh
aws ec2 associate-iam-instance-profile --region eu-central-1 \
  --instance-id i-0b69760b8c49c78d1 --iam-instance-profile Name=cato-ec2-host
aws ec2 modify-instance-metadata-options --region eu-central-1 \
  --instance-id i-0b69760b8c49c78d1 --http-tokens required --http-put-response-hop-limit 2
```
