# The image tags that run now, so a plain `terraform plan` shows no change. The deploy
# scripts rewrite this file (scripts/set_image_tag.py) after a successful apply: commit it.
image_tag = "876ff4f"
image_tags = {
  "cato-backend"   = "413618f"
  "cato-caddy"     = "4f6d3b7"
  "cato-collector" = "7c0007f"
  "cato-frontend"  = "7014854"
  "ogt-api"        = "023705c"
  "ogt-backup"     = "7717205"
  "ogt-frontend"   = "bbe22ad"
}
