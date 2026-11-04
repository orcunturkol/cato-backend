#!/usr/bin/env bash
# Deploy only Caddy: build cato-caddy (Dockerfile.caddy with Caddyfile.ecs), push it under
# cato-backend's short commit and roll the caddy service on the edge node. The other images keep
# their tags. Caddy is the only entry point: https stops for the seconds the task needs to restart.
#
#   infra/phase2/deploy-caddy.sh [tag]     # default tag: cato-backend's short commit
set -euo pipefail

REGION=eu-central-1
HERE=$(cd "$(dirname "$0")" && pwd)
BACKEND=$(cd "$HERE/../.." && pwd)
TAG=${1:-$(git -C "$BACKEND" rev-parse --short HEAD)}
if [ -n "$(git -C "$BACKEND" status --porcelain)" ]; then
  echo "$BACKEND has uncommitted changes: the image would not match the tag" >&2
  exit 1
fi

ACCOUNT=$(aws sts get-caller-identity --query Account --output text)
ECR=$ACCOUNT.dkr.ecr.$REGION.amazonaws.com
aws ecr get-login-password --region "$REGION" | docker login --username AWS --password-stdin "$ECR"
docker build --platform linux/amd64 -f "$BACKEND/Dockerfile.caddy" -t "$ECR/cato-caddy:$TAG" "$BACKEND"
docker push "$ECR/cato-caddy:$TAG"

cd "$HERE"
BACKUP=$(mktemp)
cp images.auto.tfvars "$BACKUP"
trap 'cp "$BACKUP" images.auto.tfvars' ERR
python3 scripts/set_image_tag.py cato-caddy "$TAG"
terraform fmt -list=false images.auto.tfvars
# The other tags come from images.auto.tfvars, so only Caddy's task definition changes.
terraform apply -input=false -auto-approve
echo "deployed cato-caddy:$TAG; commit infra/phase2/images.auto.tfvars"
