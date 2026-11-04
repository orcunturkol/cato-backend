#!/usr/bin/env bash
# Deploy only the OGT backup image: build ogt-backup/, push it under the short commit that last
# changed that directory and point the cato-ogt-backup task definition (and its schedule) at it.
# The first run also creates the repository first: the image needs it before the push. After the
# first deploy, run the task once by hand (README.md, "OGT backup"), so its alarm turns OK.
#
#   infra/phase2/deploy-ogt-backup.sh [tag]     # default tag: the last commit of ogt-backup/
set -euo pipefail

REGION=eu-central-1
HERE=$(cd "$(dirname "$0")" && pwd)
if [ -n "$(git -C "$HERE" status --porcelain -- ogt-backup)" ]; then
  echo "$HERE/ogt-backup has uncommitted changes: the image would not match the tag" >&2
  exit 1
fi
TAG=${1:-$(git -C "$HERE" log -1 --format=%h -- ogt-backup)}

cd "$HERE"
# No change once the repository exists.
terraform apply -input=false -auto-approve -target='aws_ecr_repository.this["ogt-backup"]'

ACCOUNT=$(aws sts get-caller-identity --query Account --output text)
ECR=$ACCOUNT.dkr.ecr.$REGION.amazonaws.com
aws ecr get-login-password --region "$REGION" | docker login --username AWS --password-stdin "$ECR"
docker build --platform linux/amd64 -t "$ECR/ogt-backup:$TAG" ogt-backup
docker push "$ECR/ogt-backup:$TAG"

BACKUP=$(mktemp)
cp images.auto.tfvars "$BACKUP"
trap 'cp "$BACKUP" images.auto.tfvars' ERR
python3 scripts/set_image_tag.py ogt-backup "$TAG"
terraform fmt -list=false images.auto.tfvars
# The other tags come from images.auto.tfvars, so only the backup's task definition and
# schedule change (the first time also its bucket, roles and alarms).
terraform apply -input=false -auto-approve
echo "deployed ogt-backup:$TAG; commit infra/phase2/images.auto.tfvars"
