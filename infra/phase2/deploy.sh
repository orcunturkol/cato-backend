#!/usr/bin/env bash
# Deploy to ECS: build the images (linux/amd64), push them to ECR under one tag, run
# cato-migrate and wait for exit 0, then roll every service to that tag with Terraform.
#
#   infra/phase2/deploy.sh [tag]     # default tag: cato-backend's short commit
#
# Needs docker, the AWS CLI login (or an instance role) and terraform. The repos sit next to
# cato-backend, as compose expects (../cato-frontend, ../ogt-backend, ...); override a path
# with CATO_FRONTEND_DIR, COLLECTOR_DIR, OGT_API_DIR or OGT_FRONTEND_DIR.
set -euo pipefail

REGION=eu-central-1
HERE=$(cd "$(dirname "$0")" && pwd)
BACKEND=$(cd "$HERE/../.." && pwd)
SIBLINGS=$(dirname "$BACKEND")
CATO_FRONTEND_DIR=${CATO_FRONTEND_DIR:-$SIBLINGS/cato-frontend}
COLLECTOR_DIR=${COLLECTOR_DIR:-$SIBLINGS/catoptric-data-collector}
OGT_API_DIR=${OGT_API_DIR:-$SIBLINGS/ogt-backend}
OGT_FRONTEND_DIR=${OGT_FRONTEND_DIR:-$SIBLINGS/ogt-frontend}
TAG=${1:-$(git -C "$BACKEND" rev-parse --short HEAD)}

ACCOUNT=$(aws sts get-caller-identity --query Account --output text)
ECR=$ACCOUNT.dkr.ecr.$REGION.amazonaws.com
aws ecr get-login-password --region "$REGION" | docker login --username AWS --password-stdin "$ECR"

build() { # <context dir> <ECR repo> <Dockerfile> [docker build args...]
  local dir=$1 repo=$2 file=$3
  shift 3
  docker build --platform linux/amd64 -f "$dir/$file" -t "$ECR/$repo:$TAG" "$@" "$dir"
  docker push "$ECR/$repo:$TAG"
}

build "$BACKEND" cato-backend Dockerfile
build "$BACKEND" cato-caddy Dockerfile.caddy
build "$CATO_FRONTEND_DIR" cato-frontend Dockerfile --build-arg VITE_API_URL=https://catoptricgames.duckdns.org/api
build "$COLLECTOR_DIR" cato-collector Dockerfile
build "$OGT_API_DIR" ogt-api Dockerfile
build "$OGT_FRONTEND_DIR" ogt-frontend Dockerfile --build-arg VITE_API_URL=

cd "$HERE"
# The migrate task definition first, so it carries the new image.
terraform apply -input=false -auto-approve -var "image_tag=$TAG" -var 'image_tags={}' -target=aws_ecs_task_definition.migrate

scripts/run-migrate.sh

# Every service to the new tag. The worker and the agents stop their old task first.
terraform apply -input=false -auto-approve -var "image_tag=$TAG" -var 'image_tags={}'
python3 scripts/set_image_tag.py --all "$TAG"
terraform fmt -list=false images.auto.tfvars
echo "deployed $TAG; commit infra/phase2/images.auto.tfvars"
