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

data "aws_caller_identity" "current" {}

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

# ── Role for today's EC2 host ──
# Every CATO process still runs on one host, so one role covers them. The ECS move
# replaces it with one task role per service and agent, each limited to its own secrets.

resource "aws_iam_role" "host" {
  name        = "cato-ec2-host"
  description = "Today's CATO EC2 host: alerts, secrets under cato/*, CATO metrics."

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "ec2.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy" "host" {
  name = "cato-ec2-host"
  role = aws_iam_role.host.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid      = "PublishAlerts"
        Effect   = "Allow"
        Action   = "sns:Publish"
        Resource = aws_sns_topic.alerts.arn
      },
      {
        Sid    = "ReadAndRotateCatoSecrets"
        Effect = "Allow"
        Action = [
          "secretsmanager:GetSecretValue",
          "secretsmanager:DescribeSecret",
          "secretsmanager:PutSecretValue",
        ]
        Resource = "arn:aws:secretsmanager:${var.aws_region}:${data.aws_caller_identity.current.account_id}:secret:cato/*"
      },
      {
        Sid      = "HeartbeatMetrics"
        Effect   = "Allow"
        Action   = "cloudwatch:PutMetricData"
        Resource = "*"
        Condition = {
          StringEquals = { "cloudwatch:namespace" = "CATO" }
        }
      },
    ]
  })
}

resource "aws_iam_instance_profile" "host" {
  name = "cato-ec2-host"
  role = aws_iam_role.host.name
}
