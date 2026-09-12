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

  # THE FILE SET, and why it is a union of three questions rather than one diff.
  #
  # A migration is in scope when it is not yet part of the base the previous release was cut from —
  # that is, when this branch adds it OR the working tree has it and no commit does. Committed
  # history before the base is deliberately OUT of scope: it has already shipped, its shape is what
  # the previous release reads, and re-litigating it would make the gate fail on files nobody can
  # change without rewriting a release. So the set is:
  #
  #   1. `diff --name-only <merge-base>` — every migration this branch adds or edits. What CI wants.
  #   2. `diff --name-only HEAD`         — working-tree edits to a file the branch did not add.
  #   3. `ls-files --others`             — UNTRACKED files.
  #
  # (3) is the whole reason this block was rewritten. `git diff` in every form reports only paths
  # git already knows, and `dotnet ef migrations add` writes a NEW file — so a freshly generated
  # migration is invisible to the gate for exactly as long as it is most likely to be wrong, which
  # is before anybody has committed it. Measured: with two untracked migrations in the tree, one of
  # them deleting rows, this command printed MIGRATIONS-ADDITIVE having read neither.
  #
  # Behaviour of this set:
  #   fresh clone  — (2) and (3) are empty; the verdict is over what the branch adds vs the base.
  #                  On the base branch itself the set is empty and the command says so (see the
  #                  NONE-TO-CHECK verdict below) rather than claiming additivity over nothing.
  #   dirty tree   — the developer's just-generated, still-untracked migration is read, which is
  #                  the case the gate exists for and the one it used to miss.
  #   CI           — the PR checkout has origin/main, so (1) supplies the branch's migrations and
  #                  (3) is empty; a runner that fetched no base ref degrades to (2)+(3), which is
  #                  narrower but never silently wider, and prints the warning below.
  #
  # Cost is bounded by the change set, not by history: a clean tree on the base branch reads zero
  # files, and the largest run so far reads two dozen. Duplicates across the three sources are
  # removed by `sort -u` so a file is never reported twice.
  if [ -n "$base" ] && git -C "$root" merge-base HEAD "$base" >/dev/null 2>&1; then
    range="$(git -C "$root" merge-base HEAD "$base")"
    committed="$(git -C "$root" diff --name-only "$range" -- '*/Persistence/Migrations/*.cs')"
  else
    echo "migrate guard: no base ref found, checking the working tree only" >&2
    committed=""
  fi

  changed="$(
    {
      printf '%s\n' "$committed"
      git -C "$root" diff --name-only HEAD -- '*/Persistence/Migrations/*.cs'
      git -C "$root" ls-files --others --exclude-standard -- '*/Persistence/Migrations/*.cs'
    } | sed '/^$/d' | sort -u
  )"

  # The diff supplies the FILE LIST and nothing else; the check itself reads each file. That split
  # is not fussiness — it is the only way to tell `Up` from `Down`. Every `Down` drops what its own
  # `Up` created, so a diff-only check reports each initial schema as destroying six tables, and a
  # check whose output is mostly noise is a check people learn to skip. Only `Up` runs on a
  # customer's database going forward, so only `Up` is read here.
  offences=""
  offending_files=0
  offending_statements=0
  read_count=0
  for file in $changed; do
    case "$file" in
      *.Designer.cs|*ModelSnapshot.cs) continue ;;
    esac
    [ -r "$root/$file" ] || continue
    read_count=$((read_count + 1))

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
      inside && $0 == closing { inside = 0; sql = 0; next }
      inside && /migrationBuilder\.(DropColumn|DropTable|RenameColumn|RenameTable|AlterColumn)/ {
        line = $0
        sub(/^[[:space:]]*/, "", line)
        print "    " line
      }

      # RAW SQL NEEDS A LATCH, NOT A PATTERN. `migrationBuilder.Sql(` is matched by the typed
      # builder rule above on no line at all, and a regex looking for a destructive verb ON the
      # `Sql(` line finds nothing either: every raw statement in this repository is a `"""` block,
      # so the call opens on one line and the verb arrives two lines later. Measured on
      # Identity/20260906134907_PasswordResetTokenUserForeignKey.cs, whose `DELETE FROM` sits on
      # line 21 while the call is on line 19. So the latch opens at the call and stays open until
      # the closing `);` of the statement, and every line in between is read.
      inside && /migrationBuilder\.Sql\(/ { sql = 1 }

      # WHAT COUNTS AS DESTRUCTIVE IN RAW SQL. The verb, never the fact of a raw call. A raw
      # `Sql(` is a normal and correct thing for a backfill to be —
      # Sites/20260901102440_SiteHostnameClaims.cs is an `INSERT … ON CONFLICT DO NOTHING` over
      # existing rows, it is committed, it is right, and a rule that refused raw SQL as such would
      # refuse it. What the previous release cannot survive is a statement that REMOVES what it
      # reads, so the match is on removal: DELETE, TRUNCATE, DROP, and an in-place ALTER COLUMN.
      #
      # KNOWN BLIND SPOT, stated here and in the verdict: an `UPDATE … SET` that rewrites rows in
      # place is NOT matched. It is excluded because `ON CONFLICT DO UPDATE SET` — the ordinary
      # shape of an idempotent backfill — would otherwise be refused on every upsert, and a gate
      # that cries on the common correct case is a gate people route around. A migration that
      # rewrites data in place still owes a `contract-phase:` line; that one is on the reviewer.
      sql {
        upper = toupper($0)
        if (upper ~ /DELETE[[:space:]]+FROM/ ||
            upper ~ /(^|[^A-Z_])TRUNCATE([^A-Z_]|$)/ ||
            upper ~ /(^|[^A-Z_])DROP[[:space:]]+(TABLE|COLUMN|CONSTRAINT|INDEX|SCHEMA|VIEW|TYPE|SEQUENCE)/ ||
            upper ~ /ALTER[[:space:]]+COLUMN/) {
          line = $0
          sub(/^[[:space:]]*/, "", line)
          print "    raw SQL: " line
        }
        if ($0 ~ /\);[[:space:]]*$/) { sql = 0 }
      }
    ' "$root/$file")"

    [ -z "$found" ] && continue
    grep -q "contract-phase:" "$root/$file" && continue

    offending_files=$((offending_files + 1))
    offending_statements=$((offending_statements + $(printf '%s\n' "$found" | wc -l)))
    offences="$offences$file
