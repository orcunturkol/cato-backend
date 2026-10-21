locals {
  ecr = "${local.account_id}.dkr.ecr.${var.aws_region}.amazonaws.com"

  # valueFrom for one top-level key of a JSON secret: "<arn>:<key>::"
  mq_uri          = "${aws_secretsmanager_secret.mq.arn}:uri::"
  cache_backend   = "${aws_secretsmanager_secret.cache.arn}:backend::"
  cache_collector = "${aws_secretsmanager_secret.cache.arn}:collector::"

  backend_env = {
    ASPNETCORE_ENVIRONMENT         = "Production"
    Secrets__Backend               = "aws"
    AWS_REGION                     = var.aws_region
    Redis__InstanceName            = "cato"
    Alerts__Enabled                = "true"
    Alerts__HeartbeatEnabled       = "true"
    Alerts__SnsTopicArn            = data.aws_sns_topic.alerts.arn
    Alerts__DiscordWebhookSecretId = "cato/alerts/discord-webhook"
    # No Seq__ServerUrl until Step A binds the host's Seq to its private IP: CloudWatch only.
  }
  backend_secrets = {
    RabbitMQ__Uri           = local.mq_uri
    Redis__ConnectionString = local.cache_backend
  }

  collector_env = merge(var.collector_env, {
    CATO_API_BASE_URL = "http://api.${local.namespace}:8080"
    SECRETS_BACKEND   = "aws"
    AWS_REGION        = var.aws_region
  })
  collector_secrets = {
    REDIS_URL    = local.cache_collector
    RABBITMQ_URI = local.mq_uri # needs the collector's AMQPS branch (feat/collector-amqps)
  }

  # cpu/memory in Fargate units (MB); "edge" = the EC2 node, memory is a hard limit there.
  services = {
    api = {
      image = "cato-backend", cpu = 512, memory = 2048, port = 8080, edge = false
      env = merge(local.backend_env, {
        CATO_ROLE               = "api"
        Worker__BaseUrl         = "http://worker.${local.namespace}:8080/"
        Cors__AllowedOrigins__0 = "https://catoptricgames.duckdns.org"
        Swagger__Enabled        = "true"
      })
      secrets = local.backend_secrets, health = "/health/live", min_healthy = 100, max_percent = 200
    }
    worker = {
      image = "cato-backend", cpu = 1024, memory = 4096, port = 8080, edge = false
      env   = merge(local.backend_env, { CATO_ROLE = "worker" })
      # min 0 / max 100: the old task stops before the new one starts (one Steam login,
      # one dispatcher).
      secrets = local.backend_secrets, health = "/health/live", min_healthy = 0, max_percent = 100
    }
    frontend = {
      image = "cato-frontend", cpu = 256, memory = 512, port = 80, edge = false
      env   = {}, secrets = {}, health = null, min_healthy = 100, max_percent = 200
    }
    "ogt-api" = {
      image = "ogt-api", cpu = 256, memory = 512, port = 8080, edge = false
      env = {
        ASPNETCORE_ENVIRONMENT = "Production"
        Cato__BaseUrl          = "http://api.${local.namespace}:8080"
      }
      secrets = {
        ConnectionStrings__DefaultConnection = aws_secretsmanager_secret.flat["cato/ogt/db-connection"].arn
        Auth__InviteCode                     = aws_secretsmanager_secret.flat["cato/ogt/invite-code"].arn
      }
      health = null, min_healthy = 100, max_percent = 200
    }
    "ogt-frontend" = {
      image = "ogt-frontend", cpu = 256, memory = 512, port = 80, edge = false
      env   = {}, secrets = {}, health = null, min_healthy = 100, max_percent = 200
    }
    "agent-light" = {
      image  = "cato-collector", cpu = 256, memory = 1024, port = null, edge = false
      env    = merge(local.collector_env, { AGENT_GROUP = "light" }), secrets = local.collector_secrets
      health = null, min_healthy = 0, max_percent = 100
    }
    "agent-events" = {
      image  = "cato-collector", cpu = 512, memory = 2048, port = null, edge = false
      env    = merge(local.collector_env, { AGENT_GROUP = "events" }), secrets = local.collector_secrets
      health = null, min_healthy = 0, max_percent = 100
    }
    "agent-steamdb" = {
      image = "cato-collector", cpu = 1024, memory = 3072, port = null, edge = true
      env = merge(local.collector_env, {
        AGENT_GROUP                  = "steamdb"
        STEAMDB_CHROME_USER_DATA_DIR = "/srv/chrome-profile"
      })
      secrets = local.collector_secrets, health = null, min_healthy = 0, max_percent = 100
    }
    "agent-steamworks" = {
      image   = "cato-collector", cpu = 512, memory = 1024, port = null, edge = true
      env     = merge(local.collector_env, { AGENT_GROUP = "steamworks" })
      secrets = local.collector_secrets, health = null, min_healthy = 0, max_percent = 100
    }
    caddy = {
      # The image must carry a Caddyfile with *.cato.internal upstreams (Step D3).
      image   = "cato-caddy", cpu = 256, memory = 256, port = null, edge = true
      env     = { ACME_EMAIL = "ofturkol@proton.me" }
      secrets = { DUCKDNS_TOKEN = aws_secretsmanager_secret.flat["cato/caddy/duckdns-token"].arn }
      health  = null, min_healthy = 0, max_percent = 100
    }
    seq = {
      image   = null, cpu = 256, memory = 1024, port = null, edge = true
      env     = { ACCEPT_EULA = "Y" }
      secrets = { SEQ_FIRSTRUN_ADMINPASSWORD = aws_secretsmanager_secret.flat["cato/seq/admin-password"].arn }
      health  = null, min_healthy = 0, max_percent = 100
    }
  }

  # Host directories on the edge node (edge-user-data.sh creates them).
  edge_mounts = {
    "agent-steamdb" = [
      { volume = "chrome-profile", host = "/srv/cato/chrome-profile", path = "/srv/chrome-profile" },
      { volume = "steamdb-historical", host = "/srv/cato/steamdb-historical", path = "/app/data/games/historical" },
    ]
    caddy = [
      { volume = "caddy-data", host = "/srv/cato/caddy-data", path = "/data" },
      { volume = "caddy-config", host = "/srv/cato/caddy-config", path = "/config" },
    ]
    seq = [{ volume = "seq", host = "/srv/cato/seq", path = "/data" }]
  }

  # Caddy shares the node's network (it listens on the Tailscale IP); Seq maps 5341 -> 80.
  network_mode = { caddy = "host", seq = "bridge", "agent-steamdb" = "bridge", "agent-steamworks" = "bridge" }
}

