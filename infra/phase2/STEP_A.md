# Step A runbook: backend and state to AWS

One maintenance window, about an hour; nobody uses CATO until the split is done. Afterwards
the worker, the API, the frontends and OGT run on ECS, Redis is Valkey and RabbitMQ is Amazon MQ.
The four job agents stay on today's host until Steps B-D2 and point at the new endpoints.

```
before:  host ── api · worker · frontend · ogt-* · redis · rabbitmq · caddy · seq · 4 agents
after:   ECS  ── api · worker (exactly 1) · frontend · ogt-api · ogt-frontend
         AWS  ── Valkey · Amazon MQ · RDS (unchanged)
         host ── caddy (upstreams *.cato.internal) · seq · 4 agents (REDIS_URL/RABBITMQ_URI/API → AWS)
```

## Before the window

- [ ] OGT keeps its data-protection keys in its database or Redis (not the `ogt_dpkeys`
      volume), merged and deployed; otherwise every ogt-api restart on Fargate ends all logins.
- [ ] Flat secrets set (set_secret.py, user approval): `cato/ogt/db-connection`,
      `cato/ogt/invite-code`, plus any new OGT keys (OGT's PR notes).
- [ ] Images in ECR for the commit to deploy: `cato-backend`, `cato-frontend`, `ogt-api`,
      `ogt-frontend` (deploy.sh builds them; OGT from its own main checkout).
- [ ] `cato/collector/config` gets two new keys, written in the window (step 6), not before:
      `REDIS_URL` = the `collector` value of `cato/cache/connection`, `RABBITMQ_URI` = the `uri`
      value of `cato/mq/app-user`. Until then the jobs use the host's Redis and RabbitMQ.
- [ ] The Phase 2 gate passed; the host Seq answers on its private IP (done 2026-10-22).

## In the window

1. **Stop new work.** Pause every dispatcher job (Jobs page, or `PUT /api/scheduled-jobs/{name}`
   with `enabled=false`, with a reason). Wait until `GET /api/job-agents` shows no current job.
2. **Drain.** `rabbitmqctl list_queues name messages` on the host: every queue 0. If the DLQ holds
   messages, export them first (`scripts/replay_ingestion.py` can send them again later).
3. **Stop the host backend:** `docker compose stop worker api`. The worker must be stopped before
   the ECS worker starts: two workers are two Steam logins and two dispatchers.
4. **Copy Redis to Valkey** from the host (its security group reaches the cache; the URL and auth
   token are in `cato/cache/connection`). `DUMP`/`RESTORE ... REPLACE` with the TTL, for:
   `steam:appids:*` (scores, the negative follower bands too), `steam:appids:*:failures`,
   `steam:appids:*:quarantine`, `steam:appid:names`, `steam:steamids:*` and the three
   `cato:collector:*` state keys (financial and sale highwatermarks, the SteamDB proxy
   scoreboard; without the financial key the next run re-syncs every sale date). Do **not** copy
   `cato:orchestrator:lock:*` or `cato-outputcache:*`. Compare key counts per pattern afterwards.
5. **Start the backend on ECS** (`deploy.sh <tag>` runs `cato-migrate` first, then rolls the
   services). Set `desired_counts`: `worker` 1, `api` 1, `frontend` 1, `ogt-api` 1, `ogt-frontend`
   1. Check: worker log "Logged in successfully", "Listening on queues", one consumer per queue
   on Amazon MQ, `/health/ready` Healthy on the worker (probe task, `scripts/gate-probe.sh`).
6. **Point the host's agents at AWS.** Write `REDIS_URL` and `RABBITMQ_URI` into
   `cato/collector/config` (set_secret.py; the jobs read it at every start) and set
   `CATO_API_BASE_URL=http://api.cato.internal:8080` in the collector `.env`, then restart the
   four agents (`systemctl restart cato-agent@{light,events,steamdb,steamworks}`; they read `.env`
   once). The host resolves `*.cato.internal` (VPC DNS) and its security group reaches the API.
7. **Point the host's Caddy at ECS:** in `Caddyfile`, `api:8080` → `api.cato.internal:8080`,
   `frontend:80` → `frontend.cato.internal:80`, `ogt-api:8080` → `ogt-api.cato.internal:8080`,
   `ogt-frontend:80` → `ogt-frontend.cato.internal:80` (use `dynamic a` as in `Caddyfile.ecs`);
   `docker compose up -d --no-deps caddy` (no `--build`: xcaddy takes 10+ minutes).
8. **Resume** the jobs (step 1 in reverse). Run now: `ccu` (light), one follower request, and
   `steamworks_navstats` (short). Check runs, batches on Amazon MQ (one consumer each), DLQ 0.
9. **Stop the old containers**, keep their volumes for rollback:
   `docker compose stop api worker frontend ogt-api ogt-frontend redis rabbitmq`.

## Rollback

Pause the jobs, wait for the agents to idle, scale every ECS service to 0, start the host's
containers again (`docker compose up -d`), remove `REDIS_URL`/`RABBITMQ_URI` from
`cato/collector/config`, put `CATO_API_BASE_URL` back to `http://localhost:5039`, restart the
agents, restore the old `Caddyfile`, resume. Progress written to Valkey after the cutover is lost;
that costs only re-scrapes. Drain Amazon MQ before switching back.

## After

- Gate (24 h soak, the CCU time-limit drill) before Step B.
- At decommission: remove the `old-host` rules (network.tf), `seq_on_old_host`, and the
  `launch-wizard-1` rule from `cato-rds-sg`.
