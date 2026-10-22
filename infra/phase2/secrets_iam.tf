# ── Secrets ──
# ECS `secrets` can read only one top-level key of a JSON secret, so the containers that get
# a secret as an environment variable (Caddy, OGT, and the broker/cache addresses) use flat
# secrets or flat keys. The backend and the collector read their own JSON secrets at start
# through the task role (cato/backend/config, cato/collector/config), as on today's host.

# Empty here; the values are set from the host with set_secret.py (a secret-store write that
# the user approves), never from this state.
resource "aws_secretsmanager_secret" "flat" {
  for_each = {
    "cato/edge/tailscale-auth-key" = "Tagged, reusable Tailscale auth key of the edge node (no key expiry)."
    "cato/caddy/duckdns-token"     = "DuckDNS token for Caddy's DNS-01 challenge (same value as cato/server/compose-env caddy/DUCKDNS_TOKEN)."
    "cato/caddy/admin-hash"        = "Base64 of the bcrypt hash of the CATO admin password (user cato) for Caddy's basic_auth on writes. Same value as CATO_ADMIN_HASH in the host's .env (scripts/set-admin-password.sh), not in cato/server/compose-env."
    "cato/ogt/db-connection"       = "ogt-api connection string (same value as cato/server/compose-env ogt-api/ConnectionStrings__DefaultConnection)."
    "cato/ogt/invite-code"         = "OGT registration invite code (same value as cato/server/compose-env ogt-api/Auth__InviteCode)."
    "cato/seq/admin-password"      = "Seq first-run admin password on the edge node (same value as cato/server/compose-env seq/SEQ_FIRSTRUN_ADMINPASSWORD)."
  }
  name                    = each.key
  description             = each.value
  recovery_window_in_days = 7
}

# Generated at apply: the broker's application login and the cache's auth token.
resource "random_password" "mq" {
  length  = 32
  special = false
}

resource "random_password" "cache" {
  length  = 32
  special = false
}

locals {
  # RabbitMQ 4.x lists an https endpoint first; take the AMQPS one.
  mq_amqps   = one([for e in aws_mq_broker.cato.instances[0].endpoints : e if startswith(e, "amqps://")])
  mq_host    = trimsuffix(trimprefix(local.mq_amqps, "amqps://"), ":5671")
  cache_host = aws_elasticache_replication_group.cato.primary_endpoint_address
}

resource "aws_secretsmanager_secret" "mq" {
  name                    = "cato/mq/app-user"
  description             = "Amazon MQ application login. uri = amqps URI for RabbitMQ:Uri (backend) and RABBITMQ_URI (collector)."
  recovery_window_in_days = 7
}

resource "aws_secretsmanager_secret_version" "mq" {
  secret_id = aws_secretsmanager_secret.mq.id
  secret_string = jsonencode({
    username = "cato"
    password = random_password.mq.result
    uri      = "amqps://cato:${random_password.mq.result}@${local.mq_host}:5671/"
  })
}

resource "aws_secretsmanager_secret" "cache" {
  name                    = "cato/cache/connection"
  description             = "Valkey auth token and connection strings: backend = Redis:ConnectionString, collector = REDIS_URL."
  recovery_window_in_days = 7
}

resource "aws_secretsmanager_secret_version" "cache" {
  secret_id = aws_secretsmanager_secret.cache.id
  secret_string = jsonencode({
    auth_token = random_password.cache.result
    backend    = "${local.cache_host}:6379,password=${random_password.cache.result},ssl=true,abortConnect=false"
    collector  = "rediss://:${random_password.cache.result}@${local.cache_host}:6379/0"
  })
}

# ── IAM ──

data "aws_iam_policy_document" "ecs_tasks_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

# Pulls images, writes logs, and reads the secrets that task definitions inject.
resource "aws_iam_role" "execution" {
  name               = "cato-ecs-execution"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_assume.json
}

