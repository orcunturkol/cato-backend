# OGT backup: a pg_dump of the OGT database only, every night at 01:00 UTC, kept 35 days.
# RDS keeps 7 days of instance backups, but the instance is shared with CATO: OGT alone comes
# back from them only through a new instance and a manual copy. A dump restores into any
# Postgres 16 or newer (README.md, "OGT backup"). Image: ogt-backup/ (deploy-ogt-backup.sh).

resource "aws_s3_bucket" "ogt_backups" {
  bucket = "cato-ogt-backups-${local.account_id}"
}

resource "aws_s3_bucket_public_access_block" "ogt_backups" {
  bucket                  = aws_s3_bucket.ogt_backups.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "ogt_backups" {
  bucket = aws_s3_bucket.ogt_backups.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_lifecycle_configuration" "ogt_backups" {
  bucket = aws_s3_bucket.ogt_backups.id
  rule {
    id     = "expire-ogt-dumps"
    status = "Enabled"
    filter {
      prefix = "ogt/"
    }
    expiration {
      days = 35
    }
  }
}

# The task writes new dumps only: it can neither read nor delete them.
resource "aws_iam_role" "ogt_backup" {
  name               = "cato-task-ogt-backup"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_assume.json
}

resource "aws_iam_role_policy" "ogt_backup" {
  name = "put-dumps"
  role = aws_iam_role.ogt_backup.id
  policy = jsonencode({
    Version   = "2012-10-17"
    Statement = [{ Effect = "Allow", Action = ["s3:PutObject"], Resource = "${aws_s3_bucket.ogt_backups.arn}/ogt/*" }]
  })
}

# The execution role is the shared one: it pulls from every repository and injects
# cato/ogt/config, so the task gets ogt-api's own database login.
resource "aws_ecs_task_definition" "ogt_backup" {
  family                   = "cato-ogt-backup"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = 256
  memory                   = 512
  execution_role_arn       = aws_iam_role.execution.arn
  task_role_arn            = aws_iam_role.ogt_backup.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "X86_64"
  }

  container_definitions = jsonencode([{
    name      = "ogt-backup"
    image     = "${local.ecr}/ogt-backup:${lookup(var.image_tags, "ogt-backup", var.image_tag)}"
    essential = true
    environment = [
      { name = "BACKUP_BUCKET", value = aws_s3_bucket.ogt_backups.bucket },
      { name = "AWS_REGION", value = var.aws_region },
    ]
    secrets = [{
      name      = "ConnectionStrings__DefaultConnection"
      valueFrom = "${aws_secretsmanager_secret.ogt_config.arn}:ConnectionStrings__DefaultConnection::"
    }]
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        awslogs-group         = aws_cloudwatch_log_group.this["ogt-api"].name
        awslogs-region        = var.aws_region
        awslogs-stream-prefix = "ogt-backup"
        mode                  = "non-blocking"
      }
    }
  }])
}

resource "aws_iam_role" "ogt_backup_scheduler" {
  name = "cato-ogt-backup-scheduler"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "scheduler.amazonaws.com" }
      Action    = "sts:AssumeRole"
      Condition = { StringEquals = { "aws:SourceAccount" = local.account_id } }
    }]
  })
}

resource "aws_iam_role_policy" "ogt_backup_scheduler" {
  name = "run-ogt-backup"
  role = aws_iam_role.ogt_backup_scheduler.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect    = "Allow"
        Action    = ["ecs:RunTask"]
        Resource  = "${aws_ecs_task_definition.ogt_backup.arn_without_revision}:*"
        Condition = { ArnEquals = { "ecs:cluster" = aws_ecs_cluster.cato.arn } }
      },
      {
        Effect    = "Allow"
        Action    = ["iam:PassRole"]
        Resource  = [aws_iam_role.execution.arn, aws_iam_role.ogt_backup.arn]
        Condition = { StringLike = { "iam:PassedToService" = "ecs-tasks.amazonaws.com" } }
      },
    ]
  })
}

# 01:00 UTC: before the RDS backup window (02:00-02:30), outside the job windows (03:00-06:00,
# 10:00-14:00) and OGT's watchlist check (14:00-15:00). The ogt-api network: its group may
# reach Postgres.
resource "aws_scheduler_schedule" "ogt_backup" {
  name                         = "cato-ogt-backup"
  description                  = "Nightly OGT-only pg_dump to S3 (task cato-ogt-backup)."
  schedule_expression          = "cron(0 1 * * ? *)"
  schedule_expression_timezone = "UTC"

  flexible_time_window {
    mode = "OFF"
  }

  target {
    arn      = aws_ecs_cluster.cato.arn
    role_arn = aws_iam_role.ogt_backup_scheduler.arn

    ecs_parameters {
      task_definition_arn = aws_ecs_task_definition.ogt_backup.arn
      task_count          = 1

      capacity_provider_strategy {
        capacity_provider = "FARGATE"
        weight            = 1
      }

      network_configuration {
        subnets          = var.subnet_ids
        security_groups  = [aws_security_group.this["ogt-api"].id]
        assign_public_ip = true # outbound (ECR, S3) without a NAT gateway; no inbound rule allows it
      }
    }

    # Retries a failed start (for example no Fargate capacity), not a failed dump.
    retry_policy {
      maximum_event_age_in_seconds = 3600
      maximum_retry_attempts       = 3
    }
  }
}

# The success line of backup.sh, as one data point per run.
resource "aws_cloudwatch_log_metric_filter" "ogt_backup_ok" {
  name           = "ogt-backup-ok"
  log_group_name = aws_cloudwatch_log_group.this["ogt-api"].name
  pattern        = "\"ogt-backup: ok\""

  metric_transformation {
    name      = "BackupOk"
    namespace = "OGT"
    value     = "1"
  }
}

# 25 hours without a successful dump: the 01:00 run failed or did not start. Hourly periods,
# so a normal day (at most 24 empty hours between two runs) never alarms; the gaps are filled
# with 0, as in phase1's heartbeat alarm. With no data at all (a new alarm, before the first
# run), missing data = breaching applies: run the task once by hand after the first apply.
resource "aws_cloudwatch_metric_alarm" "ogt_backup_missing" {
  alarm_name          = "cato-ogt-backup-missing"
  alarm_description   = "No successful OGT backup for 25 hours: the 01:00 UTC run of cato-ogt-backup failed or did not start (logs: /cato/ogt-api, prefix ogt-backup)."
  comparison_operator = "LessThanThreshold"
  threshold           = 1
  evaluation_periods  = 25
  datapoints_to_alarm = 25
  treat_missing_data  = "breaching"
  alarm_actions       = [data.aws_sns_topic.alerts.arn]
  ok_actions          = [data.aws_sns_topic.alerts.arn]

  metric_query {
    id          = "filled"
    expression  = "FILL(ok, 0)"
    label       = "Successful OGT backups per hour (0 when missing)"
    return_data = true
  }

  metric_query {
    id = "ok"
    metric {
      namespace   = aws_cloudwatch_log_metric_filter.ogt_backup_ok.metric_transformation[0].namespace
      metric_name = aws_cloudwatch_log_metric_filter.ogt_backup_ok.metric_transformation[0].name
      period      = 3600
      stat        = "Sum"
    }
  }
}
