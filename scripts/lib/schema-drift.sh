#!/usr/bin/env bash
# Answers one question about a running development database: does every migration on disk exist
# in that database's history? It reads the migration FILES and the `__EFMigrationsHistory` table
# of each module's schema, and names every migration the database has never had.
#
# WHY IT IS NOT `maran migrate status`. `status` asks EF, which means restoring a tool and
# starting a design-time host once per module — around sixteen dotnet invocations. That is the
# right price for a deliberate check and much too high for something a developer meets on every
# `maran dev`, and a gate people route around is a gate that reports nothing. This asks
# PostgreSQL directly, in one query per schema, so the common answer — no drift — costs no dotnet
# at all. `status` stays the authority: it also knows about migrations EF considers pending for
# reasons a filename cannot express, and this command says so rather than claiming to replace it.
#
# WHY IT EXISTS AT ALL. A panel that answers `{"status":"ok"}` against a schema fourteen
# migrations old is a check that cannot observe what it reports on. The panel does not apply
# migrations when it starts, and by rules/architecture.md it must not: the installer applies them
# deliberately, after a dump. So in development the drift has to be either applied or made loud,
# and this is the half that makes it loud.
#
# Usage:
#   maran drift            list every module that is behind; exit 1 if any is
#   maran drift --apply    apply the pending migrations of the modules that are behind
#
# THREE ANSWERS, NOT TWO, and the distinction this command exists to make honestly:
#
#   DRIFT VERDICT: OK            exit 0   a database was read and holds every migration on disk
#   DRIFT VERDICT: FAILED        exit 1   a database was read and is missing some, or the tree
#                                         offered no migration to compare at all
#   DRIFT VERDICT: DID NOT RUN   exit 2   no database was read — no docker, no container, or the
#                                         container would not answer. NOTHING was compared.
#
# WHICH OUTCOMES ARE FINDINGS AND WHICH ARE FACTS ABOUT THE MACHINE. This command asks an OPS
# question — "is this deployment behind the migrations on disk" — so on a workstation with no
# container up, "I could not check" is the ORDINARY answer, not an exception. It therefore must not
# be red: a gate whose everyday answer is red is a gate people learn to ignore, and an ignored gate
# reports nothing. `DID NOT RUN` (2) is exactly that third answer, and it is neither a pass nor a
# finding by construction. Facts about the machine, all of them refusals: docker absent, the
# container absent, the container not running, the database unreachable, and `--apply` unable to
# start the .NET SDK. Findings, both measured against a real history table: SCHEMA-BEHIND, and a
# repository in which no module offered a migration file (the vacuity guard below).
#
# Measured before that third answer existed, with `docker` off PATH: this command printed
# `DRIFT-UNKNOWN — the maran-postgres container is not running, so nothing was checked.` on
# **stderr** and returned **1**. A caller keeping stdout — how every CI step in this repository
# records a gate — was handed ZERO BYTES plus the status of a real schema drift. The sentence was
# also untrue about its own cause: the container was running; `docker` was what was missing.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"

# shellcheck disable=SC1091
. "$root/scripts/dev"

# For `suite_did_not_run` and `$SUITE_STATUS_DID_NOT_RUN`. This command parses no test log and
# counts no test, but it refuses for exactly the reasons `maran test`, `maran format` and
# `maran migrate check` refuse, and one refusal vocabulary across the harness is worth more to a
# reader — and to a CI step's grep — than a locally invented one. Sourced, never reimplemented.
# shellcheck disable=SC1091
. "$root/scripts/lib/suite.sh"

# drift_did_not_run: says which of the three answers this is, on STDOUT, and exits carrying it.
# Stdout deliberately: every refusal below used to print to stderr only, which is the zero-byte
# stdout log this file's header describes.
drift_did_not_run() {
  suite_did_not_run "DRIFT VERDICT" "$*"
  exit "$SUITE_STATUS_DID_NOT_RUN"
}

apply=0
[ "${1:-}" = "--apply" ] && apply=1

host="${Database__Host:-localhost}"
port="${Database__Port:-5432}"
database="${Database__Database:-maran_dev}"
username="${Database__Username:-maran_dev}"
password="${Database__Password:-maran_dev}"

# psql runs inside the database container, so the developer needs no client installed. The
# container name is the one docker-compose.dev.yml fixes.
psql_query() {
  docker exec -e PGPASSWORD="$password" maran-postgres \
    psql -qtAX -h 127.0.0.1 -p 5432 -U "$username" -d "$database" -c "$1" 2>&1
}

