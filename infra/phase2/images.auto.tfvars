# The image tags that run now, so a plain `terraform plan` shows no change. The deploy
# scripts rewrite this file (scripts/set_image_tag.py) after a successful apply: commit it.
image_tag = "876ff4f"
image_tags = {
  "cato-backend"   = "26319b0"
  "cato-caddy"     = "42ac6d2"
  "cato-collector" = "95bb742"
  "cato-frontend"  = "24aac13"
}
