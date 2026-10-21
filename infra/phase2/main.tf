# Phase 2: the AWS foundation for the ECS split (plan "Phase 2"). Nothing here runs a job or
# takes traffic: the worker and the four agents start with desired_count 0, because the
# ECS api uses the production database (an ECS agent would claim dispatches from the host's
# agents; a second worker is a second Steam login and a second dispatcher). Phase 3 turns
# them on, one step at a time. See README.md for the apply order and the cost.

terraform {
  required_version = ">= 1.10"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  backend "s3" {
    bucket       = "cato-terraform-state-657566734223"
    key          = "phase2/terraform.tfstate"
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
      Stack   = "phase2"
    }
  }
}

data "aws_caller_identity" "current" {}

data "aws_ssm_parameter" "ecs_ami" {
  name = "/aws/service/ecs/optimized-ami/amazon-linux-2023/recommended/image_id"
}

data "aws_sns_topic" "alerts" {
  name = "cato-alerts"
}

locals {
  account_id = data.aws_caller_identity.current.account_id
  secret_arn = "arn:aws:secretsmanager:${var.aws_region}:${local.account_id}:secret"

  # The internal names that Caddy, the agents and the api use instead of IP addresses.
  namespace = "cato.internal"
}
