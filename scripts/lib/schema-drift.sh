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
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"

# shellcheck disable=SC1091
. "$root/scripts/dev"

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

if ! docker inspect maran-postgres >/dev/null 2>&1; then
  echo "DRIFT-UNKNOWN — the maran-postgres container is not running, so nothing was checked." >&2
  exit 1
fi

# A connection is proved before anything is concluded. Without this, an unreachable database
# returns an empty history for every schema and the command would report the whole repository as
# drifted — a confident answer produced by measuring nothing.
probe="$(psql_query 'select 1')"
if [ "$probe" != "1" ]; then
  echo "DRIFT-UNKNOWN — could not query $database on $host:$port as $username:" >&2
  printf '%s\n' "$probe" | head -3 | sed 's/^/    /' >&2
  exit 1
fi

# WHERE THE HISTORY LIVES, asked rather than assumed. Every module sets `HasDefaultSchema` for
# its own tables, so the obvious guess is one `__EFMigrationsHistory` per module schema — and the
# first version of this file made exactly that guess and reported all 23 migrations missing from a
# database that already held 8. EF Core leaves the history table in the connection's default
# schema unless a context says otherwise, so in this database all modules share ONE table in
# `public`. It is located here instead of being named, so a context that moves it is followed
# rather than silently reported as a database with no history at all.
history="$(psql_query "select table_schema from information_schema.tables where table_name = '__EFMigrationsHistory'" | sed '/^$/d')"
history_count="$(printf '%s\n' "$history" | sed '/^$/d' | wc -l)"

if [ "$history_count" -eq 0 ]; then
  applied=""
  echo "no __EFMigrationsHistory table in $database — treating every migration as missing"
else
  applied=""
  for schema in $history; do
    applied="$applied
$(psql_query "select \"MigrationId\" from \"$schema\".\"__EFMigrationsHistory\"")"
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
if [ "$checked" -eq 0 ]; then
  echo "DRIFT-UNKNOWN — no module with migrations was found; nothing was compared." >&2
  exit 1
fi

if [ -z "$behind_modules" ]; then
  echo "SCHEMA-CURRENT — $checked module(s) compared against $database, 0 migrations missing."
  echo "    UNOBSERVED HERE: whether the MODEL matches those migration files (maran migrate check)"
  echo "    and whether EF itself would report anything pending (maran migrate status)."
  exit 0
fi

echo "SCHEMA-BEHIND — $behind_count migration(s) missing from $database in:$behind_modules"
if [ "$apply" -eq 0 ]; then
  echo "    apply them with: maran drift --apply"
  exit 1
fi

for module in $behind_modules; do
  echo
  echo "applying $module"
  "$root/scripts/lib/migrations.sh" apply "$module"
done

echo
echo "re-checking after apply"
exec "$0"
