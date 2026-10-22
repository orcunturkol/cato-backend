#!/usr/bin/env bash
# Deploy only the collector (the four agents): build cato-collector, push it under the collector's
# short commit and roll the agent services. The backend images keep their tag, so the worker and
# the API do not restart. deploy.sh still builds every image, the collector too.
#
#   infra/phase2/deploy-collector.sh [tag]     # default tag: the collector's short commit
set -euo pipefail

REGION=eu-central-1
HERE=$(cd "$(dirname "$0")" && pwd)
COLLECTOR_DIR=${COLLECTOR_DIR:-$(dirname "$(cd "$HERE/../.." && pwd)")/catoptric-data-collector}
TAG=${1:-$(git -C "$COLLECTOR_DIR" rev-parse --short HEAD)}
if [ -n "$(git -C "$COLLECTOR_DIR" status --porcelain)" ]; then
  echo "$COLLECTOR_DIR has uncommitted changes: the image would not match the tag" >&2
  exit 1
fi

ACCOUNT=$(aws sts get-caller-identity --query Account --output text)
ECR=$ACCOUNT.dkr.ecr.$REGION.amazonaws.com
aws ecr get-login-password --region "$REGION" | docker login --username AWS --password-stdin "$ECR"
docker build --platform linux/amd64 -t "$ECR/cato-collector:$TAG" "$COLLECTOR_DIR"
docker push "$ECR/cato-collector:$TAG"

cd "$HERE"
# image_tag comes from images.auto.tfvars, so only the agents' task definitions change.
terraform apply -input=false -auto-approve -var "image_tags={\"cato-collector\":\"$TAG\"}"
sed -i -E "s|^image_tags = .*|image_tags = { \"cato-collector\" = \"$TAG\" }|" images.auto.tfvars
echo "deployed cato-collector:$TAG; commit infra/phase2/images.auto.tfvars"