resource "aws_iam_role_policy_attachment" "execution" {
  role       = aws_iam_role.execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

resource "aws_iam_role_policy" "execution_secrets" {
  name = "injected-secrets"
  role = aws_iam_role.execution.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["secretsmanager:GetSecretValue"]
      Resource = concat([for s in aws_secretsmanager_secret.flat : s.arn], [aws_secretsmanager_secret.mq.arn, aws_secretsmanager_secret.cache.arn])
    }]
  })
}

locals {
  # Secrets each task reads (r) or reads and writes (rw) itself, at runtime.
  read  = ["secretsmanager:GetSecretValue", "secretsmanager:DescribeSecret"]
  write = ["secretsmanager:GetSecretValue", "secretsmanager:DescribeSecret", "secretsmanager:PutSecretValue"]
  task_secrets = {
    api = {
      r  = ["cato/backend/config", "cato/alerts/discord-webhook", "cato/steamworks/cookies"]
      rw = ["cato/steamworks/refresh"] # the Steamworks login form
    }
    worker             = { r = ["cato/backend/config", "cato/alerts/discord-webhook"], rw = ["cato/steam/steamkit-auth"] }
    migrate            = { r = ["cato/backend/config"], rw = [] }
    "agent-light"      = { r = ["cato/collector/config"], rw = [] }
    "agent-events"     = { r = ["cato/collector/config"], rw = [] }
    "agent-steamdb"    = { r = ["cato/collector/config"], rw = ["cato/steamdb/cookies"] }
    "agent-steamworks" = { r = ["cato/collector/config"], rw = ["cato/steamworks/cookies", "cato/steamworks/refresh"] }
  }
}

resource "aws_iam_role" "task" {
  for_each           = local.task_secrets
  name               = "cato-task-${each.key}"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_assume.json
}

resource "aws_iam_role_policy" "task_secrets" {
  for_each = local.task_secrets
  name     = "own-secrets"
  role     = aws_iam_role.task[each.key].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = concat(
      length(each.value.r) == 0 ? [] : [{
        Effect   = "Allow"
        Action   = local.read
        Resource = [for id in each.value.r : "${local.secret_arn}:${id}-*"]
      }],
      length(each.value.rw) == 0 ? [] : [{
        Effect   = "Allow"
        Action   = local.write
        Resource = [for id in each.value.rw : "${local.secret_arn}:${id}-*"]
      }],
    )
  })
}

# Alerts: SNS e-mail from the api (test alert) and the worker; the heartbeat metric.
resource "aws_iam_role_policy" "alerts" {
  for_each = toset(["api", "worker"])
  name     = "alerts"
  role     = aws_iam_role.task[each.key].id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = concat(
      [{ Effect = "Allow", Action = ["sns:Publish"], Resource = data.aws_sns_topic.alerts.arn }],
      each.key == "worker" ? [{
        Effect    = "Allow"
        Action    = ["cloudwatch:PutMetricData"]
        Resource  = "*"
        Condition = { StringEquals = { "cloudwatch:namespace" = "CATO" } }
      }] : [],
    )
  })
}

# ── Edge node instance role ──

resource "aws_iam_role" "edge" {
  name = "cato-edge-node"
  assume_role_policy = jsonencode({
    Version   = "2012-10-17"
    Statement = [{ Effect = "Allow", Principal = { Service = "ec2.amazonaws.com" }, Action = "sts:AssumeRole" }]
  })
}

resource "aws_iam_role_policy_attachment" "edge" {
  for_each = toset([
    "arn:aws:iam::aws:policy/service-role/AmazonEC2ContainerServiceforEC2Role",
    "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore", # shell without Tailscale (drills)
  ])
  role       = aws_iam_role.edge.name
  policy_arn = each.value
}

resource "aws_iam_role_policy" "edge_boot" {
  name = "boot"
  role = aws_iam_role.edge.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      { Effect = "Allow", Action = local.read, Resource = aws_secretsmanager_secret.flat["cato/edge/tailscale-auth-key"].arn },
      { Effect = "Allow", Action = ["ec2:AssociateAddress", "ec2:DescribeAddresses"], Resource = "*" },
    ]
  })
}

resource "aws_iam_instance_profile" "edge" {
  name = "cato-edge-node"
  role = aws_iam_role.edge.name
}
