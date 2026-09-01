#!/usr/bin/env bash
# Enforces the structural rules no compiler or analyzer can express (rules/csharp.md,
# rules/architecture.md). Runs in CI as a merge gate and locally before review.
# Exit 0 = clean; any violation prints "path: reason" and exits 1.
set -uo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
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
#
#    `distro`'s per-family concern files (`debian_paths.rs`, `debian_packages.rs`,
#    `debian_services.rs` and their `rhel_*` counterparts) are the documented exception:
#    rules/rust.md's canonical layout names exactly these three files per family as the
#    home for every path/package/service fact of their concern, so each answers several
#    related platform-fact functions rather than growing a file per function. Listed here
#    rather than inferred, same as the subject-named single-item exceptions below.
concern_files='debian_paths|debian_packages|debian_services|rhel_paths|rhel_packages|rhel_services'
while IFS= read -r file; do
  units=$(grep -cE '^pub (struct|enum|trait|fn|async fn) ' "$file")
  base="$(basename "$file" .rs)"
  if printf '%s' "$base" | grep -qE "^($concern_files)$"; then
    continue
  fi
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

# 9b. The forked child leaves through `_exit` and never through `exit`. `exit` runs atexit
#     handlers and flushes stdio the child shares with its parent, so the parent's buffered
#     bytes are written a second time by a process it does not know about — and any handler
#     the runtime registered runs in a process that holds none of the state it expects. The
#     invariant is argued at length in three comments in fork_as_account.rs and was, until
#     this check, enforced by none of them: swapping `_exit` for `exit` left every test in
#     the workspace green. It is mechanical, so it is a gate (rules/README.md "Mechanical
#     enforcement").
#     Matched as a token rather than as a substring, and on code rather than on prose, because
#     the first version of this check was three edits away from useless: `libc::exit (status)`
#     with one space and `use libc::exit as leave;` both passed it while compiling, and a doc
#     comment that merely CONTAINED the text failed the build. So comments are stripped first,
#     the call is matched as an `exit(` not preceded by `_` or an identifier character (which
#     keeps `libc::_exit(` legal and catches the bare, spaced and qualified forms alike), and
#     any `use` that imports `exit` under any name is rejected on its own — an alias is the one
#     evasion a call-site pattern cannot see.
while IFS= read -r file; do
  code="$(sed -e 's://.*$::' "$file")"
  if printf '%s\n' "$code" | grep -qE '(^|[^_[:alnum:]])exit[[:space:]]*\('; then
    report "$file: a forked child leaves through libc::_exit, never exit — exit flushes the parent's stdio and runs its atexit handlers (rules/rust.md \"Privileges\")"
  fi
  if printf '%s\n' "$code" | grep -qE '^[[:space:]]*(pub[[:space:]]+)?use[[:space:]].*(^|[^_[:alnum:]])exit([^_[:alnum:]]|$)'; then
    report "$file: importing exit — under any alias — is the same violation as calling it; the child leaves through libc::_exit (rules/rust.md \"Privileges\")"
  fi
done < <(find agent/crates/agent-core/src/privs -name '*.rs' 2>/dev/null | sort)

# 10. Junk-drawer names are no more acceptable in Rust than in C#.
while IFS= read -r file; do
  report "$file: junk-drawer name — every file states its single purpose (rules/rust.md)"
done < <(find agent/crates -name '*.rs' -not -path '*/target/*' 2>/dev/null | grep -iE '/(utils|util|helpers|misc|common|shared)\.rs$')

# 11. Member order: a type's shape comes before its behaviour, so no property may follow a method
#     (rules/csharp.md "Member order — methods come last"). Checked rather than trusted, because
#     the mistake is invisible in a diff: the new member simply lands wherever the cursor was.
while IFS= read -r file; do
  python3 - "$file" <<'PYCHECK' || report "$file: a property is declared after a method or constructor — shape first, behaviour last (rules/csharp.md)"
import re
import sys

path = sys.argv[1]
type_name = path.rsplit("/", 1)[-1][:-3]
declaration = re.compile(r"^    (public|internal|protected|private)\b")
prop = re.compile(r"^    (public|internal|protected|private)\b.*\{\s*get;")
method = re.compile(r"^    (public|internal|protected|private)\b.*\w+\(.*\)\s*$")
# A signature broken across lines ends at the open paren; without this the longest
# constructors — exactly the members this rule is about — would slip past unseen.
wrapped = re.compile(r"^    (public|internal|protected|private)\b.*\w+\($")

