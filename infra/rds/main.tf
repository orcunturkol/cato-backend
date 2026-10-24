terraform {
  required_version = ">= 1.10"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }

  # Created in August 2026 with local state that no longer exists; imports.tf brings the
  # live resources into this state (Phase 2 step 1).
  backend "s3" {
    bucket       = "cato-terraform-state-657566734223"
    key          = "rds/terraform.tfstate"
    region       = "eu-central-1"
    encrypt      = true
    use_lockfile = true
  }
}

provider "aws" {
  region = var.aws_region
}

resource "aws_db_subnet_group" "cato" {
  name       = "cato-rds-subnet-group"
  subnet_ids = var.subnet_ids

  tags = {
    Name = "cato-rds-subnet-group"
  }
}

resource "aws_security_group" "rds" {
  name        = "cato-rds-sg"
  description = "Allow Postgres access from the CATO EC2 instance only"
  vpc_id      = var.vpc_id

  # Rules are separate resources: infra/phase2 adds the ECS tasks' rules to this group, and
  # inline rules here would remove them on the next apply.

  tags = {
    Name = "cato-rds-sg"
  }
}

resource "aws_vpc_security_group_egress_rule" "rds_all" {
  security_group_id = aws_security_group.rds.id
  ip_protocol       = "-1"
  cidr_ipv4         = "0.0.0.0/0"
}

resource "aws_db_instance" "cato" {
  identifier     = "cato-postgres"
  engine         = "postgres"
  engine_version = var.engine_version
  instance_class = var.instance_class

  allocated_storage     = 20
  max_allocated_storage = 100
  storage_type          = "gp3"
  storage_encrypted     = true

  db_name  = var.db_name
  username = var.db_username
  # No password here: the instance exists, and its password lives in the secret
  # cato/backend/config (connection string). The SSM parameter /cato/rds/db_password from
  # the first apply is a leftover copy and is not managed here.
  port = 5432

  db_subnet_group_name   = aws_db_subnet_group.cato.name
  vpc_security_group_ids = [aws_security_group.rds.id]
  availability_zone      = var.availability_zone
  multi_az               = false
  publicly_accessible    = false

  auto_minor_version_upgrade = true
  backup_retention_period    = 7
  # Outside the job windows: nothing runs 02:00-02:45 UTC; Sunday 07:05 is between two
  # follower runs (a minor-version upgrade restarts the database).
  backup_window      = "02:00-02:30"
  maintenance_window = "sun:07:05-sun:07:40"

  deletion_protection       = true
  skip_final_snapshot       = false
  final_snapshot_identifier = "cato-postgres-final-snapshot"

  tags = {
    Name = "cato-postgres"
  }
}
