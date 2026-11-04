#!/bin/bash
# WireGuard on the edge node: people reach Caddy on 10.99.0.1 (wg0), UDP 51820 on the Elastic IP.
#
#   cato-wireguard install <region> <secret id> <vpc cidr>   # once (edge-user-data.sh, or over ssh)
#   cato-wireguard sync                                       # the timer, every minute
#
# Two ranges, one key per device (infra/phase2/wireguard-device.sh writes the secret):
#   10.99.0.0/24 admin: the whole node (SSH, Seq, Caddy) and the VPC
#   10.99.1.0/24 team:  ports 443 and 53 on 10.99.0.1 only; Caddy serves the team OGT and nothing else
# DNS for the devices (DNS = 10.99.0.1 in their configs) is a dnsmasq on wg0: the sites' names give
# 10.99.0.1 (many home routers drop that private answer from public DNS), every other name comes
# from Cloudflare. A server inside the tunnel works however a device's OS sends its DNS queries.
# The firewall trusts the source address because WireGuard takes from a peer only the address
# in its AllowedIPs: one /32, built here from the secret's checked ip field, never copied.
#
# The firewall is its own nftables table. A drop there is final, whatever Docker's and
# Tailscale's iptables chains accept, and their restarts cannot reorder it. Seq (bridge, host
# port 5341) arrives through Docker's DNAT, so it takes the forward path, not input: both chains
# drop the team. The timer loads the table again every minute; if the load fails, wg0 goes down.
set -euo pipefail

SELF=/usr/local/sbin/cato-wireguard
CONF=/etc/wireguard/wg0.conf

install_node() {
  local region=$1 secret=$2 vpc=$3
  dnf install -y wireguard-tools nftables jq dnsmasq
  install -d -m 700 /etc/cato-wireguard
  printf 'REGION=%s\nSECRET=%s\nVPC_CIDR=%s\n' "$region" "$secret" "$vpc" >/etc/cato-wireguard/env
  [ "$(readlink -f "$0")" = "$SELF" ] || install -m 755 "$0" "$SELF"
  echo 'net.ipv4.ip_forward = 1' >/etc/sysctl.d/99-cato-wireguard.conf
  sysctl -p /etc/sysctl.d/99-cato-wireguard.conf

  # After Docker: the admin forward rules go into its DOCKER-USER chain.
  cat >/etc/systemd/system/cato-wireguard.service <<EOF
[Unit]
Description=WireGuard on the edge node: firewall, wg0 and the devices from Secrets Manager
After=docker.service network-online.target
Wants=network-online.target

[Service]
Type=oneshot
ExecStart=$SELF sync
EOF
  cat >/etc/systemd/system/cato-wireguard.timer <<'EOF'
[Unit]
Description=Apply the WireGuard firewall and devices every minute

[Timer]
OnBootSec=30
OnUnitActiveSec=60
# The default (1 min) lets a run start up to a minute late: a removed device would keep working.
AccuracySec=5s

[Install]
WantedBy=timers.target
EOF

  # Its own config file and unit: AL2023's /etc/dnsmasq.conf listens on lo with bind-interfaces.
  # Never the VPC resolver (no-resolv), so the devices do not see cato.internal.
  cat >/etc/cato-wireguard/dnsmasq.conf <<'EOF'
pid-file=
interface=wg0
except-interface=lo
bind-dynamic
no-resolv
no-hosts
server=1.1.1.1
server=1.0.0.1
address=/catoptricgames.duckdns.org/10.99.0.1
EOF
  cat >/etc/systemd/system/cato-wireguard-dns.service <<'EOF'
[Unit]
Description=DNS for the WireGuard devices on 10.99.0.1 (dnsmasq on wg0)
After=network-online.target
Wants=network-online.target

[Service]
ExecStart=/usr/sbin/dnsmasq --keep-in-foreground --conf-file=/etc/cato-wireguard/dnsmasq.conf
Restart=always

[Install]
WantedBy=multi-user.target
EOF
  systemctl daemon-reload
  systemctl enable cato-wireguard-dns.service
  systemctl restart cato-wireguard-dns.service
  systemctl enable --now cato-wireguard.timer
  # Fails until the secret has a server key (wireguard-device.sh init); the timer tries again.
  systemctl start cato-wireguard.service || echo "cato-wireguard: no wg0 yet, see journalctl -u cato-wireguard" >&2
}

