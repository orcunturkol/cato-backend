# Phase 2: AWS foundation (ECS)

**Live.** Applied 2026-10-22; Phase 3 (Steps A-D3) moved every CATO and OGT service here by
2026-10-23. The old EC2 host was decommissioned 2026-10-24..28: stopped, its disk and Elastic IP
removed, its security group rules removed, the instance terminated. Its root disk survives only
as the archived snapshot `snap-0992206efc37b10e3` (restore steps in the infra split plan).
Deploy with the per-image scripts (below), never with `deploy.sh`.

## What it creates

| Part | Resources |
|---|---|
| Network | 10 security groups (inbound only from other CATO groups), Postgres rules in `cato-rds-sg`, Cloud Map namespace `cato.internal` with `api`, `worker`, `frontend`, `ogt-api`, `ogt-frontend` and a `seq` record (the edge node's private IP; apply again after the node is replaced) |
| Images and logs | ECR: `cato-backend`, `cato-frontend`, `cato-collector`, `cato-caddy`, `ogt-api`, `ogt-frontend` (newest 20 kept); log groups `/cato/<service>`, 30 days |
| Secrets and IAM | flat secrets for ECS injection (`cato/edge/tailscale-auth-key`, `cato/caddy/duckdns-token`, `cato/caddy/admin-hash`, `cato/seq/admin-password`); `cato/ogt/config` (OGT, one JSON key per setting); generated `cato/mq/app-user` and `cato/cache/connection`; one task role per service with only its own secrets; the edge node role |
| Data stores | Amazon MQ RabbitMQ 4.3, `mq.m7g.medium`, single instance, private; ElastiCache Valkey 8.1 `cache.t4g.micro`, TLS + auth token; maintenance Sunday 07:00 UTC |
| Compute | ECS cluster `cato` (Fargate + the edge capacity provider); edge node: one `t3.large` in an ECS-managed group (min = max = 1), Elastic IP, Tailscale subnet router (`edge-user-data.sh`); 11 services and the `cato-migrate` task definition |

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

`deploy.sh` builds every image, OGT from a sibling checkout: do not use it.

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
  financial key the light agent re-syncs every sale date.
- Seq on today's host listens on 127.0.0.1 and the Tailscale IP only: bind it to the private IP
  and open 5341 from the ECS groups, or ECS logs go to CloudWatch only until Step D3.
- OGT keeps its data-protection keys in a container volume; on Fargate they are lost at every
  restart (logins end). Move them to the database or Redis (plan item 1d, Phase 3 part).
- The `cato-caddy` image carries `Caddyfile.ecs` (`*.cato.internal` upstreams, `:443` on all
  interfaces of the edge node; `cato-edge-sg` has no inbound rule, so only the tailnet reaches
  it). Before Step D3, put the host's `CATO_ADMIN_HASH` (from `.env`, not compose-env) into
  `cato/caddy/admin-hash` with set_secret.py; the caddy task does not start without it.
- RDS backups run 03:00-04:00 UTC, inside the night jobs; consider moving the window.
