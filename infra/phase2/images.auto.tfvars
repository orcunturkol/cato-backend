# The image tags that run now, so a plain `terraform plan` shows no change. The deploy
# scripts rewrite this file (scripts/set_image_tag.py) after a successful apply: commit it.
image_tag = "876ff4f"
image_tags = {
  "cato-backend"   = "5f0f204"
  "cato-caddy"     = "4f6d3b7"
  "cato-collector" = "b9f5f0a"
  "cato-frontend"  = "669ae65"
  "ogt-api"        = "db62e90"
  "ogt-backup"     = "7717205"
  "ogt-frontend"   = "e7b84b3"
}
