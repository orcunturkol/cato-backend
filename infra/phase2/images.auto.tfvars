# The image tags that run now, so a plain `terraform plan` shows no change. The deploy
# scripts rewrite this file (scripts/set_image_tag.py) after a successful apply: commit it.
image_tag = "876ff4f"
image_tags = {
  "cato-backend"   = "8e5df31"
  "cato-caddy"     = "42ac6d2"
  "cato-collector" = "95bb742"
  "cato-frontend"  = "cf1272d"
  "ogt-api"        = "2057828"
  "ogt-frontend"   = "4eb1ff8"
}
