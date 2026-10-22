# Phase 2: AWS foundation (ECS)

Written 2026-10-21 during the Phase 1 soak. **Not applied.** `terraform plan` (read-only):
143 to add, 0 to change, 0 to destroy. Apply only after the Phase 1 gate passes and the user
approves the cost below (plan "Phase 2", step 5).

## What it creates

| Part | Resources |
|---|---|
| Network | 10 security groups (inbound only from other CATO groups), Postgres rules in `cato-rds-sg`, Cloud Map namespace `cato.internal` with `api`, `worker`, `frontend`, `ogt-api`, `ogt-frontend` and a `seq` record (today's host until Step D3) |
| Images and logs | ECR: `cato-backend`, `cato-frontend`, `cato-collector`, `cato-caddy`, `ogt-api`, `ogt-frontend` (newest 20 kept); log groups `/cato/<service>`, 30 days |
| Secrets and IAM | empty flat secrets for ECS injection (`cato/edge/tailscale-auth-key`, `cato/caddy/duckdns-token`, `cato/ogt/db-connection`, `cato/ogt/invite-code`, `cato/seq/admin-password`); generated `cato/mq/app-user` and `cato/cache/connection`; one task role per service with only its own secrets; the edge node role |
| Data stores | Amazon MQ RabbitMQ 4.3, `mq.m7g.medium`, single instance, private; ElastiCache Valkey 8.1 `cache.t4g.micro`, TLS + auth token; maintenance Sunday 07:00 UTC |
| Compute | ECS cluster `cato` (Fargate + the edge capacity provider); edge node: one `t3.large` in an ECS-managed group (min = max = 1), Elastic IP, Tailscale subnet router (`edge-user-data.sh`); 11 services and the `cato-migrate` task definition |

Running after apply: `api` and `frontend` (desired 1). **At 0:** `ogt-api` and `ogt-frontend`
(ogt-api runs `WatchlistCheckService`, which must not run twice against the OGT database), `worker`
and the four agents (the ECS api uses the production database: an ECS agent would take
dispatches from the host's agents, and a second worker is a second Steam login and dispatcher),
`caddy` and `seq` (Step D3). Phase 3 raises them one step at a time (`desired_counts`).

## Cost (eu-central-1, on-demand, AWS price list 2026-10-21; 730 h/month)

| Item | Price | Phase 2 | After Phase 3 |
|---|---|---|---|
| Amazon MQ `mq.m7g.medium` single instance (no `t3.micro` for RabbitMQ) | $0.1638/h | $120 | $120 |
| Edge node `t3.large` + 60 GB gp3 | $0.096/h | $76 | $76 |
| Fargate (vCPU $0.04656/h, GB $0.00511/h): api 0.5/2, frontend 0.25/0.5 (ogt-api and ogt-frontend from Step A) | | $33 | +$22 |
| + worker 1/4, agent-light 0.25/1, agent-events 0.5/2 | | | $141 |
| Public IPv4 ($0.005/h): Phase 2 two tasks + EIP; Phase 3 seven tasks + EIP | | $11 | $29 |
| ElastiCache Valkey `cache.t4g.micro` | $0.0144/h | $11 | $11 |
| Secrets Manager (7 new × $0.40), Route 53 zone, ECR, CloudWatch Logs | | ~$6 | ~$12 |
| **Total** | | **~$257** | **~$389** |

RDS (`db.t4g.medium`, ~$54) and today's host (~$70 with disk and IP) are extra until the host
is stopped (two quiet weeks after Phase 3).

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
- The `cato-caddy` image needs a Caddyfile with `*.cato.internal` upstreams (Step D3).
- RDS backups run 03:00-04:00 UTC, inside the night jobs; consider moving the window.
