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

# SSH for people (`ssh catoptric`), over admin WireGuard only: no TCP port is open. Emergency
# shell without WireGuard: AWS SSM (the instance role has AmazonSSMManagedInstanceCore).
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

# WireGuard: people reach Caddy on 10.99.0.1 (wg0); admins also reach the node and the VPC.
# edge-wireguard.sh installs the packages and a timer that applies ${wireguard_secret} every minute.
cat > /usr/local/sbin/cato-wireguard <<'WIREGUARD'
${wireguard_script}
WIREGUARD
chmod 755 /usr/local/sbin/cato-wireguard
/usr/local/sbin/cato-wireguard install ${region} ${wireguard_secret} ${vpc_cidr}
