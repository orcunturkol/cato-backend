# The image tags that run now, so a plain `terraform plan` shows no change. The deploy
# scripts rewrite this file (scripts/set_image_tag.py) after a successful apply: commit it.
image_tag = "876ff4f"
image_tags = {
  "cato-backend"   = "5f0f204"
  "cato-caddy"     = "4f6d3b7"
  "cato-collector" = "c115fcd"
  "cato-frontend"  = "127775e"
  "ogt-api"        = "51e7347"
  "ogt-backup"     = "7717205"
  "ogt-frontend"   = "a4b6271"
}
