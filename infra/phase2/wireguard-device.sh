#!/usr/bin/env bash
# The WireGuard devices of the edge node, in the secret cato/edge/wireguard. The node applies
# the secret within a minute (edge-wireguard.sh).
#
#   infra/phase2/wireguard-device.sh init                     # once: the server key pair
#   infra/phase2/wireguard-device.sh add <name> admin|web|team
#   infra/phase2/wireguard-device.sh remove <name>
#   infra/phase2/wireguard-device.sh list
#
#   admin  10.99.0.2-254: the whole edge node (SSH, Seq, CATO, OGT) and the VPC
#   web    10.99.2.1-254: both sites, CATO and OGT, and the tunnel's DNS; not the node or the VPC
#   team   10.99.1.1-254: OGT only (https://ogt.catoptricgames.duckdns.org) and the tunnel's DNS
#
# One key per device: two devices on one key take the session from each other. add writes the
# config to $WG_CONFIG_DIR (default ~/.config/cato-wireguard, mode 600) and shows it as a QR
# code for the phone app; the device's private key is nowhere else. Give each config only to
# its person, privately. Device names stay in the secret, never in this repo.
# Needs the AWS CLI login, jq and openssl; qrencode for the QR code.
set -euo pipefail

REGION=eu-central-1
SECRET=cato/edge/wireguard
VPC_CIDR=172.31.0.0/16
DIR=${WG_CONFIG_DIR:-$HOME/.config/cato-wireguard}

die() { echo "$*" >&2 && exit 1; }
get() { aws secretsmanager get-secret-value --region "$REGION" --secret-id "$SECRET" --query SecretString --output text; }
put() { aws secretsmanager put-secret-value --region "$REGION" --secret-id "$SECRET" --secret-string "$1" >/dev/null; }

# A Curve25519 key pair in WireGuard's format: base64 of the raw 32 bytes, the end of openssl's DER.
keypair() {
  local pem
  pem=$(openssl genpkey -algorithm X25519)
  priv=$(openssl pkey -outform DER <<<"$pem" | tail -c 32 | base64)
  pub=$(openssl pkey -pubout -outform DER <<<"$pem" | tail -c 32 | base64)
}

init() {
  if get 2>/dev/null | jq -e '.server_private_key' >/dev/null 2>&1; then
    die "$SECRET already has a server key"
  fi
  keypair
  put "$(jq -n --arg k "$priv" --arg p "$pub" '{server_private_key: $k, server_public_key: $p, peers: []}')"
  echo "server key set; add the devices"
}

add() {
  local name=${1:-} role=${2:-} net first routes json ip conf tmp endpoint psk
  [[ $name =~ ^[a-z0-9][a-z0-9-]{0,31}$ ]] || die "name: up to 32 lower-case letters, digits and -"
  case $role in
    admin) net=10.99.0 first=2 routes="10.99.0.1/32, $VPC_CIDR" ;;
    web) net=10.99.2 first=1 routes=10.99.0.1/32 ;;
    team) net=10.99.1 first=1 routes=10.99.0.1/32 ;;
    *) die "role: admin, web or team" ;;
  esac
  json=$(get)
  jq -e --arg n "$name" 'any(.peers[]; .name == $n)' <<<"$json" >/dev/null && die "$name exists"
  conf=$DIR/$name.conf
  [ ! -e "$conf" ] || die "$conf exists"

  local used
  used=$(jq -r '.peers[].ip' <<<"$json")
  ip=
  for i in $(seq "$first" 254); do
    grep -qxF "$net.$i" <<<"$used" || { ip=$net.$i && break; }
  done
  [ -n "$ip" ] || die "no free address for $role"
  endpoint=$(aws ec2 describe-addresses --region "$REGION" --filters Name=tag:Name,Values=cato-edge \
    --query 'Addresses[0].PublicIp' --output text)
  [[ $endpoint =~ ^[0-9.]+$ ]] || die "no Elastic IP tagged cato-edge"

  keypair
  psk=$(openssl rand -base64 32)
  mkdir -p "$DIR" && chmod 700 "$DIR"
  tmp=$(mktemp "$DIR/.new.XXXXXX")
  # DNS: the dnsmasq on the edge node, inside the tunnel. Many home routers drop DNS answers with a
  # private address (10.99.0.1, rebind protection), and a server outside the tunnel fails on
  # systems that send a link's DNS queries through that link (systemd-resolved).
  cat >"$tmp" <<EOF
[Interface]
PrivateKey = $priv
Address = $ip/32
DNS = 10.99.0.1

[Peer]
PublicKey = $(jq -r .server_public_key <<<"$json")
PresharedKey = $psk
Endpoint = $endpoint:51820
AllowedIPs = $routes
PersistentKeepalive = 25
EOF
  put "$(jq --arg n "$name" --arg r "$role" --arg ip "$ip" --arg k "$pub" --arg s "$psk" \
    '.peers += [{name: $n, role: $r, ip: $ip, public_key: $k, preshared_key: $s}]' <<<"$json")" \
    || { rm -f "$tmp" && die "the secret did not change"; }
  mv "$tmp" "$conf"
  echo "added $name ($role, $ip); the edge node applies it within a minute"
  echo "config: $conf"
  if command -v qrencode >/dev/null; then qrencode -t ansiutf8 <"$conf"; fi
}

remove() {
  local name=${1:-} json
  json=$(get)
  jq -e --arg n "$name" 'any(.peers[]; .name == $n)' <<<"$json" >/dev/null || die "no device $name"
  put "$(jq --arg n "$name" '.peers |= map(select(.name != $n))' <<<"$json")"
  rm -f "$DIR/$name.conf"
  echo "removed $name; the edge node drops it within a minute"
}

list() {
  get | jq -r '.peers[] | [.name, .role, .ip] | @tsv' | column -t
}

case ${1:-} in
  init | add | remove | list) cmd=$1 && shift && "$cmd" "$@" ;;
  *) die "usage: $0 init | add <name> admin|web|team | remove <name> | list" ;;
esac
