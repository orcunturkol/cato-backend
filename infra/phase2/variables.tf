variable "aws_region" {
  type    = string
  default = "eu-central-1"
}

variable "vpc_id" {
  type    = string
  default = "vpc-07c3a68d93c8593da"
}

variable "vpc_cidr" {
  description = "Routed into the tailnet by the edge node (subnet router)"
  type        = string
  default     = "172.31.0.0/16"
}

variable "subnet_ids" {
  description = "Public subnets (public IP on launch, internet gateway route; no NAT gateway)"
  type        = list(string)
  default = [
    "subnet-0eb98332667f78c88", # eu-central-1b
    "subnet-043aaca70c16f6569", # eu-central-1a
    "subnet-0578527c95f88c8e1", # eu-central-1c
  ]
}

variable "edge_subnet_id" {
  description = "The edge node and the broker stay in one AZ with RDS"
  type        = string
  default     = "subnet-0eb98332667f78c88" # eu-central-1b
}

variable "rds_security_group_id" {
  type    = string
  default = "sg-02821bdf7d5512bc6" # cato-rds-sg (infra/rds)
}

variable "old_host_security_group_id" {
  description = "Today's EC2 host (launch-wizard-1): reaches the broker, the cache and the api during the cutover. Remove at decommission."
  type        = string
  default     = "sg-0d8fa8b482fd514d4"
}


variable "image_tag" {
  description = "Tag of every image in ECR; the deploy script pushes this tag"
  type        = string
  default     = "latest"
}

variable "image_tags" {
  description = "Tag per ECR repo deployed on its own (deploy-collector.sh); other repos use image_tag"
  type        = map(string)
  default     = {}
}

variable "edge_instance_type" {
  description = "x86_64: Chrome has no Linux ARM64 build"
  type        = string
  default     = "t3.large"
}

variable "mq_instance_type" {
  description = "mq.t3.micro does not exist for RabbitMQ in eu-central-1 (2026-10-21); this is the smallest"
  type        = string
  default     = "mq.m7g.medium"
}

variable "mq_engine_version" {
  description = "Backend messaging tests and the collector publisher passed on 4.3.6 and 4.2.9 (2026-10-21)"
  type        = string
  default     = "4.3"
}

variable "cache_node_type" {
  description = "Redis uses about 40 MB today"
  type        = string
  default     = "cache.t4g.micro"
}

variable "log_retention_days" {
  type    = number
  default = 30
}

# Outside the job windows 03:00-06:00 and 10:00-14:00 UTC.
variable "mq_maintenance" {
  type = object({ day = string, time = string })
  default = {
    day  = "SUNDAY"
    time = "07:00"
  }
}

variable "cache_maintenance_window" {
  type    = string
  default = "sun:07:00-sun:08:00"
}

variable "cache_snapshot_window" {
  type    = string
  default = "06:00-07:00"
}

variable "desired_counts" {
  description = "Tasks per service; each Phase 3 step raises its own (Step A done 2026-10-22)"
  type        = map(number)
  default = {
    api                = 1
    frontend           = 1
    "ogt-api"          = 1 # never 2, and never with the host's ogt-api: its WatchlistCheckService must run once
    "ogt-frontend"     = 1
    worker             = 1 # exactly one: two are two Steam logins and two dispatchers
    "agent-light"      = 1 # Step B (2026-10-22, Ubuntu image d9bd683); host unit cato-agent@light disabled
    "agent-events"     = 1 # Step C (2026-10-23); host unit cato-agent@events disabled
    "agent-steamworks" = 1 # Step D1 (2026-10-23, edge node, EIP); host unit cato-agent@steamworks disabled
    "agent-steamdb"    = 1 # Step D2 (2026-10-23, edge node); host unit cato-agent@steamdb disabled
    caddy              = 1 # Step D3 (2026-10-23)
    seq                = 1 # Step D3 (2026-10-23)
  }
}

variable "collector_env" {
  description = "Non-secret collector settings (today's collector .env)"
  type        = map(string)
  default = {
    STEAM_API_RATE_LIMIT_DELAY = "0.2"
    STEAMDB_HEADLESS           = "0"
    WEBSHARE_PROXY_COUNTRY     = "GB"
    # A 3 GB/month plan over 30 days; no cap until WEBSHARE_API_KEY is in cato/collector/config.
    WEBSHARE_DAILY_BUDGET_MB = "100"
  }
}