first_method = None
for number, line in enumerate(open(path, encoding="utf-8"), start=1):
    if not declaration.match(line):
        continue
    # A constructor counts: it belongs below the properties too (rules/csharp.md), because it is
    # the longest member of a typical entity and burying the field list under it helps nobody.
    if (method.match(line) or wrapped.match(line)) and " get;" not in line:
        first_method = first_method or number
    elif prop.match(line) and first_method is not None:
        sys.exit(1)
PYCHECK
done < <(find backend/src -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -not -path '*/Migrations/*' -not -path '*/Generated/*' 2>/dev/null | sort)

# 12. The same order in the SPA: state before the functions that change it
#     (rules/vue.md "Member order — functions come last"). `const` is not hoisted, so a function
#     above the state it closes over also reads as if the order were free when it is not.
while IFS= read -r file; do
  python3 - "$file" <<'PYCHECK' || report "$file: a ref/computed is declared after a function — state first, functions last (rules/vue.md)"
import re
import sys

arrow = re.compile(r"^const \w+ = (async )?\(.*\)(: [^=]+)? =>")
state = re.compile(r"^const \w+.*= (ref|computed|reactive)\(")

first_function = None
for line in open(sys.argv[1], encoding="utf-8"):
    if first_function is None and arrow.match(line):
        first_function = True
    elif first_function and state.match(line):
        sys.exit(1)
PYCHECK
done < <(find frontend/src \( -name '*.vue' -o -name '*.ts' \) 2>/dev/null | sort)

# 13. A domain enum belongs in Domain/Enums/, never loose beside the entities
#     (rules/csharp.md "Domain enums live in Domain/Enums/").
while IFS= read -r file; do
  if grep -qE '^public enum ' "$file"; then
    report "$file: a domain enum lives in Domain/Enums/ (rules/csharp.md)"
  fi
done < <(find backend/src/Maran.Modules/*/Domain -maxdepth 1 -name '*.cs' 2>/dev/null | sort)

# 14. A domain entity exposes no public setter: every change of state goes through a named method
#     (rules/csharp.md "Domain models are rich"). An options class binding configuration is the one
#     legitimate bag of setters, and it never lives under Domain/.
while IFS= read -r file; do
  if grep -qE '\{ get; set; \}' "$file"; then
    report "$file: a domain property has a public setter — state changes through a named method (rules/csharp.md)"
  fi
done < <(find backend/src/Maran.Modules/*/Domain -name '*.cs' 2>/dev/null | sort)

# 15. Font sizes come only from the shared scale (rules/vue.md "One type scale"): neither a
#     literal in scoped CSS nor Tailwind's arbitrary-value escape hatch, both of which stay behind
#     when the scale is retuned.
while IFS= read -r file; do
  if grep -qE 'font-size:[[:space:]]*[0-9]' "$file"; then
    report "$file: a font size is written directly — use the --text-* scale (rules/vue.md)"
  fi
  if grep -qE 'text-\[[0-9]' "$file"; then
    report "$file: an arbitrary text size — use a named step of the scale (rules/vue.md)"
  fi
done < <(find frontend/src \( -name '*.vue' -o -name '*.ts' \) 2>/dev/null | sort)

