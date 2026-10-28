output "alerts_topic_arn" {
  description = "SNS topic for critical job alerts (Alerts:SnsTopicArn in the API)."
  value       = aws_sns_topic.alerts.arn
}

output "discord_webhook_secret_name" {
  description = "Secrets Manager name of the Discord webhook URL."
  value       = aws_secretsmanager_secret.discord_webhook.name
}
