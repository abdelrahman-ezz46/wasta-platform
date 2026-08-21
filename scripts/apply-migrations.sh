#!/usr/bin/env bash
# Regenerates the idempotent migration SQL for both modules and applies it.
#
# Idempotent scripts are safe to re-run: each migration is wrapped in a check
# against __EFMigrationsHistory, so already-applied ones are skipped. That
# makes this the same command for a fresh database and an existing one.
#
# Usage:
#   ./scripts/apply-migrations.sh                      # local docker-compose Postgres
#   DATABASE_URL="Host=...;Database=...;..." ./scripts/apply-migrations.sh
set -euo pipefail

cd "$(dirname "$0")/.."

PGHOST="${PGHOST:-localhost}"
PGPORT="${PGPORT:-55432}"
PGDATABASE="${PGDATABASE:-wasta}"
PGUSER="${PGUSER:-postgres}"
PGPASSWORD="${PGPASSWORD:-wasta_local_dev}"
export PGPASSWORD

mkdir -p docs/sql

echo "Generating migration SQL..."
dotnet ef migrations script --idempotent --project src/Wasta.CareerCoach --output docs/sql/careercoach.sql
dotnet ef migrations script --idempotent --project src/Wasta.SupportChat --output docs/sql/supportchat.sql

# The two module scripts above are generated from their own design-time
# factories, which hard-code a local connection string. That is harmless when
# only generating SQL, but `dotnet ef database update` against them needs
# --connection passed explicitly.

# The platform context keeps its own history table. It uses the snake_case
# naming convention and the two AI modules do not, so one shared
# __EFMigrationsHistory would have columns that are snake_case to one context
# and PascalCase to the others - which fails the moment the second one runs.
dotnet ef migrations script --idempotent \
    --project src/Wasta.Infrastructure --startup-project src/Wasta.Infrastructure \
    --output docs/sql/platform.sql

apply() {
  local file="$1"
  echo "Applying $file..."
  if command -v psql >/dev/null 2>&1; then
    psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -v ON_ERROR_STOP=1 -q -f "$file"
  else
    # No local psql: fall back to the one inside the compose container.
    docker compose exec -T postgres psql -U "$PGUSER" -d "$PGDATABASE" -v ON_ERROR_STOP=1 -q < "$file"
  fi
}

apply docs/sql/careercoach.sql
apply docs/sql/supportchat.sql
apply docs/sql/platform.sql

echo "Done. Both modules' schemas are up to date."
