#!/usr/bin/env bash
# Deploy only the backend image (api, worker, migrate): build cato-backend, push it under the
# backend's short commit, run cato-migrate, then roll api and worker. The frontends, OGT, Caddy
# and the agents keep their images. deploy.sh builds every image, OGT from its sibling checkout.
#
#   infra/phase2/deploy-backend.sh [tag]     # default tag: cato-backend's short commit
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
docker build --platform linux/amd64 -f "$BACKEND/Dockerfile" -t "$ECR/cato-backend:$TAG" "$BACKEND"
docker push "$ECR/cato-backend:$TAG"

cd "$HERE"
BACKUP=$(mktemp)
cp images.auto.tfvars "$BACKUP"
trap 'cp "$BACKUP" images.auto.tfvars' ERR
python3 scripts/set_image_tag.py cato-backend "$TAG"
terraform fmt -list=false images.auto.tfvars
terraform apply -input=false -auto-approve -target=aws_ecs_task_definition.migrate
scripts/run-migrate.sh
# api and worker to the new image; the worker stops its old task first.
terraform apply -input=false -auto-approve
echo "deployed cato-backend:$TAG; commit infra/phase2/images.auto.tfvars"