# THE MACHINE, before the database. Three separate facts, asked separately, because the single
# `docker inspect` that used to stand here answered all three with one sentence and got two of them
# wrong: with no `docker` on PATH it blamed a container that was running, and `docker inspect`
# succeeds on a container that EXISTS BUT IS STOPPED, so a stopped database fell through this guard
# and was reported as unreachable further down. A refusal that misnames its own cause sends the
# reader to the wrong place, which is the cheapest way to make a refusal worthless.
if ! command -v docker >/dev/null 2>&1; then
  echo "REFUSED: docker is not on PATH, and this command reaches the database THROUGH it" >&2
  echo "         (psql runs inside the maran-postgres container so no client is needed here)." >&2
  drift_did_not_run "docker is not on PATH, so no database could be asked anything."
fi

if ! docker inspect maran-postgres >/dev/null 2>&1; then
  echo "REFUSED: there is no container named maran-postgres on this machine." >&2
  echo "         Start the development database with: maran dev" >&2
  drift_did_not_run "the maran-postgres container does not exist here, so nothing was compared."
fi

container_state="$(docker inspect -f '{{.State.Status}}' maran-postgres 2>/dev/null || true)"
if [ "$container_state" != "running" ]; then
  echo "REFUSED: the maran-postgres container exists but its state is '$container_state'." >&2
  echo "         Start it with: maran dev" >&2
  drift_did_not_run "the maran-postgres container is not running, so nothing was compared."
fi

# A connection is proved before anything is concluded. Without this, an unreachable database
# returns an empty history for every schema and the command would report the whole repository as
# drifted — a confident answer produced by measuring nothing.
# `|| probe_status=$?`, and NOT a bare assignment. Measured on the tree this change was made
# against: `probe="$(psql_query 'select 1')"` alone is an assignment whose command substitution
# failed, so `set -e` ENDED THE SCRIPT ON THIS LINE and the guard immediately below it — the one
# written to catch exactly this — was DEAD CODE that had never executed. With a database name that
# does not exist the command printed **zero bytes on stdout and zero bytes on stderr** (psql's own
# message goes into `$probe`) and returned psql's 2. Nothing above a guard may be allowed to kill
# the script first (rules/testing.md).
probe_status=0
probe="$(psql_query 'select 1')" || probe_status=$?
if [ "$probe_status" -ne 0 ] || [ "$probe" != "1" ]; then
  echo "REFUSED: could not query $database on $host:$port as $username:" >&2
  printf '%s\n' "$probe" | head -3 | sed 's/^/    /' >&2
  drift_did_not_run "the database would not answer 'select 1' (its own words above), so no" \
    "migration history was read."
fi

# WHERE THE HISTORY LIVES, asked rather than assumed. Every module sets `HasDefaultSchema` for
# its own tables, so the obvious guess is one `__EFMigrationsHistory` per module schema — and the
# first version of this file made exactly that guess and reported all 23 migrations missing from a
# database that already held 8. EF Core leaves the history table in the connection's default
# schema unless a context says otherwise, so in this database all modules share ONE table in
# `public`. It is located here instead of being named, so a context that moves it is followed
# rather than silently reported as a database with no history at all.
# Same shape, same reason, and here the consequence of a silent failure is worse than a dead
# script: an empty answer to either of these queries is indistinguishable from "this database holds
# no migration at all", which this command would then report as every module BEHIND — a confident
# finding manufactured by a query that did not run. So each is asked for its status and a failure is
# a refusal, never an empty set.
history_status=0
history_raw="$(psql_query "select table_schema from information_schema.tables where table_name = '__EFMigrationsHistory'")" || history_status=$?
if [ "$history_status" -ne 0 ]; then
  echo "REFUSED: could not ask $database where its __EFMigrationsHistory table is:" >&2
  printf '%s\n' "$history_raw" | head -3 | sed 's/^/    /' >&2
  drift_did_not_run "the query that locates the migration history failed, and an empty answer" \
    "here would have been reported as every module being behind."
fi
history="$(printf '%s\n' "$history_raw" | sed '/^$/d')"
history_count="$(printf '%s\n' "$history" | sed '/^$/d' | wc -l)"

if [ "$history_count" -eq 0 ]; then
  applied=""
  echo "no __EFMigrationsHistory table in $database — treating every migration as missing"
