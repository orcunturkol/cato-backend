terraform {
  required_version = ">= 1.5"

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

  ingress {
    description     = "Postgres from the CATO EC2 instance"
    from_port       = 5432
    to_port         = 5432
    protocol        = "tcp"
    security_groups = [var.ec2_security_group_id]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name = "cato-rds-sg"
  }
}

resource "random_password" "db" {
  length  = 32
  special = false
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
  password = random_password.db.result
  port     = 5432

  db_subnet_group_name   = aws_db_subnet_group.cato.name
  vpc_security_group_ids = [aws_security_group.rds.id]
  availability_zone      = var.availability_zone
  multi_az               = false
  publicly_accessible    = false

  auto_minor_version_upgrade = true
  backup_retention_period    = 7
  backup_window              = "03:00-04:00"
  maintenance_window         = "mon:04:30-mon:05:30"

  deletion_protection       = true
  skip_final_snapshot       = false
  final_snapshot_identifier = "cato-postgres-final-snapshot"

  tags = {
    Name = "cato-postgres"
  }
}

resource "aws_ssm_parameter" "db_password" {
  name        = "/cato/rds/db_password"
  description = "Master password for the cato-postgres RDS instance"
  type        = "SecureString"
  value       = random_password.db.result

  tags = {
    Name = "cato-rds-db-password"
  }
}
