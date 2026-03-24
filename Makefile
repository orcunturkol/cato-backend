.PHONY: build run migrate migration db-update infra up down restore clean

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
