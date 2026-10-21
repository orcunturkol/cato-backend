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
terraform apply -input=false -auto-approve -var "image_tag=$TAG" -target=aws_ecs_task_definition.migrate

SUBNETS=$(terraform output -json subnet_ids | python3 -c 'import json,sys; print(",".join(json.load(sys.stdin)))')
MIGRATE_SG=$(terraform output -json service_security_groups | python3 -c 'import json,sys; print(json.load(sys.stdin)["migrate"])')
TASK=$(aws ecs run-task --region "$REGION" --cluster cato --task-definition cato-migrate --launch-type FARGATE \
  --network-configuration "awsvpcConfiguration={subnets=[$SUBNETS],securityGroups=[$MIGRATE_SG],assignPublicIp=ENABLED}" \
  --query 'tasks[0].taskArn' --output text)
echo "migrate: $TASK"
aws ecs wait tasks-stopped --region "$REGION" --cluster cato --tasks "$TASK"
EXIT=$(aws ecs describe-tasks --region "$REGION" --cluster cato --tasks "$TASK" --query 'tasks[0].containers[0].exitCode' --output text)
if [ "$EXIT" != "0" ]; then
  echo "migrate exited $EXIT: services not changed (log group /cato/migrate)" >&2
  exit 1
fi

# Every service to the new tag. The worker and the agents stop their old task first.
terraform apply -input=false -auto-approve -var "image_tag=$TAG"
echo "deployed $TAG"