else
  applied=""
  for schema in $history; do
    rows_status=0
    rows="$(psql_query "select \"MigrationId\" from \"$schema\".\"__EFMigrationsHistory\"")" || rows_status=$?
    if [ "$rows_status" -ne 0 ]; then
      echo "REFUSED: could not read \"$schema\".\"__EFMigrationsHistory\":" >&2
      printf '%s\n' "$rows" | head -3 | sed 's/^/    /' >&2
      drift_did_not_run "the history table in schema '$schema' could not be read, so the set of" \
        "applied migrations is incomplete and every comparison below it would be a guess."
    fi
    applied="$applied
$rows"
  done
fi
applied="$(printf '%s\n' "$applied" | sed '/^$/d' | sort -u)"

behind_modules=""
behind_count=0
checked=0

for migrations_dir in "$root"/backend/src/Maran.Modules/*/Persistence/Migrations; do
  [ -d "$migrations_dir" ] || continue
  module="$(basename "$(dirname "$(dirname "$migrations_dir")")")"

  # On disk: every migration file that is not a designer file or the snapshot. The migration id
  # is the file name without its extension, which is exactly what EF records.
  on_disk="$(find "$migrations_dir" -maxdepth 1 -name '*.cs' \
    -not -name '*.Designer.cs' -not -name '*ModelSnapshot.cs' -printf '%f\n' \
    | sed 's/\.cs$//' | sort)"
  [ -z "$on_disk" ] && continue
  checked=$((checked + 1))

  missing="$(comm -23 <(printf '%s\n' "$on_disk") <(printf '%s\n' "$applied"))"
  if [ -n "$missing" ]; then
    behind_count=$((behind_count + $(printf '%s\n' "$missing" | wc -l)))
    behind_modules="$behind_modules $module"
    printf '%-14s BEHIND — %s migration(s) this database has never had:\n' \
      "$module" "$(printf '%s\n' "$missing" | wc -l)"
    printf '%s\n' "$missing" | sed 's/^/    /'
  else
    printf '%-14s up to date (%s applied)\n' "$module" "$(printf '%s\n' "$on_disk" | wc -l)"
  fi
done

echo
# THE VACUITY GUARD, and it is on the axis that can go blind: this loop's entire input is a glob
# over `backend/src/Maran.Modules/*/Persistence/Migrations`. If a module layout moves, the glob
# matches nothing, every comparison is skipped and the two branches below would answer
# `SCHEMA-CURRENT — 0 module(s) compared` — a clean gate produced by looking at nothing at all.
# That is a FINDING about this tree and not a fact about the machine: the database answered, the
# repository is what had nothing to offer, and rules/testing.md is explicit that "no tests found"
# is a failure and never a pass. So it is FAILED (1), not DID NOT RUN (2), and it says which.
if [ "$checked" -eq 0 ]; then
  echo "DRIFT VERDICT: FAILED — no module offered a single migration file, so nothing was compared."
  echo "    The database answered; the TREE had nothing to check. Either"
  echo "    backend/src/Maran.Modules/*/Persistence/Migrations has moved and this command is now"
  echo "    blind, or this repository genuinely carries no migration. Both are defects here."
  exit 1
fi

if [ -z "$behind_modules" ]; then
  echo "DRIFT VERDICT: OK — SCHEMA-CURRENT: $checked module(s) compared against $database, 0"
  echo "    migrations missing."
  echo "    UNOBSERVED HERE: whether the MODEL matches those migration files (maran migrate check)"
  echo "    and whether EF itself would report anything pending (maran migrate status)."
  exit 0
fi

# A FINDING, and one about the DATABASE rather than about the branch — it was measured, a real
# history table was read, and `--apply` is the fix. Status 1, the harness's word for a run that
# produced a verdict with findings in it.
echo "DRIFT VERDICT: FAILED — SCHEMA-BEHIND: $behind_count migration(s) missing from $database"
echo "    in:$behind_modules"
if [ "$apply" -eq 0 ]; then
  echo "    apply them with: maran drift --apply"
  exit 1
fi

# `--apply` runs `maran migrate apply`, which needs the .NET SDK this command itself does not. That
# script refuses with its own `MIGRATIONS VERDICT: DID NOT RUN` and status 2, and under `set -e`
# that status would end this script here with somebody else's verdict line as its last word and no
# answer of its own. So the apply is a refusal too, named as this command's.
for module in $behind_modules; do
  echo
  echo "applying $module"
  if ! "$root/scripts/lib/migrations.sh" apply "$module"; then
    echo "REFUSED: applying $module did not succeed (its own output is above)." >&2
    drift_did_not_run "the pending migrations of $module could not be applied, so this database" \
      "is still behind and NOTHING here re-measured it."
  fi
done

echo
echo "re-checking after apply"
exec "$0"
