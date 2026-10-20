variable "aws_region" {
  description = "Region of every CATO resource."
  type        = string
  default     = "eu-central-1"
}

variable "alert_email" {
  description = "Address that receives critical job alerts from the cato-alerts SNS topic."
  type        = string
  default     = "orcun@catoptricgames.com"
}
