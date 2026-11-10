# The image tags that run now, so a plain `terraform plan` shows no change. The deploy
# scripts rewrite this file (scripts/set_image_tag.py) after a successful apply: commit it.
image_tag = "876ff4f"
image_tags = {
  "cato-backend"   = "7a0a2e2"
  "cato-caddy"     = "4f6d3b7"
  "cato-collector" = "5d1bd2a"
  "cato-frontend"  = "cf1272d"
  "ogt-api"        = "9e45495"
  "ogt-backup"     = "7717205"
  "ogt-frontend"   = "912858d"
}
