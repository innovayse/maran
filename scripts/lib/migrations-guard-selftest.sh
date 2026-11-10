#!/usr/bin/env bash
# `maran migrate selftest` — proves `maran migrate guard` still refuses what the expand-then-contract
# law forbids AND still accepts what it allows.
#
# WHY THIS FILE EXISTS
#
# The guard is a REFUSING GATE, and rules/testing.md is explicit about what those owe: a gate mutated
# to refuse everything passes every test that only ever hands it broken input. So every planted break
# below is paired with an INVERSE CONTROL — the same shape of migration, written the allowed way —
# and the assertion on the control is that the guard's own refusal is ABSENT and its additive verdict
# is present. Assertions read the printed verdict line and the sentence that names the cause, never
# the exit status alone.
#
# The guard under test is not the working tree's: each case builds a throwaway git repository, copies
# scripts/lib/migrations.sh into it (the guard derives its root from its own location), commits a
# base, tags it where the case needs a release, and writes the migration under test as an untracked
# file — the state `dotnet ef migrations add` leaves behind, which the guard exists to read.
#
# WHAT THIS DOES NOT OBSERVE, stated rather than implied:
#   UNOBSERVED HERE: the real history. The cases run against a fixture repository, so they prove the
#   guard's logic, not that this repository's tags are present in a CI checkout (backend.yml checks
#   out the full history for that).
#   UNOBSERVED HERE: an UPDATE ... SET that rewrites rows in place, which the guard itself names as
#   its known blind spot.
#
# Usage:  maran migrate selftest
# Exit:   0 only when every case passed. The OUTPUT is the verdict; the exit status exists so a CI
#         job can go red.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

passed=0
failed=0

migrations_dir="backend/src/Maran.Modules/Widgets/Persistence/Migrations"
expand_id="20260101000000_AddWidgetName"

# fixture_repo: a fresh repository whose `main` holds the expand migration, with the guard copied in.
fixture_repo() {
  local repo="$work/repo-$1"
  mkdir -p "$repo/scripts/lib" "$repo/$migrations_dir"
  cp "$root/scripts/lib/migrations.sh" "$repo/scripts/lib/migrations.sh"
  write_migration "$repo" "$expand_id" '        migrationBuilder.AddColumn<string>(
            name: "Name",
            table: "Widgets",
            type: "text",
            nullable: true);'
  git -C "$repo" init -q -b main
  git -C "$repo" config user.email selftest@example.invalid
  git -C "$repo" config user.name selftest
  git -C "$repo" add -A
  git -C "$repo" commit -q -m "expand phase"
  printf '%s' "$repo"
}

# write_migration <repo> <id> <up-body> [header-comment]: writes <id>.cs with the given Up body.
write_migration() {
  local repo="$1" id="$2" body="$3" header="${4:-}"
  mkdir -p "$repo/$migrations_dir"
  {
    echo "namespace Maran.Modules.Widgets.Persistence.Migrations"
    echo "{"
    echo "    public partial class ${id#*_} : Migration"
    echo "    {"
    echo "        protected override void Up(MigrationBuilder migrationBuilder)"
    echo "        {"
    [ -n "$header" ] && printf '%s\n' "$header"
    printf '%s\n' "$body"
    echo "        }"
    echo ""
    echo "        protected override void Down(MigrationBuilder migrationBuilder)"
    echo "        {"
    echo "        }"
    echo "    }"
    echo "}"
  } > "$repo/$migrations_dir/$id.cs"
}

# run_guard <repo>: runs the guard in the fixture, leaving its output in $output and status in $status.
run_guard() {
  status=0
  output="$(bash "$1/scripts/lib/migrations.sh" guard main 2>&1)" || status=$?
}

# expect <name> <verdict-prefix> <must-contain-or-empty>: asserts the printed verdict line, and when
# given, a sentence that names the cause. A refusal is also asserted to be status 1, an acceptance 0.
expect() {
  local name="$1" verdict="$2" sentence="$3" want_status=0
  [ "$verdict" = "MIGRATIONS-DESTRUCTIVE" ] && want_status=1
  if printf '%s\n' "$output" | grep -q "^$verdict" \
    && [ "$status" -eq "$want_status" ] \
    && { [ -z "$sentence" ] || printf '%s\n' "$output" | grep -qF -- "$sentence"; }; then
    echo "PASS  $name"
    passed=$((passed + 1))
  else
    echo "FAIL  $name — wanted $verdict (status $want_status)${sentence:+ with: $sentence}; got status $status:"
    printf '%s\n' "$output" | sed 's/^/        /'
    failed=$((failed + 1))
  fi
}

