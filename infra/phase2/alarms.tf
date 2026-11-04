# Alarms of this stack; they e-mail the cato-alerts topic (phase1), and ok_actions says when
# the problem is over. The nightly backup's alarm is in ogt_backup.tf.

# ECS sends ogt-api's CPU once a minute per running task. Ten minutes without one: no task runs
# (it stopped and does not start again). A task that runs but answers badly does not trip it.
# The gaps are filled with 0 by metric math, as in phase1's heartbeat alarm: with plain
# missing data = breaching, older data points in range kept that alarm quiet on 2026-10-21.
resource "aws_cloudwatch_metric_alarm" "ogt_api_down" {
  alarm_name          = "cato-ogt-api-down"
  alarm_description   = "No ogt-api task has run for 10 minutes (no ECS CPU data points): OGT is down."
  comparison_operator = "LessThanThreshold"
  threshold           = 1
  evaluation_periods  = 10
  datapoints_to_alarm = 10
  treat_missing_data  = "breaching"
  alarm_actions       = [data.aws_sns_topic.alerts.arn]
  ok_actions          = [data.aws_sns_topic.alerts.arn]

  metric_query {
    id          = "filled"
    expression  = "FILL(tasks, 0)"
    label       = "ogt-api tasks that sent CPU data per minute (0 when missing)"
    return_data = true
  }

  metric_query {
    id = "tasks"
    metric {
      namespace   = "AWS/ECS"
      metric_name = "CPUUtilization"
      period      = 60
      stat        = "SampleCount"
      dimensions = {
        ClusterName = aws_ecs_cluster.cato.name
        ServiceName = aws_ecs_service.this["ogt-api"].name
      }
    }
  }
}
