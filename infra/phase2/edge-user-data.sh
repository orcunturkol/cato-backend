#!/bin/bash
# Boot of the edge node (ECS-optimized Amazon Linux 2023). Rendered by Terraform.
set -euo pipefail

cat >> /etc/ecs/ecs.config <<EOF
ECS_CLUSTER=${cluster}
ECS_INSTANCE_ATTRIBUTES={"cato-role":"edge"}
EOF

# Host directories of the edge services. uid 1000 is the collector image's user.
install -d -o 1000 -g 1000 /srv/cato/chrome-profile /srv/cato/steamdb-historical
install -d /srv/cato/caddy-data /srv/cato/caddy-config /srv/cato/seq

# SSH for people (`ssh catoptric`), over Tailscale only: the security group has no inbound rule.
install -d -m 700 -o ec2-user -g ec2-user /home/ec2-user/.ssh
echo 'ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIFBMo/bTHJKFae5qce2pWdJ7b65anSpMPoKkCFZLBhbO catoptric-key' \
  >> /home/ec2-user/.ssh/authorized_keys
chown ec2-user:ec2-user /home/ec2-user/.ssh/authorized_keys
chmod 600 /home/ec2-user/.ssh/authorized_keys

command -v aws >/dev/null || dnf install -y awscli-2

# The fixed public IP: the Steamworks partner login has only ever seen one address.
IMDS=http://169.254.169.254/latest
TOKEN=$(curl -fsS -X PUT "$IMDS/api/token" -H "X-aws-ec2-metadata-token-ttl-seconds: 300")
INSTANCE_ID=$(curl -fsS -H "X-aws-ec2-metadata-token: $TOKEN" "$IMDS/meta-data/instance-id")
aws ec2 associate-address --region ${region} --instance-id "$INSTANCE_ID" \
  --allocation-id ${eip_allocation_id} --allow-reassociation

# Tailscale: people reach Caddy here, and the node routes the VPC into the tailnet (the gate
# tool and SSM-free debugging). Machines never use a Tailscale IP to reach each other.
# Tailscale's install.sh does not support Amazon Linux 2023 ("isn't supported by this script
# yet", 2026-10-22, and set -e stopped the boot here); its package repository does.
curl -fsSL https://pkgs.tailscale.com/stable/amazon-linux/2023/tailscale.repo -o /etc/yum.repos.d/tailscale.repo
dnf install -y tailscale
systemctl enable --now tailscaled
echo 'net.ipv4.ip_forward = 1' > /etc/sysctl.d/99-tailscale.conf
sysctl -p /etc/sysctl.d/99-tailscale.conf
AUTHKEY=$(aws secretsmanager get-secret-value --region ${region} --secret-id ${tailscale_secret} \
  --query SecretString --output text)
tailscale up --authkey="$AUTHKEY" --hostname=cato-edge --advertise-routes=${vpc_cidr} --accept-dns=false
unset AUTHKEY