$found
"
  done

  # A COUNT, NOT AN EXIT CODE. Every verdict below names what was read and how much of it, because
  # the failure this command has already produced once is a green printed over zero examined files —
  # and an exit code cannot tell that apart from a green over two dozen. Every offending file is
  # collected and every offending statement inside it is printed: the loop does not stop at the
  # first finding, so one run tells a developer everything they have to fix.
  if [ -n "$offences" ]; then
    echo "MIGRATIONS-DESTRUCTIVE — $offending_statements statement(s) in $offending_files of $read_count migration(s) read remove or rewrite what the previous release reads:" >&2
    printf '%s' "$offences" >&2
    echo >&2
    echo "Expand now, contract later: add the new shape, leave the old one in place, and delete it in a" >&2
    echo "release after the code that read it is gone. If this IS that later release, say so in the" >&2
    echo "migration file:" >&2
    echo "    // contract-phase: <what is being removed>, expanded in <version>, unread since <version>" >&2
    exit 1
  fi

  # A verdict is never claimed over nothing. Zero migrations read is an honest NONE-TO-CHECK — the
  # ordinary state of the base branch and of any branch that touches no schema — and it is said in
  # those words so that it can never be read, in a log or a report, as "additive".
  if [ "$read_count" -eq 0 ]; then
    echo "MIGRATIONS-NONE-TO-CHECK — 0 migrations in the change set; no additivity claim is made."
    exit 0
  fi

  echo "MIGRATIONS-ADDITIVE — $read_count migration(s) read, 0 destructive statements."
  echo "    Scanned Up() for DropColumn/DropTable/RenameColumn/RenameTable/AlterColumn and, inside"
  echo "    migrationBuilder.Sql(...), for DELETE/TRUNCATE/DROP/ALTER COLUMN. UNOBSERVED HERE: an"
  echo "    UPDATE ... SET that rewrites rows in place."
  exit 0
fi

# shellcheck disable=SC1091
. "$root/scripts/dev"

