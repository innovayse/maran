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
#   scripts/maran migrate status                               every migration on disk is applied to this database
#   scripts/maran migrate guard [base-ref]                     no migration destroys a column the last release read
#
# `check` is the one that runs in CI. An entity edited without a migration is not an error anywhere
# until a real database is involved, and then it surfaces as a confusing query failure rather than
# as the thing that actually happened: somebody changed the model and did not say so in a file.
#
# `check` compares the MODEL to the migration FILES and never opens a database, so it cannot know
# whether those migrations were ever applied. That gap had a cost: a developer database four
# migrations behind — missing the whole Sites and Ssl schemas — sat behind a green MIGRATIONS-OK,
# and surfaced as HTTP 500 on the sign-in screen, `column u.FailedLoginAttempts does not exist`,
# three layers from the cause. `status` is the half `check` structurally cannot do. It is separate
# because it needs a reachable database and `check` must keep running in CI where there is none;
# a check that silently passes when it could not run is the failure this file already warns about
# twice.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"

# `guard` is dispatched HERE, above the toolchain setup, and returns without reaching it. It reads
# migration files as text and needs no SDK, no EF tool and no database; paying for `dotnet tool
# restore` to run a grep would make the cheapest gate in CI the slowest.
#
# WHAT IT ENFORCES — expand-contract. The installer promises an update is "reversible with an
# automatic database dump and a rollback command", and modules migrate independently while
# Wolverine keeps its queues in the same database. Rolling the CODE back one version is cheap;
# rolling the SCHEMA back means restoring a dump, which discards every message in flight and every
# row written since. The promise is only keepable if the old code still runs against the new
# schema — so a migration MUST NOT destroy or rename what the previous release reads. Removal is a
# separate release, after the code that read it has been gone for one.
#
# Destroying and renaming are the same act here: a renamed column is, to the previous release, a
# column that vanished. So `DropColumn`, `DropTable`, `RenameColumn` and `RenameTable` are all
# refused. `AlterColumn` is refused too, because the widening cases are indistinguishable from the
# narrowing ones by name alone and a narrowing is a silent data loss the previous release cannot
# survive; a genuine widening is exactly the case the marker exists for.
#
# THE MARKER. The rule asks one question — does any release that a customer might roll back to read
# what this migration destroys? A migration that answers it says so in its own file:
#
#   // contract-phase: PanelTasks.LegacyKind, expanded in 1.4, unread since 1.5
#   // contract-phase: Plans.MaxFtpUsers, never shipped — no released version reads it
#
# Both forms are the same assertion: a person has checked, and the answer is no. The second is the
# honest one before 1.0, when there is no earlier release to be compatible with; it stops being
# available the day one exists, and the check cannot tell the difference — the reviewer can.
#
# It lives in the migration rather than in the pull request on purpose — the file outlives the PR,
# and a reviewer reading the migration a year later can see that the removal was planned rather
# than reconstruct it from a merged discussion.
if [ "${1:-}" = "guard" ]; then
  shift

  # What to compare against. In CI the useful base is the merge point with the default branch, so
  # the check sees the migrations THIS branch adds and not the whole history. Locally, with no such
  # ref, it falls back to the working tree against HEAD, which is what a developer has just written.
  base="${1:-}"
  if [ -z "$base" ]; then
    for candidate in origin/main main; do
      if git -C "$root" rev-parse --verify --quiet "$candidate" >/dev/null; then
        base="$candidate"
        break
      fi
    done
  fi

  if [ -n "$base" ] && git -C "$root" merge-base HEAD "$base" >/dev/null 2>&1; then
    range="$(git -C "$root" merge-base HEAD "$base")"
    changed="$(git -C "$root" diff --name-only "$range" -- '*/Persistence/Migrations/*.cs')"
  else
    echo "migrate guard: no base ref found, checking uncommitted changes only" >&2
    changed="$(git -C "$root" diff --name-only HEAD -- '*/Persistence/Migrations/*.cs')"
  fi

  # The diff supplies the FILE LIST and nothing else; the check itself reads each file. That split
  # is not fussiness — it is the only way to tell `Up` from `Down`. Every `Down` drops what its own
  # `Up` created, so a diff-only check reports each initial schema as destroying six tables, and a
  # check whose output is mostly noise is a check people learn to skip. Only `Up` runs on a
  # customer's database going forward, so only `Up` is read here.
  offences=""
  for file in $changed; do
    case "$file" in
      *.Designer.cs|*ModelSnapshot.cs) continue ;;
    esac
    [ -r "$root/$file" ] || continue

    found="$(awk '
      # Remember the indentation of the Up signature: the method ends at a closing brace in that
      # same column. Migrations are machine-written and consistently formatted, and matching the
      # column rather than counting braces keeps this readable.
      /(^|[[:space:]])void Up\(/ {
        match($0, /^[[:space:]]*/)
        closing = substr($0, 1, RLENGTH) "}"
        inside = 1
        next
      }
      inside && $0 == closing { inside = 0; next }
      inside && /migrationBuilder\.(DropColumn|DropTable|RenameColumn|RenameTable|AlterColumn)/ {
        line = $0
        sub(/^[[:space:]]*/, "", line)
        print "    " line
      }
    ' "$root/$file")"

    [ -z "$found" ] && continue
    grep -q "contract-phase:" "$root/$file" && continue

    offences="$offences$file
$found
"
  done

  if [ -n "$offences" ]; then
    echo "MIGRATIONS-DESTRUCTIVE — these migrations remove or rewrite what the previous release reads:" >&2
    printf '%s' "$offences" >&2
    echo >&2
    echo "Expand now, contract later: add the new shape, leave the old one in place, and delete it in a" >&2
    echo "release after the code that read it is gone. If this IS that later release, say so in the" >&2
    echo "migration file:" >&2
    echo "    // contract-phase: <what is being removed>, expanded in <version>, unread since <version>" >&2
    exit 1
  fi

  echo "MIGRATIONS-ADDITIVE"
  exit 0
fi

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
  echo "usage: maran migrate {add <Module> <Name>|apply <Module>|list <Module>|check|status|guard [base-ref]}" >&2
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

    # A module may legitimately own NO persistence: the Cron module keeps none, because the
    # account's crontab is the record rather than a panel table. Asking EF about a context that does
    # not exist answers with a tool error, which this loop reports as COULD NOT CHECK — a check
    # whose failure mode lies, which is the very thing the comment above warns against. Skipped by
    # the ABSENCE OF A DbContext FILE under the module, not by a name list: a context that was
    # renamed or moved is then still reported rather than silently skipped.
    if ! find "$(dirname "$project_file")" -name '*DbContext.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print -quit | grep -q .; then
      echo "no persistence — nothing to migrate"
      continue
    fi

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

# `status` answers the question `check` cannot: is every migration ON DISK actually APPLIED to the
# database this developer is pointed at.
#
# It demands POSITIVE EVIDENCE that a database was reached, and the first version of this command
# did not — which is why the rule is written here rather than assumed. `dotnet ef migrations list`
# tries to connect, and when it cannot it logs the failure and then prints the migration list from
# the files anyway, with no "(Pending)" marker and no error on stdout. So the obvious reading —
# "no (Pending) means everything is applied" — reports success from a run that never asked
# anything. Verified: with the database container stopped, the first version printed every module
# as "applied" and exited 0.
#
# `-v` is therefore not optional. Only the verbose log distinguishes "connected, nothing pending"
# from "could not connect, here are the filenames", and this command exists precisely because the
# second must never read as the first.
if [ "$command_name" = "status" ]; then
  behind=0
  for project_file in "$root"/backend/src/Maran.Modules/*/Maran.Modules.*.csproj; do
    name="$(basename "$(dirname "$project_file")")"
    printf '%-12s ' "$name"

    # A module may legitimately own NO persistence: the Cron module keeps none, because the
    # account's crontab is the record rather than a panel table. Asking EF about a context that does
    # not exist answers with a tool error, which this loop reports as COULD NOT CHECK — a check
    # whose failure mode lies, which is the very thing the comment above warns against. Skipped by
    # the ABSENCE OF A DbContext FILE under the module, not by a name list: a context that was
    # renamed or moved is then still reported rather than silently skipped.
    if ! find "$(dirname "$project_file")" -name '*DbContext.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print -quit | grep -q .; then
      echo "no persistence — nothing to migrate"
      continue
    fi

    output="$(ef migrations list --project "$project_file" --context "${name}DbContext" -v 2>&1 || true)"

    if printf '%s' "$output" | grep -qE "An error occurred using the connection|Failed to connect|password authentication failed|database \"[^\"]*\" does not exist"; then
      echo "UNKNOWN — the database could not be reached, so nothing was checked:"
      printf '%s' "$output" | grep -E "Failed to connect|password authentication failed|does not exist" | head -2 | sed 's/^/    /'
      behind=$((behind + 1))
      continue
    fi

    if ! printf '%s' "$output" | grep -q "Opening connection to database"; then
      echo "UNKNOWN — no evidence this run opened a connection at all:"
      printf '%s' "$output" | tail -2 | sed 's/^/    /'
      behind=$((behind + 1))
      continue
    fi

    if printf '%s' "$output" | grep -q "(Pending)"; then
      echo "BEHIND — migrations exist that this database has never had:"
      printf '%s' "$output" | grep "(Pending)" | sed 's/^/    /'
      echo "    run: maran migrate apply $name"
      behind=$((behind + 1))
      continue
    fi

    echo "applied"
  done

  [ "$behind" -gt 0 ] && exit 1
  echo "MIGRATIONS-APPLIED"
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
