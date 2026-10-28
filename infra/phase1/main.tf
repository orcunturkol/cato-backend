# Phase 1 of the infrastructure split: the few AWS parts that today's EC2 host needs
# before anything moves to ECS — the alert topic, the alert secret and a host role.
# See README.md for the bootstrap and the one-off steps outside Terraform.

terraform {
  required_version = ">= 1.10"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }

  backend "s3" {
    bucket       = "cato-terraform-state-657566734223"
    key          = "phase1/terraform.tfstate"
    region       = "eu-central-1"
    encrypt      = true
    use_lockfile = true
  }
}

provider "aws" {
  region = var.aws_region

  default_tags {
    tags = {
      Project = "cato"
      Stack   = "phase1"
    }
  }
}

# ── Alerts ──

resource "aws_sns_topic" "alerts" {
  name = "cato-alerts"
}

# AWS sends a confirmation email first; alerts arrive only after the link in it is clicked.
resource "aws_sns_topic_subscription" "alert_email" {
  topic_arn = aws_sns_topic.alerts.arn
  protocol  = "email"
  endpoint  = var.alert_email
}

# The value is set with the AWS CLI (README.md), so the webhook never enters the state.
resource "aws_secretsmanager_secret" "discord_webhook" {
  name                    = "cato/alerts/discord-webhook"
  description             = "Discord webhook URL for critical CATO job alerts."
  recovery_window_in_days = 7
}

# Credentials the jobs and the worker read and rotate at runtime, and the static secrets of
# each side. Terraform creates only the empty secrets: the instance role may read and write
# values but not create secrets. Values are set from the host (README.md), never from state.
locals {
  runtime_secrets = {
    "cato/steam/steamkit-auth" = "SteamKit refresh token and guard data of the worker's Steam account (rotated by the worker)."
    "cato/steamworks/refresh"  = "Steamworks steamRefresh_partner cookie (~207-day login)."
    "cato/steamworks/cookies"  = "Steamworks partner-site cookies, rewritten by every Steamworks job run."
    "cato/steamdb/cookies"     = "SteamDB login cookies (__Host-steamdb list) of the SteamDB jobs."
    "cato/backend/config"      = "Static backend secrets as JSON configuration keys (database, Steam account, Web API key)."
    "cato/collector/config"    = "Static collector secrets as JSON environment variables (API keys, Webshare, RabbitMQ)."
    "cato/server/compose-env"  = "Container environment per compose service as JSON {service: {NAME: value}}: Caddy's DuckDNS token, Seq's first-run password, OGT's database login and invite code."
  }
}

resource "aws_secretsmanager_secret" "runtime" {
  for_each                = local.runtime_secrets
  name                    = each.key
  description             = each.value
  recovery_window_in_days = 7
}

# Watches the watcher: the worker's alert monitor sends CATO/WorkerHeartbeat every minute.
# Five minutes without one (worker, host or AWS access down) e-mail the topic; ok_actions
# says when it is back. The plain metric alarm with missing data = breaching did not fire
# on 2026-10-21 when the worker was down 7 minutes (older data points were in range), so
# the gaps are filled with 0 by metric math: a missing minute is a real 0 below the
# threshold. With no data at all in range, missing data = breaching still applies.
resource "aws_cloudwatch_metric_alarm" "worker_heartbeat" {
  alarm_name          = "cato-worker-heartbeat"
  alarm_description   = "The CATO worker sent no heartbeat for 5 minutes: the worker, its host or its AWS access is down."
  comparison_operator = "LessThanThreshold"
  threshold           = 1
  evaluation_periods  = 5
  datapoints_to_alarm = 5
  treat_missing_data  = "breaching"
  alarm_actions       = [aws_sns_topic.alerts.arn]
  ok_actions          = [aws_sns_topic.alerts.arn]

  metric_query {
    id          = "filled"
    expression  = "FILL(beat, 0)"
    label       = "Worker heartbeats per minute (0 when missing)"
    return_data = true
  }

  metric_query {
    id = "beat"
    metric {
      namespace   = "CATO"
      metric_name = "WorkerHeartbeat"
      period      = 60
      stat        = "SampleCount"
    }
  }
}
