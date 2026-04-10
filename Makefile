.PHONY: build run migrate migration db-update infra up down restore clean ts-env

# Build the solution
build:
	dotnet build

# Start infrastructure (PostgreSQL + RabbitMQ)
infra:
	docker compose up -d postgres rabbitmq

# Run the API (starts infra first)
run: infra
	dotnet run --project src/Cato.API

# Create a new migration (usage: make migration NAME=MigrationName)
migration:
	@if [ -z "$(NAME)" ]; then echo "Usage: make migration NAME=MigrationName"; exit 1; fi
	dotnet ef migrations add $(NAME) --project src/Cato.Infrastructure --startup-project src/Cato.API

# Apply pending migrations
db-update:
	dotnet ef database update --project src/Cato.Infrastructure --startup-project src/Cato.API

# Remove the last migration
migration-remove:
	dotnet ef migrations remove --project src/Cato.Infrastructure --startup-project src/Cato.API

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