# THE THIRD ANSWER, and where it comes from. `suite.sh` owns this harness's refusal vocabulary —
# `suite_require_toolchain`, `suite_did_not_run` and `$SUITE_STATUS_DID_NOT_RUN` (2) — and it is
# sourced rather than reimplemented so that "DID NOT RUN" means one thing in every script and a
# caller greps one pattern. It is a pure function library: nothing in it runs at source time.
# shellcheck disable=SC1091
. "$root/scripts/lib/suite.sh"

# THE GUARD RUNS HERE, and the position is the whole point: after `scripts/dev` has put the pinned
# SDK on PATH, and BEFORE the first command that touches it. Measured before this existed, in a
# fresh subshell whose PATH held no dotnet at all:
#
#     $ maran migrate check
#     scripts/lib/migrations.sh: line 248: dotnet: command not found      (on stderr)
#     0 bytes on stdout, status 127
#
# 127 is none of this harness's three answers, and a caller keeping only stdout was handed an empty
# file — the exact shape rules/testing.md records for `maran agent check` and `maran proto`. A
# toolchain guard is not only a message: nothing above it may be allowed to kill the script first.
if ! suite_require_toolchain backend "$root"; then
  suite_did_not_run "MIGRATIONS VERDICT" \
    "the .NET SDK this command needs is absent or too old, so no model, no migration file and no" \
    "database was read. This is the environment, not the schema."
  exit "$SUITE_STATUS_DID_NOT_RUN"
fi

# The EF Core CLI is pinned in backend/.config/dotnet-tools.json to the same version as the
# EF Core packages, and restored locally rather than installed globally.
#
# It used to be `dotnet tool install --global dotnet-ef`, which installs the LATEST — on a
# machine with the .NET 9 SDK that fetches the .NET 10 build of the tool, which then refuses to
# start ("You must install .NET to run this application"). It worked on a developer's machine
# that happened to have both runtimes and failed on a clean CI runner, which is the definition
# of a version that should have been written down.
# And its failure is a REFUSAL, not a finding. `set -e` used to end the script here, which is the
# same defect as the missing SDK one layer down: a restore that cannot reach the feed, or a manifest
# that will not parse, says nothing about this repository's schema.
if ! tool_restore_output="$(cd "$root/backend" && dotnet tool restore 2>&1)"; then
  printf '%s\n' "$tool_restore_output" | sed 's/^/    /' >&2
  suite_did_not_run "MIGRATIONS VERDICT" \
    "the pinned EF Core CLI could not be restored (output above), so the tool every command below" \
    "needs does not exist. Nothing was read."
  exit "$SUITE_STATUS_DID_NOT_RUN"
fi

# Runs the pinned tool. `dotnet ef` would find whatever is installed globally instead.
ef() {
  (cd "$root/backend" && dotnet tool run dotnet-ef "$@")
}

# strip_bom: removes the UTF-8 byte order mark from every .cs file in a directory that carries one.
#
# It rewrites only files that actually begin with EF BB BF, and it reports each one by name, so a
# run that changed nothing says nothing and a run that changed something is not silent about it.
strip_bom() {
  local directory="$1"
  [ -d "$directory" ] || return 0
  python3 - "$directory" <<'PYBOM'
"""Strips a leading UTF-8 BOM from every .cs file under a directory, naming the files it rewrote."""
import pathlib
import sys

BOM = b"\xef\xbb\xbf"
for path in sorted(pathlib.Path(sys.argv[1]).rglob("*.cs")):
    data = path.read_bytes()
    if not data.startswith(BOM):
        continue
    path.write_bytes(data[len(BOM):])
    print(f"stripped the UTF-8 BOM written by the generator: {path}")
PYBOM
}

usage() {
  echo "usage: maran migrate {add <Module> <Name>|apply <Module>|list <Module>|check|status|guard [base-ref]}" >&2
  exit 1
}

command_name="${1:-}"
module="${2:-}"
[ -z "$command_name" ] && usage

