output "ecr_repositories" {
  value = { for k, r in aws_ecr_repository.this : k => r.repository_url }
}

output "cluster" {
  value = aws_ecs_cluster.cato.name
}

output "edge_public_ip" {
  description = "The edge node's fixed address (the Steamworks partner login sees this one)"
  value       = aws_eip.edge.public_ip
}

output "mq_endpoint" {
  value = local.mq_amqps
}

output "mq_console" {
  value = aws_mq_broker.cato.instances[0].console_url
}

output "cache_endpoint" {
  value = aws_elasticache_replication_group.cato.primary_endpoint_address
}

output "namespace" {
  value = aws_service_discovery_private_dns_namespace.cato.name
}

output "migrate_task_definition" {
  value = aws_ecs_task_definition.migrate.arn
}

output "service_security_groups" {
  description = "For run-task of the migrate task (deploy script) and of the OGT backup (by hand)"
  value       = { migrate = aws_security_group.this["migrate"].id, "ogt-api" = aws_security_group.this["ogt-api"].id }
}

output "subnet_ids" {
  description = "For the deploy script's run-task of the migrate task"
  value       = var.subnet_ids
}

output "ogt_backup_bucket" {
  description = "The nightly OGT dumps (ogt/ogt-<UTC time>.dump, kept 35 days)"
  value       = aws_s3_bucket.ogt_backups.bucket
}
