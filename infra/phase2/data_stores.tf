# Hourly cost starts here (README.md "Cost"). Both are private, in the RDS AZ, with their
# maintenance outside the job windows.

resource "aws_mq_broker" "cato" {
  broker_name                = "cato"
  engine_type                = "RabbitMQ"
  engine_version             = var.mq_engine_version
  auto_minor_version_upgrade = true
  host_instance_type         = var.mq_instance_type
  deployment_mode            = "SINGLE_INSTANCE"
  publicly_accessible        = false
  subnet_ids                 = [var.edge_subnet_id]
  security_groups            = [aws_security_group.this["mq"].id]

  user {
    username = "cato"
    password = random_password.mq.result
  }

  maintenance_window_start_time {
    day_of_week = var.mq_maintenance.day
    time_of_day = var.mq_maintenance.time
    time_zone   = "UTC"
  }

  logs {
    general = true
  }
}

resource "aws_elasticache_subnet_group" "cato" {
  name       = "cato-cache"
  subnet_ids = var.subnet_ids
}

# Node-based (not serverless): a small fixed price. The backend seeds the app-id sets from
# Postgres when they are missing, but Step A copies today's Redis first (plan "Step A").
resource "aws_elasticache_replication_group" "cato" {
  replication_group_id        = "cato"
  description                 = "CATO app-id queues, locks and output cache (was the host's Redis)"
  engine                      = "valkey"
  engine_version              = "8.1"
  node_type                   = var.cache_node_type
  num_cache_clusters          = 1
  automatic_failover_enabled  = false
  port                        = 6379
  subnet_group_name           = aws_elasticache_subnet_group.cato.name
  security_group_ids          = [aws_security_group.this["cache"].id]
  preferred_cache_cluster_azs = ["eu-central-1b"]

  transit_encryption_enabled = true
  at_rest_encryption_enabled = true
  auth_token                 = random_password.cache.result

  maintenance_window       = var.cache_maintenance_window
  snapshot_window          = var.cache_snapshot_window
  snapshot_retention_limit = 3
  apply_immediately        = false
}