# Called in an if: set -e is off in here, so every step returns on failure itself.
firewall() {
  nft -f - <<EOF || return 1
table inet cato_wg
delete table inet cato_wg
table inet cato_wg {
	chain input {
		type filter hook input priority filter - 10; policy accept;
		iifname "wg0" jump from_peers
	}

	chain from_peers {
		ct state established,related accept
		ip saddr 10.99.0.0/24 accept
		ip saddr 10.99.1.0/24 ip daddr 10.99.0.1 meta l4proto { tcp, udp } th dport { 53, 443 } accept
		limit rate 6/minute log prefix "cato-wg drop: "
		drop
	}

	# The team forwards nothing: no VPC, no Seq (Docker DNAT), no other device.
	chain forward {
		type filter hook forward priority filter - 10; policy accept;
		iifname "wg0" ip saddr 10.99.0.0/24 accept
		iifname "wg0" drop
	}

	chain postrouting {
		type nat hook postrouting priority srcnat; policy accept;
		ip saddr 10.99.0.0/24 ip daddr $VPC_CIDR masquerade
	}
}
EOF
  # iptables' FORWARD policy is DROP (Docker): admins to the VPC and the replies need an accept.
  iptables -N DOCKER-USER 2>/dev/null || true
  accept() { iptables -C DOCKER-USER "$@" 2>/dev/null || iptables -I DOCKER-USER 1 "$@"; }
  accept -i wg0 -s 10.99.0.0/24 -d "$VPC_CIDR" -j ACCEPT || return 1
  accept -o wg0 -d 10.99.0.0/24 -m conntrack --ctstate RELATED,ESTABLISHED -j ACCEPT || return 1
}

# A device counts only with an address in its role's range and well-formed keys.
VALID='
  def key: type == "string" and test("^[A-Za-z0-9+/]{43}=$");
  def valid: (.ip | type == "string" and test("^10\\.99\\.[01]\\.([1-9][0-9]?|1[0-9][0-9]|2[0-4][0-9]|25[0-4])$"))
    and .ip != "10.99.0.1"
    and ((.role == "admin" and (.ip | startswith("10.99.0."))) or (.role == "team" and (.ip | startswith("10.99.1."))))
    and (.public_key | key) and (.preshared_key | key);'

render() {
  jq -r "$VALID"'
    "[Interface]",
    "Address = 10.99.0.1/16",
    "ListenPort = 51820",
    "MTU = 1420",
    "PrivateKey = \(.server_private_key)",
    ((.peers // [])[] | select(valid) | "", "[Peer]", "PublicKey = \(.public_key)",
      "PresharedKey = \(.preshared_key)", "AllowedIPs = \(.ip)/32")'
}

sync_node() {
  # shellcheck disable=SC1091
  . /etc/cato-wireguard/env
  if ! firewall; then
    wg-quick down wg0 2>/dev/null || true
    echo "cato-wireguard: the firewall did not load, wg0 is down" >&2
    exit 1
  fi

  local json
  if ! json=$(aws secretsmanager get-secret-value --region "$REGION" --secret-id "$SECRET" \
    --query SecretString --output text) || ! jq -e "$VALID"' .server_private_key | key' >/dev/null <<<"$json"; then
    echo "cato-wireguard: no server key in $SECRET, the devices stay as they are" >&2
    exit 1
  fi
  jq -r "$VALID"' (.peers // [])[] | select(valid | not) | "cato-wireguard: skipped device \(.name // "?"): bad role, address or key"' \
    <<<"$json" >&2

  install -d -m 700 /etc/wireguard
  (umask 077 && render <<<"$json" >"$CONF.new")
  if cmp -s "$CONF.new" "$CONF"; then rm "$CONF.new"; else mv "$CONF.new" "$CONF" && echo "cato-wireguard: devices changed"; fi

  if ip link show wg0 >/dev/null 2>&1; then
    wg syncconf wg0 <(wg-quick strip wg0)
  else
    wg-quick up wg0
  fi
}

case ${1:-} in
  install) shift && install_node "$@" ;;
  sync) sync_node ;;
  *) echo "usage: $0 install <region> <secret id> <vpc cidr> | sync" >&2 && exit 2 ;;
esac
