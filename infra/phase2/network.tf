# Security groups. Inbound only from other CATO groups; nothing from the internet. People
# reach the services through Tailscale on the edge node (Caddy), never through an open port.

locals {
  security_groups = {
    edge         = "Edge node: Caddy, Seq, the steamdb and steamworks agents, the Tailscale subnet router"
    api          = "cato-api tasks (HTTP only)"
    worker       = "cato-worker task (hosted services)"
    migrate      = "cato-migrate one-shot task"
    frontend     = "cato-frontend tasks (nginx)"
    ogt-api      = "ogt-api tasks"
    ogt-frontend = "ogt-frontend tasks"
    agents       = "Fargate job agents (light, events)"
    mq           = "Amazon MQ broker (AMQPS)"
    cache        = "ElastiCache Valkey"
  }

  # [target group, port, source group]. "old-host" is today's EC2 host, for the cutover.
  ingress = [
    ["api", 8080, "edge"],    # Caddy, steamdb/steamworks agents
    ["api", 8080, "agents"],  # light/events agents: claims, job-run reports
    ["api", 8080, "ogt-api"], # OGT's catalog calls
    ["api", 8080, "old-host"],
    ["worker", 8080, "api"], # store enrichment (RemoteGameEnrichmentGateway)
    ["worker", 8080, "edge"],
    ["frontend", 80, "edge"],
    ["ogt-api", 8080, "edge"],
    ["ogt-frontend", 80, "edge"],
    ["mq", 5671, "worker"],
    ["mq", 5671, "api"],
    ["mq", 5671, "agents"],
    ["mq", 5671, "edge"],
    ["mq", 5671, "old-host"],
    ["mq", 443, "edge"], # management API (the gate tool's queue check)
    ["cache", 6379, "worker"],
    ["cache", 6379, "api"],
    ["cache", 6379, "agents"],
    ["cache", 6379, "edge"],
    ["cache", 6379, "old-host"],
  ]

  # Postgres in the existing cato-rds-sg (infra/rds keeps its rules as separate resources).
  rds_clients = ["api", "worker", "migrate", "ogt-api"]
}

resource "aws_security_group" "this" {
  for_each    = local.security_groups
  name        = "cato-${each.key}-sg"
  description = each.value
  vpc_id      = var.vpc_id
  tags        = { Name = "cato-${each.key}-sg" }
}

# Outbound: everything (Steam, SteamDB, Webshare, ECR, Secrets Manager; no NAT gateway).
resource "aws_vpc_security_group_egress_rule" "all" {
  for_each          = aws_security_group.this
  security_group_id = each.value.id
  ip_protocol       = "-1"
  cidr_ipv4         = "0.0.0.0/0"
}

resource "aws_vpc_security_group_ingress_rule" "this" {
  for_each = { for r in local.ingress : "${r[0]}-${r[1]}-from-${r[2]}" => r }

  security_group_id            = aws_security_group.this[each.value[0]].id
  ip_protocol                  = "tcp"
  from_port                    = each.value[1]
  to_port                      = each.value[1]
  referenced_security_group_id = each.value[2] == "old-host" ? var.old_host_security_group_id : aws_security_group.this[each.value[2]].id
  description                  = "from ${each.value[2]}"
}

resource "aws_vpc_security_group_ingress_rule" "rds" {
  for_each                     = toset(local.rds_clients)
  security_group_id            = var.rds_security_group_id
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
  referenced_security_group_id = aws_security_group.this[each.key].id
  description                  = "Postgres from ${each.key}"
}

# Private DNS: tasks register here (A records), so Caddy and the agents follow task IP changes.
resource "aws_service_discovery_private_dns_namespace" "cato" {
  name = local.namespace
  vpc  = var.vpc_id
}

resource "aws_service_discovery_service" "this" {
  for_each = toset(["api", "worker", "frontend", "ogt-api", "ogt-frontend"])
  name     = each.key

  dns_config {
    namespace_id   = aws_service_discovery_private_dns_namespace.cato.id
    routing_policy = "MULTIVALUE"
    dns_records {
      type = "A"
      ttl  = 10
    }
  }

  health_check_custom_config {
    failure_threshold = 1
  }
}

# Seq stays on today's host until Step D3, then this record moves to the edge node.
# Note: the host binds Seq to 127.0.0.1 and its Tailscale IP only; Step A must also bind it
# to the private IP (and open 5341 from the ECS groups) before ECS tasks can log there.
resource "aws_route53_record" "seq" {
  zone_id = aws_service_discovery_private_dns_namespace.cato.hosted_zone
  name    = "seq.${local.namespace}"
  type    = "A"
  ttl     = 60
  records = [var.old_host_private_ip]
}
