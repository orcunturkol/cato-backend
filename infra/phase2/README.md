# Phase 2: AWS foundation (ECS)

**Live.** Applied 2026-10-22; Phase 3 (Steps A-D3) moved every CATO and OGT service here by
2026-10-23. The old EC2 host was decommissioned 2026-10-24..28: stopped, its disk and Elastic IP
removed, its security group rules removed, the instance terminated. Its root disk survives only
as the archived snapshot `snap-0992206efc37b10e3` (restore steps in the infra split plan).
Deploy with the per-image scripts (below), never with `deploy.sh`.

## What it creates

| Part | Resources |
|---|---|
| Network | 10 security groups (inbound only from other CATO groups; from the internet only WireGuard, UDP 51820 on the edge node), Postgres rules in `cato-rds-sg`, Cloud Map namespace `cato.internal` with `api`, `worker`, `frontend`, `ogt-api`, `ogt-frontend` and a `seq` record (the edge node's private IP; apply again after the node is replaced) |
| Images and logs | ECR: `cato-backend`, `cato-frontend`, `cato-collector`, `cato-caddy`, `ogt-api`, `ogt-frontend`, `ogt-backup` (newest 20 kept); log groups `/cato/<service>`, 30 days |
| Secrets and IAM | flat secrets for ECS injection (`cato/caddy/duckdns-token`, `cato/caddy/admin-hash`, `cato/seq/admin-password`); `cato/ogt/config` (OGT, one JSON key per setting); `cato/edge/wireguard` (the WireGuard server key pair and devices, written by `wireguard-device.sh`); generated `cato/mq/app-user` and `cato/cache/connection`; one task role per service with only its own secrets; the edge node role |
| Data stores | Amazon MQ RabbitMQ 4.3, `mq.m7g.medium`, single instance, private; ElastiCache Valkey 8.1 `cache.t4g.micro`, TLS + auth token; maintenance Sunday 07:00 UTC |
| OGT backup and alarms | S3 bucket `cato-ogt-backups-<account>` (private, SSE-S3, dumps deleted after 35 days); task `cato-ogt-backup` (`pg_dump` of the OGT database only) started at 01:00 UTC by the EventBridge Scheduler schedule `cato-ogt-backup`; alarms `cato-ogt-backup-missing` and `cato-ogt-api-down` e-mail `cato-alerts` (`ogt_backup.tf`, `alarms.tf`) |
| Compute | ECS cluster `cato` (Fargate + the edge capacity provider); edge node: one `t3.large` in an ECS-managed group (min = max = 1), Elastic IP, WireGuard server (`edge-user-data.sh`, `edge-wireguard.sh`), SSM for an emergency shell; 11 services and the `cato-migrate` task definition |

Running now: every service at 1 (`desired_counts`). Fargate: `api`, `worker`, `frontend`,
`ogt-api`, `ogt-frontend`; `agent-light` and `agent-events` on `FARGATE_SPOT` (an agent stop
queues its run once more). Edge node: `caddy` (host network), `seq`, `agent-steamworks`,
`agent-steamdb`. Never more than one `worker` (two Steam logins, two dispatchers) or `ogt-api`
(`WatchlistCheckService` must run once).

## Deploying

Each script builds one image under its repo's short commit, applies the **whole** config of this
checkout and records the tag in `images.auto.tfvars`; commit that file afterwards. Merge your
branch into `main` and `git fetch` before you deploy: another session deploys from this repo too.

| Script | Rolls |
|---|---|
| `deploy-backend.sh` | `cato-backend`: `cato-migrate`, then `api` and `worker` |
| `deploy-frontend.sh` | `cato-frontend` |
| `deploy-collector.sh` | `cato-collector`: the four agents |
| `deploy-ogt.sh` | `ogt-api`, `ogt-frontend` (outside 14:00-15:00 UTC) |
| `deploy-ogt-backup.sh` | `ogt-backup`: the nightly backup task (the first run creates its repository first) |
| `deploy-caddy.sh` | `cato-caddy` (`Caddyfile.ecs`): https stops for the seconds Caddy needs to restart |

`deploy.sh` builds every image, OGT from a sibling checkout: do not use it.

## OGT backup

Every night at 01:00 UTC the schedule `cato-ogt-backup` starts the task of the same name: a
`pg_dump` of the OGT database only (custom format), uploaded to
`s3://cato-ogt-backups-<account>/ogt/ogt-<UTC time>.dump` and kept 35 days. The task logs
`ogt-backup: ok <key> <bytes>` to `/cato/ogt-api` (prefix `ogt-backup`); 25 hours without that
line e-mail `cato-alerts`. RDS's own backups (7 days) cover the whole instance, CATO too.

First deploy: `terraform plan` first, then `deploy-ogt-backup.sh` (the repository, the image,
then the whole config). Then run the task once by hand, so the backup alarm turns OK; it is in
alarm from its creation until the first dump:

    SG=$(terraform output -json service_security_groups | jq -r '."ogt-api"')
    SUBNETS=$(terraform output -json subnet_ids | jq -r 'join(",")')
    aws ecs run-task --cluster cato --task-definition cato-ogt-backup \
      --capacity-provider-strategy capacityProvider=FARGATE,weight=1 \
      --network-configuration "awsvpcConfiguration={subnets=[$SUBNETS],securityGroups=[$SG],assignPublicIp=ENABLED}"

Restore a dump into a new, empty database first and check it; never over the live one without
a fresh dump. Use `pg_restore` 16 or newer:

    aws s3 ls "s3://$(terraform output -raw ogt_backup_bucket)/ogt/"
    aws s3 cp "s3://$(terraform output -raw ogt_backup_bucket)/ogt/<file>.dump" ogt.dump
    createdb -h <host> -U <user> ogt_restore
    pg_restore --no-owner --no-privileges -h <host> -U <user> -d ogt_restore ogt.dump

RDS is reachable only from the CATO security groups: restore into RDS from a one-off task in the
`ogt-api` network. To put a restored database live, stop `ogt-api`, swap the databases (or the
connection string in `cato/ogt/config`) and start `ogt-api` again.

## People's access: WireGuard

People reach Caddy through WireGuard on the edge node: `*.catoptricgames.duckdns.org` resolves to
10.99.0.1 (wg0), and the devices connect to UDP 51820 on the Elastic IP. Tailscale was removed
on 2026-11-04; without WireGuard, the emergency shell is AWS SSM (`aws ssm send-command`). One key
per device, in three ranges:

| Role | Addresses | Reaches |
|---|---|---|
| admin | 10.99.0.2-254 | the whole edge node (SSH, Seq, CATO, OGT) and the VPC |
| web | 10.99.2.1-254 | both sites, CATO and OGT: ports 443 and 53 (DNS) on 10.99.0.1; not the node, Seq or the VPC |
| team | 10.99.1.1-254 | `https://ogt.catoptricgames.duckdns.org` only: ports 443 and 53 (DNS) on 10.99.0.1, and Caddy answers 403 for CATO |

```bash
infra/phase2/wireguard-device.sh init                # once: the server key pair
infra/phase2/wireguard-device.sh add <name> team     # or web, admin; writes ~/.config/cato-wireguard/<name>.conf, shows a QR code
infra/phase2/wireguard-device.sh remove <name>       # the node drops the device within a minute
infra/phase2/wireguard-device.sh list
```

The person installs the WireGuard app and imports the file (phone: the QR code). Send each config
privately; the device's private key exists only there. Device names live in the secret, never in
this repo. Each config sets `DNS = 10.99.0.1`: a dnsmasq on the edge node (`cato-wireguard-dns`,
only on wg0) gives the sites' names 10.99.0.1 and every other name from Cloudflare. Many home
routers drop public DNS answers with a private address (rebind protection; seen on 2026-11-04),
and a DNS server outside the tunnel fails where the OS sends a link's queries through that link
(systemd-resolved). While the tunnel is on, all of a device's DNS goes through the node. On Linux
with NetworkManager, send only the sites' names there after the import:
`nmcli connection modify <name> ipv4.dns-search "~catoptricgames.duckdns.org"`.

On the node, `cato-wireguard.timer` runs `/usr/local/sbin/cato-wireguard sync` every minute: it
loads the firewall (nftables table `inet cato_wg`; its drops are final whatever Docker's chains
accept) and applies the secret to wg0. If the firewall does not load, wg0 goes
down. `edge-user-data.sh` installs it on a new node; on the running node, once:

```bash
scp infra/phase2/edge-wireguard.sh catoptric:/tmp/ && ssh catoptric \
  sudo bash /tmp/edge-wireguard.sh install eu-central-1 cato/edge/wireguard 172.31.0.0/16
```

Checks: `sudo wg show` and `journalctl -u cato-wireguard` on the node. A device that has the
tunnel but cannot open the site: `nslookup ogt.catoptricgames.duckdns.org` must give 10.99.0.1,
and the config must have `DNS = 10.99.0.1`; on the node, `systemctl status cato-wireguard-dns`.
Cost: the secret ($0.40) and ~43,000 reads a month (~$0.22).

## Cost (eu-central-1, on-demand, AWS price list 2026-10-21; 730 h/month)

| Item | Price | Phase 2 | After Phase 3 |
|---|---|---|---|
| Amazon MQ `mq.m7g.medium` single instance (no `t3.micro` for RabbitMQ) | $0.1638/h | $120 | $120 |
| + its fixed broker storage (~196 GB, measured 2026-10-24; not in the first estimate) | | $23 | $23 |
| Edge node `t3.large` + 60 GB gp3 | $0.096/h | $76 | $76 |
| Fargate (vCPU $0.04656/h, GB $0.00511/h): api 0.5/2, frontend 0.25/0.5 (ogt-api and ogt-frontend from Step A) | | $33 | +$22 |
| + worker 1/4, agent-light 0.25/1, agent-events 0.5/2 | | | $141 |
| agent-light and agent-events on Fargate Spot since 2026-10-24 (about 70 % less; the price changes) | | | −$25 |
| Public IPv4 ($0.005/h): Phase 2 two tasks + EIP; Phase 3 seven tasks + EIP | | $11 | $29 |
| ElastiCache Valkey `cache.t4g.micro` | $0.0144/h | $11 | $11 |
| Secrets Manager (8 new × $0.40), Route 53 zone, ECR, CloudWatch Logs | | ~$6 | ~$12 |
| OGT backup (2026-11): S3, a 2-minute Fargate task a night, 1 custom metric, 2 alarms | | | <$1 |
| **Total** | | **~$280** | **~$387** |

RDS (`db.t4g.medium` with storage and backups, ~$59) is extra. The old host is gone (stopped
2026-10-24, terminated 2026-10-28); its archived root snapshot costs about $2 a month.

The sections below are the history of the first apply and the cutover; they no longer describe
work to do.

## Before `apply`

1. The Phase 1 gate passed; the user approved the cost.
2. `infra/rds`: `terraform apply` first (it only imports the live database, subnet group and
   security group into state; its plan says 5 to import, 0 to add, 0 to destroy, and an in-place
   update of Terraform-only fields). From then on the RDS group's rules are separate resources,
   so this stack can add its own.
3. In the Tailscale admin console: a reusable, tagged auth key (`tag:cato-edge`) with the VPC
   route approved, put into `cato/edge/tailscale-auth-key` after apply (set_secret.py).

## Apply, then

1. `terraform apply` (creates the empty secrets; the edge node boots and waits for the
   Tailscale key: put it into the secret, then replace the instance with an instance refresh).
2. Seed the flat secrets from `cato/server/compose-env` with set_secret.py (user approval).
3. Build and push the images (`deploy.sh`), run `cato-migrate` once (the same commit as the
   host, so it changes nothing), and check the four running services are healthy.
4. Phase 2 gate: services healthy, `*.cato.internal` resolve from a task, the security group
   paths open, a test alert from AWS on both channels.

## Phase 3 prerequisites (found while writing this)

Status 2026-10-22: done except OGT's data-protection keys and the Caddy image. Step A: `STEP_A.md`.

- Collector branches to merge after the gate: `feat/collector-amqps` (RABBITMQ_URI, AMQPS with
  certificate and host-name checks), `feat/collector-state-in-redis` (financial and sale
  highwatermarks, proxy scoreboard in Redis; seeds from the files once),
  `feat/collector-dockerfile` (the agent image).
- Pin `requirements.txt` (it uses `>=`) before the first image goes to ECR.
- The agent id is `group@hostname`; every ECS task has a new hostname, so after a deploy the old
  id stays "seen this week" and the `agent_offline` rule fires. Fix before Step B.
- Step A copies Redis to Valkey, including the three `cato:collector:*` keys; without the
  financial key the light agent re-syncs every sale date. Since 2026-11 (`feat/multi-partner`) the
  cursor is per Steamworks partner, `cato:collector:financial:highwatermark:<partnerId>`; the old
  key seeds 216530 once and stays as the rollback. A missing partner key re-syncs only that
  partner (337008: 467 dates).
- Seq on today's host listens on 127.0.0.1 and the Tailscale IP only: bind it to the private IP
  and open 5341 from the ECS groups, or ECS logs go to CloudWatch only until Step D3.
- OGT keeps its data-protection keys in a container volume; on Fargate they are lost at every
  restart (logins end). Move them to the database or Redis (plan item 1d, Phase 3 part).
- The `cato-caddy` image carries `Caddyfile.ecs` (`*.cato.internal` upstreams, `:443` on all
  interfaces of the edge node; `cato-edge-sg` has no inbound rule, so only the tailnet reaches
  it). Before Step D3, put the host's `CATO_ADMIN_HASH` (from `.env`, not compose-env) into
  `cato/caddy/admin-hash` with set_secret.py; the caddy task does not start without it.
- RDS backups run 03:00-04:00 UTC, inside the night jobs; consider moving the window.
