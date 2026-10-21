resource "aws_ecs_cluster" "cato" {
  name = "cato"
}

# ── Edge node: one EC2 instance in an ECS-managed group (min = max = 1) ──
# Caddy (TLS edge on the node's Tailscale IP), Seq, and the steamdb and steamworks agents
# (Chrome profile on the host; one fixed public IP for the Steamworks partner login).

resource "aws_eip" "edge" {
  domain = "vpc"
  tags   = { Name = "cato-edge" }
}

resource "aws_launch_template" "edge" {
  name_prefix   = "cato-edge-"
  image_id      = data.aws_ssm_parameter.ecs_ami.value
  instance_type = var.edge_instance_type

  iam_instance_profile {
    arn = aws_iam_instance_profile.edge.arn
  }

  vpc_security_group_ids = [aws_security_group.this["edge"].id]

  metadata_options {
    http_tokens                 = "required"
    http_put_response_hop_limit = 2
  }

  block_device_mappings {
    device_name = "/dev/xvda"
    ebs {
      volume_size = 60 # images (the collector image is ~1.5 GB), Chrome profile, Seq
      volume_type = "gp3"
      encrypted   = true
    }
  }

  user_data = base64encode(templatefile("${path.module}/edge-user-data.sh", {
    cluster           = aws_ecs_cluster.cato.name
    region            = var.aws_region
    eip_allocation_id = aws_eip.edge.allocation_id
    tailscale_secret  = aws_secretsmanager_secret.flat["cato/edge/tailscale-auth-key"].name
    vpc_cidr          = var.vpc_cidr
  }))

  tag_specifications {
    resource_type = "instance"
    tags          = { Name = "cato-edge" }
  }
}

resource "aws_autoscaling_group" "edge" {
  name                = "cato-edge"
  min_size            = 1
  max_size            = 1
  desired_capacity    = 1
  vpc_zone_identifier = [var.edge_subnet_id]

  launch_template {
    id      = aws_launch_template.edge.id
    version = "$Latest"
  }

  tag {
    key                 = "AmazonECSManaged"
    value               = "true"
    propagate_at_launch = true
  }
}

resource "aws_ecs_capacity_provider" "edge" {
  name = "cato-edge"

  auto_scaling_group_provider {
    auto_scaling_group_arn         = aws_autoscaling_group.edge.arn
    managed_termination_protection = "DISABLED"

    managed_scaling {
      status = "DISABLED" # always exactly one node
    }
  }
}

resource "aws_ecs_cluster_capacity_providers" "cato" {
  cluster_name       = aws_ecs_cluster.cato.name
  capacity_providers = ["FARGATE", aws_ecs_capacity_provider.edge.name]

  default_capacity_provider_strategy {
    capacity_provider = "FARGATE"
    weight            = 1
  }
}
