output "rds_endpoint" {
  description = "RDS instance endpoint (host:port)"
  value       = aws_db_instance.cato.endpoint
}

output "rds_address" {
  description = "RDS instance hostname only (no port)"
  value       = aws_db_instance.cato.address
}

output "rds_port" {
  description = "RDS instance port"
  value       = aws_db_instance.cato.port
}