new_id="20260201000000_RemoveWidgetName"

# --- Destructive operations: a planted break each, and the accepting control beside it. ---------------

repo="$(fixture_repo drop-index)"
write_migration "$repo" "$new_id" '            migrationBuilder.DropIndex(
                name: "IX_Widgets_Name",
                table: "Widgets");'
run_guard "$repo"
expect "DropIndex without a marker is refused" MIGRATIONS-DESTRUCTIVE "DropIndex"

repo="$(fixture_repo drop-column)"
write_migration "$repo" "$new_id" '            migrationBuilder.DropColumn(
                name: "Name",
                table: "Widgets");'
run_guard "$repo"
expect "DropColumn without a marker is refused" MIGRATIONS-DESTRUCTIVE "DropColumn"

repo="$(fixture_repo drop-table)"
write_migration "$repo" "$new_id" '            migrationBuilder.DropTable(
                name: "Widgets");'
run_guard "$repo"
expect "DropTable without a marker is refused" MIGRATIONS-DESTRUCTIVE "DropTable"

repo="$(fixture_repo rename-column)"
write_migration "$repo" "$new_id" '            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Widgets",
                newName: "Title");'
run_guard "$repo"
expect "RenameColumn without a marker is refused" MIGRATIONS-DESTRUCTIVE "RenameColumn"

repo="$(fixture_repo drop-foreign-key)"
write_migration "$repo" "$new_id" '            migrationBuilder.DropForeignKey(
                name: "FK_Widgets_Owners_OwnerId",
                table: "Widgets");'
run_guard "$repo"
expect "DropForeignKey without a marker is refused" MIGRATIONS-DESTRUCTIVE "DropForeignKey"

repo="$(fixture_repo narrowing-length)"
write_migration "$repo" "$new_id" '            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Widgets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);'
run_guard "$repo"
expect "AlterColumn that shortens a column is refused" MIGRATIONS-DESTRUCTIVE "not a provable widening"

repo="$(fixture_repo narrowing-nullability)"
write_migration "$repo" "$new_id" '            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Widgets",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);'
run_guard "$repo"
expect "AlterColumn that forbids null is refused" MIGRATIONS-DESTRUCTIVE "not a provable widening"

repo="$(fixture_repo narrowing-type)"
write_migration "$repo" "$new_id" '            migrationBuilder.AlterColumn<int>(
                name: "Name",
                table: "Widgets",
                type: "integer",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);'
run_guard "$repo"
expect "AlterColumn that changes the type is refused" MIGRATIONS-DESTRUCTIVE "not a provable widening"

repo="$(fixture_repo widening-length)"
write_migration "$repo" "$new_id" '            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Widgets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);'
run_guard "$repo"
expect "AlterColumn that lengthens a column is accepted" MIGRATIONS-ADDITIVE ""

repo="$(fixture_repo widening-nullability)"
write_migration "$repo" "$new_id" '            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Widgets",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");'
run_guard "$repo"
expect "AlterColumn that allows null is accepted" MIGRATIONS-ADDITIVE ""

repo="$(fixture_repo additive)"
write_migration "$repo" "$new_id" '            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Widgets",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Widgets_Title",
                table: "Widgets",
                column: "Title");'
run_guard "$repo"
expect "AddColumn and CreateIndex are accepted" MIGRATIONS-ADDITIVE ""

# --- The marker: it must name an expand phase, and the tags must agree with it. -----------------------

drop_column='            migrationBuilder.DropColumn(
                name: "Name",
                table: "Widgets");'

repo="$(fixture_repo marker-bare)"
write_migration "$repo" "$new_id" "$drop_column" '            // contract-phase: Widgets.Name, trust me'
run_guard "$repo"
expect "a marker naming no expand phase is refused" MIGRATIONS-DESTRUCTIVE "names no expand phase"

repo="$(fixture_repo marker-untagged)"
write_migration "$repo" "$new_id" "$drop_column" "            // contract-phase: Widgets.Name; expand: $expand_id"
run_guard "$repo"
expect "an expand phase in no release tag is refused" MIGRATIONS-DESTRUCTIVE "is in no published release tag"

repo="$(fixture_repo marker-tagged)"
git -C "$repo" tag v1.0.0
write_migration "$repo" "$new_id" "$drop_column" "            // contract-phase: Widgets.Name; expand: $expand_id"
run_guard "$repo"
expect "an expand phase inside a release tag is accepted" MIGRATIONS-ADDITIVE ""