resource "aws_ecs_task_definition" "this" {
  for_each = local.services

  family                   = "cato-${each.key}"
  requires_compatibilities = each.value.edge ? ["EC2"] : ["FARGATE"]
  network_mode             = each.value.edge ? local.network_mode[each.key] : "awsvpc"
  cpu                      = each.value.edge ? null : each.value.cpu
  memory                   = each.value.edge ? null : each.value.memory
  execution_role_arn       = aws_iam_role.execution.arn
  task_role_arn            = contains(keys(aws_iam_role.task), each.key) ? aws_iam_role.task[each.key].arn : null

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64" # Chrome has no Linux ARM64 build; one image arch for all
  }

  dynamic "ephemeral_storage" {
    for_each = each.key == "agent-events" ? [1] : []
    content {
      size_in_gib = 21 # Chrome and its cache
    }
  }

  dynamic "volume" {
    for_each = lookup(local.edge_mounts, each.key, [])
    content {
      name      = volume.value.volume
      host_path = volume.value.host
    }
  }

  container_definitions = jsonencode([{
    name      = each.key
    image     = each.value.image == null ? "datalust/seq:2025.2" : "${local.ecr}/${each.value.image}:${var.image_tag}"
    essential = true
    cpu       = each.value.edge ? each.value.cpu : null
    memory    = each.value.edge ? each.value.memory : null # hard limit: a leak kills only this task

    # Same attributes in both branches, or Terraform turns the numbers into strings.
    portMappings = each.key == "seq" ? [{ containerPort = 80, hostPort = 5341, protocol = "tcp" }] : (
      each.value.port == null || each.value.edge ? [] : [{ containerPort = each.value.port, hostPort = each.value.port, protocol = "tcp" }]
    )

    environment = [for k, v in each.value.env : { name = k, value = v }]
    secrets     = [for k, v in each.value.secrets : { name = k, valueFrom = v }]

    mountPoints = [for m in lookup(local.edge_mounts, each.key, []) : { sourceVolume = m.volume, containerPath = m.path }]

    healthCheck = each.value.health == null ? null : {
      command     = ["CMD-SHELL", "curl -fsS http://localhost:${each.value.port}${each.value.health} || exit 1"]
      interval    = 30
      timeout     = 5
      retries     = 3
      startPeriod = 180
    }

    # The .NET host needs up to 30 s to stop its services.
    stopTimeout = 45

    logConfiguration = {
      logDriver = "awslogs"
      options = {
        awslogs-group         = aws_cloudwatch_log_group.this[each.key].name
        awslogs-region        = var.aws_region
        awslogs-stream-prefix = each.key
        mode                  = "non-blocking"
        max-buffer-size       = "25m"
      }
    }
  }])
}

