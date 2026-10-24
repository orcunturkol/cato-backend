variable "aws_region" {
  description = "AWS region hosting the CATO infrastructure"
  type        = string
  default     = "eu-central-1"
}

variable "vpc_id" {
  description = "VPC ID hosting the catoptric-games EC2 instance"
  type        = string
  default     = "vpc-07c3a68d93c8593da"
}

variable "subnet_ids" {
  description = "Subnet IDs for the RDS subnet group (must span at least 2 AZs)"
  type        = list(string)
  default = [
    "subnet-0eb98332667f78c88", # eu-central-1b (EC2 instance's own subnet)
    "subnet-043aaca70c16f6569", # eu-central-1a
    "subnet-0578527c95f88c8e1", # eu-central-1c
  ]
}

variable "availability_zone" {
  description = "AZ to place the RDS instance in (co-located with the EC2 instance)"
  type        = string
  default     = "eu-central-1b"
}

variable "engine_version" {
  description = "Postgres engine version"
  type        = string
  default     = "16.14"
}

# 2026-10-21: micro (1 GB) had ~95 MB free and swapped; watcher queries hit the 30 s
# command limit every few hours. Changed with modify-db-instance (state is not here).
variable "instance_class" {
  description = "RDS instance class"
  type        = string
  default     = "db.t4g.medium"
}

variable "db_name" {
  description = "Database name"
  type        = string
  default     = "cato"
}

variable "db_username" {
  description = "Master username"
  type        = string
  default     = "cato_user"
}
