# The image tags that run now, so a plain `terraform plan` shows no change. The deploy
# scripts rewrite this file (scripts/set_image_tag.py) after a successful apply: commit it.
image_tag = "876ff4f"
image_tags = {
  "cato-backend"   = "fe7cf1c"
  "cato-collector" = "4af8e1d"
}