# The migrate one-shot: the deploy script runs it (aws ecs run-task) and waits for exit 0.
resource "aws_ecs_task_definition" "migrate" {
  family                   = "cato-migrate"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = 256
  memory                   = 512
  execution_role_arn       = aws_iam_role.execution.arn
  task_role_arn            = aws_iam_role.task["migrate"].arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  container_definitions = jsonencode([{
    name        = "migrate"
    image       = "${local.ecr}/cato-backend:${var.image_tag}"
    essential   = true
    environment = [for k, v in merge(local.backend_env, { CATO_ROLE = "migrate" }) : { name = k, value = v }]
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        awslogs-group         = aws_cloudwatch_log_group.this["migrate"].name
        awslogs-region        = var.aws_region
        awslogs-stream-prefix = "migrate"
        mode                  = "non-blocking"
      }
    }
  }])
}

resource "aws_ecs_service" "this" {
  for_each = local.services

  name                               = each.key
  cluster                            = aws_ecs_cluster.cato.id
  task_definition                    = aws_ecs_task_definition.this[each.key].arn
  desired_count                      = var.desired_counts[each.key]
  deployment_minimum_healthy_percent = each.value.min_healthy
  deployment_maximum_percent         = each.value.max_percent
  enable_execute_command             = true

  capacity_provider_strategy {
    capacity_provider = each.value.edge ? aws_ecs_capacity_provider.edge.name : "FARGATE"
    weight            = 1
  }

  dynamic "placement_constraints" {
    for_each = each.value.edge ? [1] : []
    content {
      type       = "memberOf"
      expression = "attribute:cato-role == edge"
    }
  }

  dynamic "network_configuration" {
    for_each = each.value.edge ? [] : [1]
    content {
      subnets          = var.subnet_ids
      security_groups  = [aws_security_group.this[startswith(each.key, "agent-") ? "agents" : each.key].id]
      assign_public_ip = true # outbound internet without a NAT gateway; no inbound rule allows it
    }
  }

  dynamic "service_registries" {
    for_each = contains(keys(aws_service_discovery_service.this), each.key) ? [1] : []
    content {
      registry_arn = aws_service_discovery_service.this[each.key].arn
    }
  }

  depends_on = [aws_ecs_cluster_capacity_providers.cato]
}
