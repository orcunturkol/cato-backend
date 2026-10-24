#!/usr/bin/env bash
# Deploy only the CATO frontend: build cato-frontend, push it under the frontend's short commit
# and roll the frontend service. The backend, collector, Caddy and OGT images keep their tags.
# deploy.sh still builds every image, the frontend too.
#
#   infra/phase2/deploy-frontend.sh [tag]     # default tag: the frontend's short commit
set -euo pipefail

REGION=eu-central-1
HERE=$(cd "$(dirname "$0")" && pwd)
CATO_FRONTEND_DIR=${CATO_FRONTEND_DIR:-$(dirname "$(cd "$HERE/../.." && pwd)")/cato-frontend}
TAG=${1:-$(git -C "$CATO_FRONTEND_DIR" rev-parse --short HEAD)}
if [ -n "$(git -C "$CATO_FRONTEND_DIR" status --porcelain)" ]; then
  echo "$CATO_FRONTEND_DIR has uncommitted changes: the image would not match the tag" >&2
  exit 1
fi

ACCOUNT=$(aws sts get-caller-identity --query Account --output text)
ECR=$ACCOUNT.dkr.ecr.$REGION.amazonaws.com
aws ecr get-login-password --region "$REGION" | docker login --username AWS --password-stdin "$ECR"
docker build --platform linux/amd64 -f "$CATO_FRONTEND_DIR/Dockerfile" -t "$ECR/cato-frontend:$TAG" \
  --build-arg VITE_API_URL=https://catoptricgames.duckdns.org/api "$CATO_FRONTEND_DIR"
docker push "$ECR/cato-frontend:$TAG"

cd "$HERE"
BACKUP=$(mktemp)
cp images.auto.tfvars "$BACKUP"
trap 'cp "$BACKUP" images.auto.tfvars' ERR
python3 scripts/set_image_tag.py cato-frontend "$TAG"
terraform fmt -list=false images.auto.tfvars
# The other tags come from images.auto.tfvars, so only the frontend's task definition changes.
terraform apply -input=false -auto-approve
echo "deployed cato-frontend:$TAG; commit infra/phase2/images.auto.tfvars"
