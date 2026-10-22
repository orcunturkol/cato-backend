# The image tags that run now, so a plain `terraform plan` shows no change. deploy.sh and
# deploy-collector.sh rewrite this file after a successful apply: commit it.
image_tag  = "876ff4f"
image_tags = { "cato-collector" = "f1ef27e" }