# `check` walks every module itself, so it is the one command that takes no module name.
# WHY TWO COUNTERS AND NOT ONE. `COULD NOT CHECK` used to increment the same counter as
# `MODEL CHANGED`, so both ended in `exit 1` — the command said the honest thing in words and the
# wrong thing in its status, and a caller scoring by status could not tell a missing SDK from a
# schema drift. Measured before this change, with the EF tool unable to start: twelve rows of
# `COULD NOT CHECK — this is a broken toolchain, not a model change`, status 1, and no verdict line
# of any shape.
#
# PRECEDENCE, argued because the mixed case is real. A module that reported MODEL CHANGED was
# MEASURED, and what it found is a defect in this tree; a module that could not be checked was not.
# So a finding wins: the run is red as a finding (1), and the unmeasured modules are named in the
# same breath rather than folded into it. Only when there is no finding at all does an unmeasured
# module decide the verdict, and then it is DID NOT RUN (2). This ordering can never turn a real
# drift into "just the environment", and can never turn a refusal into a pass.
if [ "$command_name" = "check" ]; then
  pending=0
  unmeasured=0
  unmeasured_modules=""
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
        unmeasured=$((unmeasured + 1))
        unmeasured_modules="$unmeasured_modules $name"
        ;;
    esac
  done

  # A LINE FOR EVERY ANSWER. The finding case used to print no summary line at all — only the
  # per-module rows and a bare `exit 1` — so a caller that scores on a printed verdict, which is how
  # every other gate here is scored, had nothing to read in the one case it most needed to.
  if [ "$pending" -gt 0 ]; then
    echo "MIGRATIONS-MODEL-CHANGED — $pending module(s) have a model that no migration records."
    if [ "$unmeasured" -gt 0 ]; then
      echo "    and$unmeasured_modules could not be checked at all, so this run is also INCOMPLETE:"
      echo "    the finding above is real, and the modules just named were not measured either way."
    fi
    exit 1
  fi

  if [ "$unmeasured" -gt 0 ]; then
    suite_did_not_run "MIGRATIONS VERDICT" \
      "$unmeasured module(s) —$unmeasured_modules — could not be checked (the tool errors are" \
      "printed above). No model was compared to any migration, so this is neither a drifted tree" \
      "nor a clean one."
    exit "$SUITE_STATUS_DID_NOT_RUN"
  fi

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
# The same two counters, for the same reason, and the same precedence: a module the database is
# demonstrably BEHIND on is a measured finding; a module whose database could not be reached is not.
# Both used to end in `exit 1`, and the UNKNOWN message went to stdout while the run that produced
# it was indistinguishable, to a caller, from a migration genuinely missing from the database.
if [ "$command_name" = "status" ]; then
  behind=0
  unknown=0
  unknown_modules=""
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
      unknown=$((unknown + 1))
      unknown_modules="$unknown_modules $name"
      continue
    fi

    if ! printf '%s' "$output" | grep -q "Opening connection to database"; then
      echo "UNKNOWN — no evidence this run opened a connection at all:"
      printf '%s' "$output" | tail -2 | sed 's/^/    /'
      unknown=$((unknown + 1))
      unknown_modules="$unknown_modules $name"
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

  if [ "$behind" -gt 0 ]; then
    echo "MIGRATIONS-BEHIND — $behind module(s) have migrations this database has never had."
    if [ "$unknown" -gt 0 ]; then
      echo "    and$unknown_modules could not be reached, so this run is also INCOMPLETE."
    fi
    exit 1
  fi

  if [ "$unknown" -gt 0 ]; then
    suite_did_not_run "MIGRATIONS VERDICT" \
      "$unknown module(s) —$unknown_modules — could not be asked (the errors are printed above)." \
      "Nothing was compared, so no module here is known to be applied or behind."
    exit "$SUITE_STATUS_DID_NOT_RUN"
  fi

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
    # `dotnet ef` writes every file it generates with a UTF-8 BOM, and .editorconfig says
    # `charset = utf-8` for this repository, so `maran format --check` reports `error CHARSET` on
    # each one. Two of the three files here — the `.Designer.cs` and the model snapshot — are files
    # nobody opens, so the byte order mark was being stripped by hand, one migration at a time, and
    # whichever migration its author forgot broke the format gate for everybody. The generator is
    # where it comes from, so the generator is where it goes.
    strip_bom "$root/backend/src/Maran.Modules/$module/Persistence/Migrations"
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
