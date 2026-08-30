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
#   scripts/maran migrate add Accounts InitialAccountsSchema   create a migration
#   scripts/maran migrate apply Accounts                       apply pending migrations locally
#   scripts/maran migrate list Accounts                        show migrations and which are applied
#   scripts/maran migrate check                                every module's model matches its migrations
#
# `check` is the one that runs in CI. An entity edited without a migration is not an error anywhere
# until a real database is involved, and then it surfaces as a confusing query failure rather than
# as the thing that actually happened: somebody changed the model and did not say so in a file.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"

# shellcheck disable=SC1091
. "$root/scripts/dev"

# dotnet tool install --global puts executables here; scripts/dev deliberately does not add it,
# because only this script needs them.
export PATH="$HOME/.dotnet/tools:$PATH"

if ! command -v dotnet-ef >/dev/null 2>&1; then
  echo "installing the EF Core CLI"
  dotnet tool install --global dotnet-ef >/dev/null
fi

usage() {
  echo "usage: maran migrate {add <Module> <Name>|apply <Module>|list <Module>|check}" >&2
  exit 1
}

command_name="${1:-}"
module="${2:-}"
[ -z "$command_name" ] && usage

# `check` walks every module itself, so it is the one command that takes no module name.
if [ "$command_name" = "check" ]; then
  pending=0
  for project_file in "$root"/backend/src/Maran.Modules/*/Maran.Modules.*.csproj; do
    name="$(basename "$(dirname "$project_file")")"
    printf '%-12s ' "$name"
    if dotnet ef migrations has-pending-model-changes \
      --project "$project_file" --context "${name}DbContext" >/dev/null 2>&1; then
      echo "up to date"
    else
      echo "MODEL CHANGED WITHOUT A MIGRATION — run: maran migrate add $name <Name>"
      pending=$((pending + 1))
    fi
  done

  [ "$pending" -gt 0 ] && exit 1
  echo "MIGRATIONS-OK"
  exit 0
fi

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
