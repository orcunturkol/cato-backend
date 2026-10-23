#!/usr/bin/env bash
# Run the cato-migrate task once and fail unless it exits 0. Its task definition must already
# carry the image to migrate with. Used by deploy.sh and deploy-backend.sh, from infra/phase2.
set -euo pipefail

REGION=eu-central-1
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
