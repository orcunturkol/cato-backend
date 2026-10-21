resource "aws_ecr_repository" "this" {
  for_each             = toset(["cato-backend", "cato-frontend", "cato-collector", "cato-caddy", "ogt-api", "ogt-frontend"])
  name                 = each.key
  image_tag_mutability = "MUTABLE"

  image_scanning_configuration {
    scan_on_push = true
  }
}

resource "aws_ecr_lifecycle_policy" "this" {
  for_each   = aws_ecr_repository.this
  repository = each.value.name
  policy = jsonencode({
    rules = [{
      rulePriority = 1
      description  = "Keep the newest 20 images"
      selection    = { tagStatus = "any", countType = "imageCountMoreThan", countNumber = 20 }
      action       = { type = "expire" }
    }]
  })
}

# One log group per service and agent; awslogs uses mode non-blocking, so a slow log
# delivery never stalls a task (the 62 GB lesson, the other way round).
resource "aws_cloudwatch_log_group" "this" {
  for_each = toset([
    "api", "worker", "migrate", "frontend", "ogt-api", "ogt-frontend",
    "agent-light", "agent-events", "agent-steamdb", "agent-steamworks", "caddy", "seq", "edge",
  ])
  name              = "/cato/${each.key}"
  retention_in_days = var.log_retention_days
}
