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

# The EF Core CLI is pinned in backend/.config/dotnet-tools.json to the same version as the
# EF Core packages, and restored locally rather than installed globally.
#
# It used to be `dotnet tool install --global dotnet-ef`, which installs the LATEST — on a
# machine with the .NET 9 SDK that fetches the .NET 10 build of the tool, which then refuses to
# start ("You must install .NET to run this application"). It worked on a developer's machine
# that happened to have both runtimes and failed on a clean CI runner, which is the definition
# of a version that should have been written down.
(cd "$root/backend" && dotnet tool restore >/dev/null)

# Runs the pinned tool. `dotnet ef` would find whatever is installed globally instead.
ef() {
  (cd "$root/backend" && dotnet tool run dotnet-ef "$@")
}

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

    # The OUTPUT decides, not the exit code alone. `dotnet ef` exits non-zero both when the
    # model has drifted and when it could not run at all — a missing tool, a build failure, a
    # context it cannot find. Treating those the same made this check report "model changed"
    # for a broken toolchain, which sends the reader to fix the wrong thing. A check whose
    # failure mode lies is worse than no check.
    output="$(ef migrations has-pending-model-changes \
      --project "$project_file" --context "${name}DbContext" 2>&1 || true)"

    case "$output" in
      *"No changes have been made to the model"*)
        echo "up to date"
        ;;
      *"Changes have been made to the model"*|*"pending model changes"*)
        echo "MODEL CHANGED WITHOUT A MIGRATION — run: maran migrate add $name <Name>"
        pending=$((pending + 1))
        ;;
      *)
        echo "COULD NOT CHECK — this is a broken toolchain, not a model change:"
        printf '%s\n' "$output" | sed 's/^/    /'
        pending=$((pending + 1))
        ;;
    esac
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
    ef migrations add "$name" \
      --project "$project" --context "$context" --output-dir Persistence/Migrations
    ;;
  apply)
    # The design-time factory's connection string is not the one used here: `--startup-project`
    # runs the host, so the migration lands in the same database the running panel uses.
    ASPNETCORE_ENVIRONMENT=Development ef database update \
      --project "$project" --startup-project "$root/backend/src/Maran.Host/Maran.Host.csproj" \
      --context "$context"
    ;;
  list)
    ASPNETCORE_ENVIRONMENT=Development ef migrations list \
      --project "$project" --startup-project "$root/backend/src/Maran.Host/Maran.Host.csproj" \
      --context "$context"
    ;;
  *)
    usage
    ;;
esac
