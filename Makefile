.PHONY: build run infra up down clean ts-env \
	ef-tool migration db-update migration-remove migration-list \
	migration-check migration-script db-drop db-reset

# ── EF Core migration config ──
# Centralised so every target uses the same project layout.
EF_PROJECT       := src/Cato.Infrastructure
EF_STARTUP       := src/Cato.API
EF              := dotnet ef
EF_ARGS         := --project $(EF_PROJECT) --startup-project $(EF_STARTUP)
MIGRATIONS_DIR  := $(EF_PROJECT)/Database/Migrations

# Build the solution
build:
	dotnet build

# Start infrastructure (PostgreSQL + RabbitMQ)
infra:
	docker compose up -d postgres rabbitmq

# Run the API (starts infra first)
run: infra
	dotnet run --project $(EF_STARTUP)

# ──────────────────────────────────────────────
# Migrations
# ──────────────────────────────────────────────

# Ensure the dotnet-ef CLI is installed (idempotent)
ef-tool:
	@dotnet ef --version >/dev/null 2>&1 || dotnet tool install --global dotnet-ef
	@dotnet ef --version

# Create a new migration (usage: make migration NAME=MigrationName)
migration: ef-tool
	@if [ -z "$(NAME)" ]; then echo "Usage: make migration NAME=MigrationName"; exit 1; fi
	$(EF) migrations add $(NAME) $(EF_ARGS) --output-dir Database/Migrations

# Report whether the model has changes not yet captured in a migration.
# Exits non-zero when changes are pending (handy in CI / pre-commit).
migration-check: ef-tool
	$(EF) migrations has-pending-model-changes $(EF_ARGS)

# List all migrations and whether each is applied to the connected DB
migration-list: ef-tool
	$(EF) migrations list $(EF_ARGS)

# Apply pending migrations to the database
db-update: ef-tool
	$(EF) database update $(EF_ARGS)

# Remove the last (unapplied) migration
migration-remove: ef-tool
	$(EF) migrations remove $(EF_ARGS)

# Generate an idempotent SQL script for all migrations (usage: make migration-script [OUT=migrate.sql])
migration-script: ef-tool
	$(EF) migrations script --idempotent $(EF_ARGS) $(if $(OUT),--output $(OUT))

# Drop the database (DESTRUCTIVE — asks for confirmation)
db-drop: ef-tool
	$(EF) database drop $(EF_ARGS)

# Drop and recreate the database from scratch (DESTRUCTIVE)
db-reset: db-drop db-update

# Start all services
up:
	docker compose up -d

# Stop all services
down:
	docker compose down

# Clean build artifacts
clean:
	dotnet clean

# Detect Tailscale IP and write/update TAILSCALE_IP in .env
ts-env:
	@TS_IP=$$(tailscale ip -4 2>/dev/null); \
	if [ -z "$$TS_IP" ]; then echo "ERROR: Tailscale not running"; exit 1; fi; \
	if [ -f .env ]; then \
		sed -i "s|^TAILSCALE_IP=.*|TAILSCALE_IP=$$TS_IP|" .env; \
		sed -i "s|^VITE_API_URL=.*|VITE_API_URL=http://$$TS_IP:5039|" .env; \
	else \
		printf "TAILSCALE_IP=$$TS_IP\nVITE_API_URL=http://$$TS_IP:5039\nCOLLECTOR_DATA_PATH=/home/ofturkol/catoptric-data-collector/data\n" > .env; \
	fi; \
	echo "TAILSCALE_IP set to $$TS_IP"
