# The image tags that run now, so a plain `terraform plan` shows no change. The deploy
# scripts rewrite this file (scripts/set_image_tag.py) after a successful apply: commit it.
image_tag = "876ff4f"
image_tags = {
  "cato-backend"   = "c0146e0"
  "cato-caddy"     = "4f6d3b7"
  "cato-collector" = "d15e577"
  "cato-frontend"  = "8a8f6df"
  "ogt-api"        = "b3e818e"
  "ogt-backup"     = "7717205"
  "ogt-frontend"   = "9630181"
}
