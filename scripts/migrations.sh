#!/usr/bin/env bash
# Manages EF Core migrations for the panel's module schemas. Each module owns a PostgreSQL schema
# and its own DbContext (rules/architecture.md), so every command takes a module name and targets
# that module's project.
#
# Migrations are never applied by a starting process — the installer and the update command apply
# them deliberately, after taking a dump (rules/architecture.md). This script is the developer's
# equivalent of that deliberate step.
#
# Usage:
#   scripts/migrations.sh add Accounts InitialAccountsSchema   create a migration
#   scripts/migrations.sh apply Accounts                       apply pending migrations locally
#   scripts/migrations.sh list Accounts                        show migrations and which are applied
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"

# shellcheck disable=SC1091
. "$root/scripts/dev-env.sh"

# dotnet tool install --global puts executables here; dev-env.sh deliberately does not add it,
# because only this script needs them.
export PATH="$HOME/.dotnet/tools:$PATH"

if ! command -v dotnet-ef >/dev/null 2>&1; then
  echo "installing the EF Core CLI"
  dotnet tool install --global dotnet-ef >/dev/null
fi

usage() {
  echo "usage: scripts/migrations.sh {add <Module> <Name>|apply <Module>|list <Module>}" >&2
  exit 1
}

command_name="${1:-}"
module="${2:-}"
[ -z "$command_name" ] && usage
[ -z "$module" ] && usage

project="$root/backend/src/Maran.Modules/$module/Maran.Modules.$module.csproj"
if [ ! -f "$project" ]; then
  echo "no such module: $module (expected $project)" >&2
  exit 1
fi

context="${module}DbContext"

case "$command_name" in
  add)
    name="${3:-}"
    [ -z "$name" ] && usage
    dotnet ef migrations add "$name" \
      --project "$project" --context "$context" --output-dir Persistence/Migrations
    ;;
  apply)
    # The design-time factory's connection string is not the one used here: `--startup-project`
    # runs the host, so the migration lands in the same database the running panel uses.
    ASPNETCORE_ENVIRONMENT=Development dotnet ef database update \
      --project "$project" --startup-project "$root/backend/src/Maran.Host/Maran.Host.csproj" \
      --context "$context"
    ;;
  list)
    ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations list \
      --project "$project" --startup-project "$root/backend/src/Maran.Host/Maran.Host.csproj" \
      --context "$context"
    ;;
  *)
    usage
    ;;
esac
