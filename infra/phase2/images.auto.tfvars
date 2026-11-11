# The image tags that run now, so a plain `terraform plan` shows no change. The deploy
# scripts rewrite this file (scripts/set_image_tag.py) after a successful apply: commit it.
image_tag = "876ff4f"
image_tags = {
  "cato-backend"   = "071715a"
  "cato-caddy"     = "4f6d3b7"
  "cato-collector" = "d1d5103"
  "cato-frontend"  = "3994760"
  "ogt-api"        = "8db4cd2"
  "ogt-backup"     = "7717205"
  "ogt-frontend"   = "b64991d"
}