# 16. A Rust file is named after its single public item, in snake_case
#     (rules/rust.md "One unit per file"). The rules have promised this check since they were
#     written; until now they only promised it, which is how `adapter_selector.rs` came to hold
#     `adapter_for` and nobody noticed for a session.
#
#     Subject-named files are the documented exception (rules/rust.md names them explicitly in
#     the canonical layout), so they are listed here rather than inferred — an inferred
#     exception is a rule that quietly stops applying.
subject_named='adapter|adapter_for|detect|distro_info|os_release|family|path|name|domain|port|ip_address'
subject_named="$subject_named|cron_expression|directory|current_uid|unit|pool|user_config|error|server"
subject_named="$subject_named|agent_options|options_error|peer_policy|peer_guard|render_validate_swap|rollback_guard"
subject_named="$subject_named|debian_paths|debian_packages|debian_services|rhel_paths|rhel_packages|rhel_services"
while IFS= read -r file; do
  base="$(basename "$file" .rs)"
  case "$base" in
    mod|lib|main|build) continue ;;
  esac
  if printf '%s' "$base" | grep -qE "^($subject_named)$"; then
    continue
  fi

  # `<service>_service.rs` and `<area>_status.rs` are the shapes the service anatomy
  # mandates by name, so they are exempt as a family rather than one by one.
  case "$base" in
    *_service|*_status) continue ;;
  esac

  # The single public item, if there is one. `sed` turns the declaration into the item name;
  # a type becomes snake_case, a function is already in it.
  item="$(grep -m1 -E '^pub (struct|enum|trait|fn|async fn) ' "$file" |
          sed -E 's/^pub (async )?(struct|enum|trait|fn) ([A-Za-z0-9_]+).*/\3/')"
  [ -z "$item" ] && continue

  expected="$(printf '%s' "$item" | sed -E 's/([a-z0-9])([A-Z])/\1_\2/g' | tr '[:upper:]' '[:lower:]')"
  if [ "$base" != "$expected" ]; then
    report "$file: holds \`$item\`, so the file is $expected.rs (rules/rust.md \"One unit per file\")"
  fi
done < <(find agent/crates -name '*.rs' -not -path '*/target/*' -not -path '*/tests/*' 2>/dev/null | sort)

# 17. No platform literal in ops: paths, package managers and service tools differ between the
#     supported families, and a literal is a guess that `useradd` will not check
#     (rules/architecture.md "Supported systems", rules/rust.md "Distro adapter"). The facts come
#     from the adapter, which is the one place a family is branched on.
while IFS= read -r file; do
  offenders="$(grep -nE '"(/usr/s?bin|/s?bin|/etc)/[a-z]|"(apt|apt-get|dnf|yum|zypper)"' "$file" |
               grep -v '^\s*//' || true)"
  if [ -n "$offenders" ]; then
    line="$(printf '%s' "$offenders" | head -1 | cut -d: -f1)"
    report "$file:$line: platform literal in ops — ask the DistroAdapter (rules/architecture.md)"
  fi
done < <(find agent/crates/ops/src -name '*.rs' -not -path '*/tests/*' 2>/dev/null | sort)

# 18. Every locale carries the same keys. The backend has this check for its .resx files
#     (ResourceKeyParityTests); the SPA had none, and its locale files are edited by hand three at
#     a time. A key added to en/ and forgotten in hy/ is not an error anywhere — vue-i18n renders
#     the key itself, so the Armenian user sees `app.audit.heading` where a heading should be.
locale_report="$(python3 - "$root" <<'PYEOF'
import json, pathlib, sys

def flatten(value, prefix=''):
    keys = set()
    for key, item in value.items():
        path = f'{prefix}.{key}' if prefix else key
        keys |= flatten(item, path) if isinstance(item, dict) else {path}
    return keys

root = pathlib.Path(sys.argv[1]) / 'frontend' / 'src' / 'locales'
if not root.is_dir():
    sys.exit(0)

locales = {}
for directory in sorted(p for p in root.iterdir() if p.is_dir()):
    keys = set()
    for path in sorted(directory.glob('*.json')):
        try:
            keys |= flatten(json.loads(path.read_text()))
        except json.JSONDecodeError as error:
            print(f'{path}: not valid JSON ({error})')
            sys.exit(0)
    locales[directory.name] = keys

# English is the reference because it is the language the keys are written in.
reference = locales.get('en', set())
for name, keys in sorted(locales.items()):
    if name == 'en':
        continue
    for key in sorted(reference - keys):
        print(f'frontend/src/locales/{name}: missing key `{key}` (rules/vue.md "the backend owns text; the SPA owns its own keys in every locale")')
    for key in sorted(keys - reference):
        print(f'frontend/src/locales/{name}: key `{key}` exists in no other locale')
PYEOF
)"
while IFS= read -r line; do
  [ -n "$line" ] && report "$line"
done <<< "$locale_report"

if [ "$violations" -gt 0 ]; then
  echo
  echo "$violations structural violation(s). See rules/ for the rule each one cites."
  exit 1
fi

echo "STRUCTURE-OK"