repo="$(fixture_repo marker-tagged-by-commit)"
git -C "$repo" tag v1.0.0
commit="$(git -C "$repo" rev-parse --short=10 HEAD)"
write_migration "$repo" "$new_id" "$drop_column" "            // contract-phase: Widgets.Name; expand: $commit"
run_guard "$repo"
expect "an expand phase named by commit hash inside a release tag is accepted" MIGRATIONS-ADDITIVE ""

repo="$(fixture_repo marker-non-release-tag)"
git -C "$repo" tag backup-before-rewrite
write_migration "$repo" "$new_id" "$drop_column" "            // contract-phase: Widgets.Name; expand: $expand_id"
run_guard "$repo"
expect "a tag that is not a release tag does not count" MIGRATIONS-DESTRUCTIVE "is in no published release tag"

repo="$(fixture_repo marker-unresolved)"
git -C "$repo" tag v1.0.0
write_migration "$repo" "$new_id" "$drop_column" "            // contract-phase: Widgets.Name; expand: 20250101000000_NoSuchMigration"
run_guard "$repo"
expect "an expand reference that resolves to nothing is refused" MIGRATIONS-DESTRUCTIVE "resolves to no migration file"

repo="$(fixture_repo marker-uncommitted-expand)"
git -C "$repo" tag v1.0.0
write_migration "$repo" "20260115000000_AddWidgetTitle" '        migrationBuilder.AddColumn<string>(name: "Title", table: "Widgets", type: "text", nullable: true);'
write_migration "$repo" "$new_id" "$drop_column" "            // contract-phase: Widgets.Title; expand: 20260115000000_AddWidgetTitle"
run_guard "$repo"
expect "an expand phase that is not committed yet is refused" MIGRATIONS-DESTRUCTIVE "is in no published release tag"

repo="$(fixture_repo never-shipped-true)"
write_migration "$repo" "$new_id" "$drop_column" "            // contract-phase: Widgets.Name; never-shipped: $expand_id"
run_guard "$repo"
expect "never-shipped over a schema no release carried is accepted" MIGRATIONS-ADDITIVE ""

repo="$(fixture_repo never-shipped-false)"
git -C "$repo" tag v1.0.0
write_migration "$repo" "$new_id" "$drop_column" "            // contract-phase: Widgets.Name; never-shipped: $expand_id"
run_guard "$repo"
expect "never-shipped over a schema a release carried is refused" MIGRATIONS-DESTRUCTIVE "a release has shipped it"

repo="$(fixture_repo doc-mention)"
git -C "$repo" tag v1.0.0
write_migration "$repo" "$new_id" "$drop_column" "            /// <c>// contract-phase: Widgets.Name; expand: $expand_id</c>"
run_guard "$repo"
expect "a doc comment that mentions the marker is not a marker" MIGRATIONS-DESTRUCTIVE "DropColumn"

# --- Scope: a migration already inside a release shipped, and is not re-read. -------------------------

repo="$(fixture_repo released)"
write_migration "$repo" "$new_id" "$drop_column"
git -C "$repo" add -A
git -C "$repo" commit -q -m "contract phase"
git -C "$repo" tag v1.1.0
# The branch point is the first commit, so the contract migration is in the diff against it.
status=0
output="$(bash "$repo/scripts/lib/migrations.sh" guard "$(git -C "$repo" rev-list --max-parents=0 HEAD)" 2>&1)" || status=$?
expect "a migration inside a release tag is skipped, not judged" MIGRATIONS-NONE-TO-CHECK "1 already inside a release tag"

repo="$(fixture_repo unreleased-committed)"
write_migration "$repo" "$new_id" "$drop_column"
git -C "$repo" add -A
git -C "$repo" commit -q -m "contract phase"
status=0
output="$(bash "$repo/scripts/lib/migrations.sh" guard "$(git -C "$repo" rev-list --max-parents=0 HEAD)" 2>&1)" || status=$?
expect "the same committed migration with no release tag is refused" MIGRATIONS-DESTRUCTIVE "DropColumn"

echo
if [ "$failed" -gt 0 ]; then
  echo "MIGRATE-GUARD SELFTEST: FAILED — $failed case(s) failed, $passed passed."
  exit 1
fi
echo "MIGRATE-GUARD SELFTEST: OK — $passed case(s) passed."
echo "    Every planted break was refused with its cause named, and every accepting control passed."
echo "    UNOBSERVED HERE: this repository's real tags and history, and an UPDATE ... SET rewriting rows."
