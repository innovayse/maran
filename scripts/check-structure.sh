#!/usr/bin/env bash
# Enforces the structural rules no compiler or analyzer can express (rules/csharp.md,
# rules/architecture.md). Runs in CI as a merge gate and locally before review.
# Exit 0 = clean; any violation prints "path: reason" and exits 1.
set -uo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
violations=0

report() {
  echo "VIOLATION  $1"
  violations=$((violations + 1))
}

sources() {
  find backend/src backend/tests -name '*.cs' \
    -not -path '*/obj/*' -not -path '*/bin/*' -not -name 'GlobalUsings.cs'
}

# 1. One type per file. A file declares exactly one top-level type, and its name matches.
while IFS= read -r file; do
  count=$(grep -cE '^(public|internal)( sealed| static| abstract| partial)* (class|record|interface|enum|struct) ' "$file")
  if [ "$count" -gt 1 ]; then
    report "$file: declares $count top-level types — one type per file (rules/csharp.md)"
  fi
  if [ "$count" -eq 1 ]; then
    declared=$(grep -oE '^(public|internal)( sealed| static| abstract| partial)* (class|record|interface|enum|struct) [A-Za-z0-9_]+' "$file" \
      | grep -oE '[A-Za-z0-9_]+$')
    expected=$(basename "$file" .cs)
    # `<Name>OfT.cs` is the sanctioned name for the generic half of a generic/non-generic
    # pair (rules/csharp.md), so `ResultOfT.cs` legitimately declares `Result<T>`.
    expected="${expected%OfT}"
    if [ "$declared" != "$expected" ]; then
      report "$file: declares '$declared' — the file name must equal the type name (rules/csharp.md)"
    fi
  fi
done < <(sources)

# 2. Every *Extensions type lives in an Extensions/ folder.
while IFS= read -r file; do
  case "$file" in
    */Extensions/*) ;;
    *) report "$file: an *Extensions type belongs in an Extensions/ folder (rules/csharp.md)" ;;
  esac
done < <(sources | grep 'Extensions\.cs$')

# 3. Interfaces live in an Interfaces/ folder — except module-internal Common/ and Domain/ ones,
#    which the module layout places deliberately.
while IFS= read -r file; do
  case "$file" in
    */Interfaces/*) ;;
    */Common/Interfaces/*|*/Domain/Interfaces/*) ;;
    *) report "$file: an interface belongs in an Interfaces/ folder (rules/csharp.md)" ;;
  esac
done < <(sources | grep -E '/I[A-Z][A-Za-z0-9_]*\.cs$')

# 4. Namespace must mirror the folder path.
while IFS= read -r file; do
  ns=$(grep -oE '^namespace [A-Za-z0-9_.]+' "$file" | head -1 | cut -d' ' -f2)
  [ -z "$ns" ] && continue
  expected_dir=$(dirname "$file" | sed -E 's|^backend/(src\|tests)/||')
  ns_path=$(echo "$ns" | tr '.' '/')
  case "$ns_path" in
    *"$(echo "$expected_dir" | sed -E 's|^Maran[A-Za-z.]*/||')") ;;
    *)
      if [ "$(basename "$ns_path")" != "$(basename "$expected_dir")" ] && [ "$expected_dir" != "$(basename "$expected_dir")" ]; then
        report "$file: namespace '$ns' does not mirror its folder (rules/csharp.md)"
      fi
      ;;
  esac
done < <(sources)

# 5. Modules never reference each other (the architecture tests cover assemblies; this catches
#    the source-level import before it ever compiles).
while IFS= read -r file; do
  owner=$(echo "$file" | sed -E 's|backend/src/Maran.Modules/([^/]+)/.*|\1|')
  while IFS= read -r used; do
    [ "$used" = "$owner" ] && continue
    report "$file: imports module '$used' — modules never reference each other (rules/architecture.md)"
  done < <(grep -oE 'using Maran\.Modules\.[A-Za-z0-9_]+' "$file" | sed -E 's|using Maran\.Modules\.||' | sort -u)
done < <(find backend/src/Maran.Modules -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' 2>/dev/null)

# 6. Junk-drawer names are never valid file names.
while IFS= read -r file; do
  report "$file: junk-drawer name — every file states its single purpose (rules/architecture.md)"
done < <(sources | grep -iE '/(utils|helpers|misc|common|shared|manager|service)\.cs$')

# 7. Rust obeys the same law as C#: exactly one public unit per file, and a crate root or
#    mod.rs declares modules rather than defining anything (rules/rust.md). Until this check
#    existed the Rust side of the rule rested on review alone, which is how a type and its
#    error ended up sharing a file twice.
while IFS= read -r file; do
  units=$(grep -cE '^pub (struct|enum|trait|fn|async fn) ' "$file")
  if [ "$units" -gt 1 ]; then
    report "$file: $units public units — one per file, errors in their own *_error.rs (rules/rust.md)"
  fi
  case "$file" in
    */mod.rs|*/lib.rs|*/main.rs)
      if [ "$units" -gt 0 ]; then
        report "$file: a crate root or mod.rs declares modules and re-exports, never defines (rules/rust.md)"
      fi
      ;;
  esac
done < <(find agent/crates -name '*.rs' -not -path '*/target/*' 2>/dev/null | sort)

# 8. Tests live in their own file, never inline in the unit they test (rules/rust.md).
#    `#[cfg(test)] #[path = "<unit>_tests.rs"] mod tests;` keeps the one-unit-per-file rule
#    while still reaching private items, which a tests/ integration test cannot see.
while IFS= read -r file; do
  if grep -qE '^\s*mod tests \{' "$file"; then
    report "$file: inline test module — move it to $(basename "${file%.rs}")_tests.rs (rules/rust.md)"
  fi
done < <(find agent/crates -name '*.rs' -not -path '*/target/*' 2>/dev/null | sort)

# 9. Unit tests mirror the source tree under src/tests/, never beside the unit itself.
while IFS= read -r file; do
  case "$file" in
    */src/tests/*) ;;
    *) report "$file: tests live under the crate's src/tests/ mirror (rules/testing.md)" ;;
  esac
done < <(find agent/crates -name '*_tests.rs' -not -path '*/target/*' 2>/dev/null | sort)

# 10. Junk-drawer names are no more acceptable in Rust than in C#.
while IFS= read -r file; do
  report "$file: junk-drawer name — every file states its single purpose (rules/rust.md)"
done < <(find agent/crates -name '*.rs' -not -path '*/target/*' 2>/dev/null | grep -iE '/(utils|util|helpers|misc|common|shared)\.rs$')

if [ "$violations" -gt 0 ]; then
  echo
  echo "$violations structural violation(s). See rules/ for the rule each one cites."
  exit 1
fi

echo "STRUCTURE-OK"
