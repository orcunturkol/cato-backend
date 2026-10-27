#!/usr/bin/env bash
# Deploy only OGT: build ogt-api and ogt-frontend from their checkouts, push each under its
# repo's short commit and roll the two OGT services. The CATO images keep their tags; deploy.sh
# still builds every image, OGT too. ogt-api migrates its own database when it starts.
# Deploy outside 14:00-15:00 UTC: OGT's daily watchlist check runs then, and the rollout runs
# two ogt-api tasks for a moment.
#
#   OGT_API_DIR=... OGT_FRONTEND_DIR=... infra/phase2/deploy-ogt.sh
#   (default: ../ogt-backend and ../ogt-frontend next to cato-backend)
set -euo pipefail

REGION=eu-central-1
HERE=$(cd "$(dirname "$0")" && pwd)
SIBLINGS=$(dirname "$(cd "$HERE/../.." && pwd)")
OGT_API_DIR=${OGT_API_DIR:-$SIBLINGS/ogt-backend}
OGT_FRONTEND_DIR=${OGT_FRONTEND_DIR:-$SIBLINGS/ogt-frontend}
for dir in "$OGT_API_DIR" "$OGT_FRONTEND_DIR"; do
  if [ -n "$(git -C "$dir" status --porcelain)" ]; then
    echo "$dir has uncommitted changes: the image would not match the tag" >&2
    exit 1
  fi
done
API_TAG=$(git -C "$OGT_API_DIR" rev-parse --short HEAD)
FRONTEND_TAG=$(git -C "$OGT_FRONTEND_DIR" rev-parse --short HEAD)

ACCOUNT=$(aws sts get-caller-identity --query Account --output text)
ECR=$ACCOUNT.dkr.ecr.$REGION.amazonaws.com
aws ecr get-login-password --region "$REGION" | docker login --username AWS --password-stdin "$ECR"
docker build --platform linux/amd64 -f "$OGT_API_DIR/Dockerfile" -t "$ECR/ogt-api:$API_TAG" "$OGT_API_DIR"
docker push "$ECR/ogt-api:$API_TAG"
# An empty VITE_API_URL keeps the API calls on the page's own origin (/api), as deploy.sh builds it.
docker build --platform linux/amd64 -f "$OGT_FRONTEND_DIR/Dockerfile" -t "$ECR/ogt-frontend:$FRONTEND_TAG" \
  --build-arg VITE_API_URL= "$OGT_FRONTEND_DIR"
docker push "$ECR/ogt-frontend:$FRONTEND_TAG"

cd "$HERE"
BACKUP=$(mktemp)
cp images.auto.tfvars "$BACKUP"
trap 'cp "$BACKUP" images.auto.tfvars' ERR
python3 scripts/set_image_tag.py ogt-api "$API_TAG"
python3 scripts/set_image_tag.py ogt-frontend "$FRONTEND_TAG"
terraform fmt -list=false images.auto.tfvars
# The other tags come from images.auto.tfvars, so only the two OGT task definitions change.
terraform apply -input=false -auto-approve
echo "deployed ogt-api:$API_TAG and ogt-frontend:$FRONTEND_TAG; commit infra/phase2/images.auto.tfvars"
