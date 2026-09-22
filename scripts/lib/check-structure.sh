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
#
#    The modifier list is the whole check: a declaration whose modifiers it does not know is
#    not "allowed", it is INVISIBLE, and an invisible declaration means the file's second type
#    is never counted. The list used to be `sealed static abstract partial` only, so
#    `public readonly record struct` — the shape Maran.Host/Security uses twice — was seen by
#    neither half of this check. Measured: appending a second `public readonly record struct`
#    to PanelPeerPolicy.cs left the whole gate at STRUCTURE-OK. `readonly`, `unsafe`, `ref`
#    and the `file` accessibility are now known, and `record class`/`record struct` are read as
#    the two-word keywords they are (rules/testing.md "a check must be able to observe what it
#    reports on").
type_declaration='^(public|internal|file)([[:space:]]+(sealed|static|abstract|partial|readonly|unsafe|ref))*[[:space:]]+(record[[:space:]]+)?(class|record|interface|enum|struct)[[:space:]]+[A-Za-z0-9_]+'
while IFS= read -r file; do
  count=$(grep -cE "$type_declaration" "$file")
  if [ "$count" -gt 1 ]; then
    report "$file: declares $count top-level types — one type per file (rules/csharp.md)"
  fi
  if [ "$count" -eq 1 ]; then
    declared=$(grep -oE "$type_declaration" "$file" \
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

# 3. Interfaces live in an Interfaces/ folder. A module's own Interfaces/ sits at the module root,
#    beside Maran.Sdk/Interfaces/ and Maran.SharedKernel/Interfaces/; Domain/Interfaces/ is the one
#    deliberate second home, for repository contracts that belong beside the entities they load.
while IFS= read -r file; do
  case "$file" in
    */Interfaces/*) ;;
    */Domain/Interfaces/*) ;;
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
#
#    Matched anywhere in the file, not only after `using`. A `using` is the polite way to reach
#    another module and it was the only way this check could see: `nameof(Maran.Modules.Sites.
#    SitesModule)` written inline compiles, needs no directive, and passed the whole gate —
#    measured. The file's OWN namespace names its owner and is skipped by the same comparison
#    that skips a self-import, so the widening costs no false positive.
while IFS= read -r file; do
  owner=$(echo "$file" | sed -E 's|backend/src/Maran.Modules/([^/]+)/.*|\1|')
  while IFS= read -r used; do
    [ "$used" = "$owner" ] && continue
    report "$file: names module '$used' — modules never reference each other (rules/architecture.md)"
  # Comments stripped first, and that is the inverse control this widening owed: on real code
  # the widened pattern immediately reported Tasks/Jobs/TaskRetentionRequested.cs, whose only
  # mention of another module is a doc comment naming Ssl's message as the parallel case. A
  # prose reference is not a reference; only code is.
  done < <(sed -E 's://.*$::' "$file" | grep -oE 'Maran\.Modules\.[A-Za-z0-9_]+' | sed -E 's|Maran\.Modules\.||' | sort -u)
done < <(find backend/src/Maran.Modules -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' 2>/dev/null)

# 6. Junk-drawer names are never valid file names.
#    The singular forms are on the list beside the plural ones, and `util`/`utilities` beside
#    `utils`: the word list IS the check, so a word missing from it is a name the gate cannot
#    see rather than a name it permits. `Util.cs` holding `public static class Util` satisfied
#    check 1 (its name matches its type) and check 6 (the word was absent) at the same time —
#    measured at STRUCTURE-OK. Kept in step with the Rust list in check 10, which had `util`
#    when this one did not.
junk_drawer_words='utils|util|utilities|helpers|helper|misc|common|shared|manager|managers|service|services|constants|stuff'
while IFS= read -r file; do
  report "$file: junk-drawer name — every file states its single purpose (rules/architecture.md)"
done < <(sources | grep -iE "/($junk_drawer_words)\.cs\$")

# 6b. The caller's address is spelled in exactly ONE place. SharedKernel/Utilities/Network/
#     ClientAddress.cs owns the rendering; production code asks it. The duplicate this catches is
#     invisible in review — every copy of `RemoteIpAddress?.ToString()` looks correct, and each one
#     silently drops the IPv4-mapped normalisation, which splits a brute-force counter in half and
#     produces bans the agent refuses. Eleven controllers had written it out before this check
#     existed (rules/csharp.md "One spelling of the caller's address, mechanically").
#
#     backend/src only: a test fixture that deliberately echoes the raw connection value is
#     asserting on middleware behaviour and must stay raw.
while IFS= read -r file; do
  report "$file: spells the caller's address itself — call ClientAddress.Of (rules/csharp.md)"
done < <(grep -rl 'RemoteIpAddress?\.ToString()' --include='*.cs' backend/src 2>/dev/null \
  | grep -v '/obj/' | grep -v '/bin/' | sort)

# 6c. An audit entry is built by its module's journal and nowhere else. The journal is where a
#     module decides what an entry of its kind carries and what it must NOT — which identifiers
#     are recorded, what is redacted, how a system actor is spelled — and that decision is only
#     reviewable while it lives in one file. Identity built entries inline in thirteen handlers,
#     which is how three different spellings of the system actor reached one table, one of them
#     filling IpAddress/UserAgent with the actor's name against AuditEntry's own documentation
#     (rules/csharp.md "A module writes its audit entries through its own journal").
#
#     backend/src only, and the type's own file is exempt by construction: a constructor has to
#     be called somewhere, and Sdk/Contracts/SystemAuditEntry.cs is where AuditEntry is declared.
#
#     Three spellings, because C# has three and the check used to know one. `new AuditEntry(` is
#     the obvious one; `AuditEntry entry = new(…)` and `=> new(…)` from a member typed
#     `AuditEntry` are the target-typed forms, which name the type nowhere near the `new` and so
#     matched nothing. Measured: a file whose only content was
#     `public static AuditEntry Build() { return new(null, …); }` passed the whole gate.
while IFS= read -r file; do
  report "$file: builds an AuditEntry itself — write it through the module's <Module>AuditJournal (rules/csharp.md)"
done < <(find backend/src -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' 2>/dev/null \
  | grep -v 'AuditJournal\.cs$' | grep -v '/SystemAuditEntry\.cs$' | sort \
  | while IFS= read -r file; do
      python3 - "$file" <<'PYAUDIT'
import re
import sys

try:
    source = open(sys.argv[1], encoding="utf-8").read()
except FileNotFoundError:
    sys.exit(0)

# `new AuditEntry(` — the explicit form, spacing and line breaks included.
explicit = re.search(r"\bnew\s+AuditEntry\s*\(", source)
# `AuditEntry x = new(` / `AuditEntry x = new (` — target-typed, on one line.
assigned = re.search(r"\bAuditEntry\b[^;=\n]*=\s*new\s*\(", source)
# A member whose declared return type is AuditEntry, in a file that target-types a `new`.
returns = re.search(r"^\s*(?:(?:public|internal|private|protected|static|sealed|override|virtual)\s+)*AuditEntry\s+\w+\s*\(", source, re.M)
target_typed_new = re.search(r"(?:=>|\breturn)\s*new\s*\(", source)

if explicit or assigned or (returns and target_typed_new):
    print(sys.argv[1])
PYAUDIT
    done)

# 6d. A DI-registered type is a service, and services live in the module's Services/ — never
#     anywhere under Common/. Common/ is the module's inert furniture: DTOs, value objects and pure
#     rules over values, none of which the container ever constructs. The rule has a measurement
#     rather than an adjective behind it, which is the whole point: "is it module-specific" cannot
#     separate SecurityPolicyCache from SecurityPolicyDto (both are), but "does <Name>Module.cs
#     register it" separates them exactly. SecurityPolicyCache and IdentityAuditJournal sat in
#     Identity/Common/ for want of this line (rules/csharp.md "Common/ versus Services/").
#
#     The WHOLE Common/ subtree is considered, not just its top level. It used to be the top level
#     only, because Common/Interfaces/, Common/Options/ and Common/Validators/ held exactly the
#     kinds of type a registration legitimately mentions — the seam, the settings record, the
#     options validator — and 6d would have fired on all of them. That carve-out was a hole the
#     size of a folder: anything a module wanted to keep out of Services/ could be filed one level
#     down and the check went quiet. Those three folders are now module-root folders
#     (<Module>/Interfaces/, Options/, Validators/), matching Maran.Sdk and Maran.SharedKernel, so
#     nothing registered by design lives under Common/ any more and the exemption is gone with it.
registered_types_in_common() {
  find backend/src/Maran.Modules -maxdepth 2 -name '*Module.cs' \
    -not -path '*/obj/*' -not -path '*/bin/*' | sort | while IFS= read -r module_file; do
    module_dir="$(dirname "$module_file")"
    [ -d "$module_dir/Common" ] || continue
    # Generic AND non-generic registrations. The generic form was the only one read, and the
    # container has never required it: `services.AddSingleton(sp => new AuditEventDto())`
    # registers the type just as firmly and names it after a `new` rather than inside angle
    # brackets. Measured: that exact line, pointing at a type in Identity/Common/, passed the
    # whole gate. `TryAdd*` and `AddHostedService` are registrations too and are read here for
    # the same reason — a form this list does not hold is a form the check cannot see, not one
    # the rule permits (rules/testing.md).
    { grep -oE 'services\.(Try)?Add(Scoped|Singleton|Transient|HostedService)<[^>]*>' "$module_file" \
        | sed -E 's/.*<//; s/>$//'
      grep -E 'services\.(Try)?Add(Scoped|Singleton|Transient|HostedService)\(' "$module_file" \
        | grep -oE '(new [A-Za-z0-9_]+|typeof\([A-Za-z0-9_]+\))' \
        | sed -E 's/^new //; s/^typeof\(//; s/\)$//'
    } \
      | tr ',' '\n' \
      | sed -E 's/^ *//; s/ *$//; s/<.*//' \
      | while IFS= read -r type_name; do
          [ -n "$type_name" ] || continue
          found="$(find "$module_dir/Common" -name "$type_name.cs" \
            -not -path '*/obj/*' -not -path '*/bin/*' | sort | head -1)"
          if [ -n "$found" ]; then
            printf '%s\t%s\n' "$found" "$(basename "$module_file")"
          fi
        done
  done | sort -u
}
while IFS="$(printf '\t')" read -r file module_file; do
  report "$file: registered in $module_file — a type with a DI lifetime belongs in the module's Services/ (rules/csharp.md)"
done < <(registered_types_in_common)

# 6e. Nothing anywhere under a module's Common/ touches the HTTP surface. Check 6d's measurement is a
#     DI registration, and a static class never has one — so a static class escapes 6d whatever it
#     does. RefreshCookie passed both written tests (Identity-specific, never registered) and still
#     did not belong: it took an HttpResponse and MUTATED it, owning the refresh cookie's name,
#     path, HttpOnly/Secure/SameSite flags and expiry. Inert means NO EFFECT, not merely no DI
#     lifetime, and behaviour on the HTTP surface belongs in Controllers/ (rules/csharp.md
#     "Common/ versus Services/", the effect test).
#
#     Same scope as 6d — the whole Common/ subtree. Comments and doc comments are
#     stripped before matching, so a remark explaining why a handler has no HttpContext (as
#     LoginOutcome.cs carries) is not a violation; only real code is.
while IFS= read -r file; do
  report "$file: touches the HTTP surface — Common/ is inert furniture, HTTP behaviour belongs in Controllers/ (rules/csharp.md)"
done < <(find backend/src/Maran.Modules -mindepth 3 -path '*/Common/*.cs' \
  -not -path '*/obj/*' -not -path '*/bin/*' | sort | while IFS= read -r file; do
    #
    #     The decisive line is the namespace, not the type list. A list of type names is a list of
    #     the types somebody thought of: `IActionResult` and `IHttpContextAccessor` were not on it,
    #     and `IHttpContextAccessor` could not have been caught by the `\bHttpContext\b` entry
    #     either, because there is no word boundary after the `I`. Measured: a static class in
    #     Identity/Common/ returning `IActionResult` and taking an `IHttpContextAccessor` passed
    #     the whole gate. `using Microsoft.AspNetCore.` is the one observation that cannot be
    #     spelt around — inert furniture references the web framework not at all — and the type
    #     list stays beside it for a file reaching the surface through a global using.
    if sed -E 's://.*::g' "$file" \
      | grep -qE 'using[[:space:]]+Microsoft\.AspNetCore\.|\b(HttpResponse|HttpRequest|HttpContext|IHttpContextAccessor|CookieOptions|IHeaderDictionary|IResponseCookies|IRequestCookieCollection|IActionResult|ActionResult|ControllerBase|StatusCodes)\b'; then
      printf '%s\n' "$file"
    fi
  done)

# 6f. A module's Common/ holds `*Dto.cs` and nothing else. The map is unambiguous — "Common/:
#     *Dto.cs ONLY — the wire shapes, data with no logic" (rules/csharp.md) — and the folder has
#     still been corrected six rounds running, every round by a reviewer's eye. Checks 6d and 6e
#     catch the two kinds of misfiling that carry a measurable symptom (a DI registration, a reach
#     for the HTTP surface); this one needs neither, because the rule is about the NAME and the
#     name is observable on its own. A carrier, a policy, a mapper or an interface parked here is
#     rejected by the same line, whatever it does or does not do.
#
#     The subject is every file under the subtree, not just its top level, and not just `*.cs`: a
#     folder documented as inert wire shapes holds no `.json`, no `.resx` and no `.sql` either, and
#     scoping the check to `.cs` would have made "Common/ is DTOs" mean "the C# in Common/ is DTOs".
#
#     A vacuity guard sits on the axis that can go blind — the folder list. If no module has a
#     Common/ at all, this check compared nothing, and that reads exactly like a clean run
#     (rules/testing.md).
common_dirs="$(find backend/src/Maran.Modules -maxdepth 2 -type d -name Common \
  -not -path '*/obj/*' -not -path '*/bin/*' 2>/dev/null | sort)"
if [ -z "$common_dirs" ]; then
  report "backend/src/Maran.Modules: no module Common/ folder could be found — the 'Common/ is *Dto.cs only' check had nothing to look at (rules/testing.md)"
fi
while IFS= read -r file; do
  [ -n "$file" ] || continue
  report "$file: not a *Dto.cs — a module's Common/ holds the wire shapes and nothing else (rules/csharp.md)"
done < <(printf '%s\n' "$common_dirs" | while IFS= read -r common_dir; do
    [ -n "$common_dir" ] || continue
    find "$common_dir" -type f ! -name '*Dto.cs' -not -path '*/obj/*' -not -path '*/bin/*'
  done | sort)

# 6g. No logic on a DTO (rules/csharp.md "Common/ holds *Dto.cs and nothing else, and a DTO carries
#     no logic. No methods, no factories"). Check 6f polices the folder's file names; a file can
#     satisfy it and still be a service wearing a Dto suffix, so this one reads inside the type.
#
#     Every `*Dto.cs` in backend/src is the subject, not only the ones under Common/: the rule is
#     about the kind of type, and the agent-client DTOs are the same kind of type in a different
#     project.
#
#     What it observes, after comments and string literals are stripped and the type's own header
#     (including a positional record's parameter list) is skipped: an expression-bodied member
#     (`=>`), a property with an accessor body (`get {`, `set {`, `init {`), and a method or
#     constructor declaration — which is also how a static factory and a private constructor are
#     seen, since both are declarations of that shape.
#
#     What it CANNOT see, stated rather than implied (rules/testing.md): logic in a `partial` half
#     of the same DTO declared in another file; logic inside a type NESTED in the DTO, which it
#     reads as part of the body and reports at the file rather than at the nested type; a default
#     value in a primary-constructor parameter, which lives in the header it skips; and an
#     extension method over the DTO written anywhere else, which is logic ABOUT the DTO but not ON
#     it. It also cannot judge a type that does not end its file name in `Dto`.
dto_files="$(find backend/src -name '*Dto.cs' -not -path '*/obj/*' -not -path '*/bin/*' 2>/dev/null | sort)"
if [ -z "$dto_files" ]; then
  report "backend/src: no *Dto.cs could be found — the 'no logic on a DTO' check read nothing (rules/testing.md)"
fi
while IFS="$(printf '\t')" read -r file finding; do
  [ -n "$file" ] || continue
  report "$file: $finding — a DTO is data; logic lives on the domain (rules/csharp.md)"
done < <(printf '%s\n' "$dto_files" | while IFS= read -r file; do
    [ -n "$file" ] || continue
    python3 - "$file" <<'PYDTO'
import re
import sys

try:
    source = open(sys.argv[1], encoding="utf-8").read()
except OSError:
    sys.exit(0)

# Strings first, then comments: a `//` inside a string is not a comment, and a `=>` inside
# either is prose, not code. Raw, verbatim and ordinary literals all become empty.
source = re.sub(r'"""(?:.|\n)*?"""', '""', source)
source = re.sub(r'@"(?:[^"]|"")*"', '""', source)
source = re.sub(r'"(?:\\.|[^"\\\n])*"', '""', source)
source = re.sub(r"'(?:\\.|[^'\\\n])*'", "' '", source)
source = re.sub(r"/\*(?:.|\n)*?\*/", " ", source)
source = re.sub(r"//[^\n]*", "", source)

declaration = re.search(
    r"\b(record\s+class|record\s+struct|record|class|struct|interface)\s+[A-Za-z0-9_]+", source
)
if not declaration:
    sys.exit(0)

# Skip the header — a positional record's parameter list and any generic or base list — and
# start reading at the opening brace of the body. A declaration ending in `;` has no body.
index, depth, body = declaration.end(), 0, None
while index < len(source):
    character = source[index]
    if character in "(<[":
        depth += 1
    elif character in ")>]":
        depth -= 1
    elif depth <= 0 and character == "{":
        body = source[index + 1:]
        break
    elif depth <= 0 and character == ";":
        break
    index += 1

if body is None:
    sys.exit(0)

findings = []
if re.search(r"=>", body):
    findings.append("expression-bodied member (=>)")
if re.search(r"\b(get|set|init)\s*\{", body):
    findings.append("property with an accessor body")
member = re.search(
    r"^[ \t]*(?:\[[^\]\n]*\][ \t]*)*"
    r"(?:(?:public|internal|private|protected|static|sealed|override|virtual|abstract|extern|partial|async|new|readonly|unsafe)[ \t]+)+"
    r"(?:[A-Za-z0-9_<>,\.\[\]\?]+[ \t]+)?[A-Za-z0-9_]+[ \t]*\(",
    body,
    re.M,
)
if member:
    findings.append("method or constructor declaration: %s" % " ".join(member.group(0).split())[:60])

if findings:
    print("%s\t%s" % (sys.argv[1], "; ".join(findings)))
PYDTO
  done)

# 6h. Common/, Models/ and Mappers/ are FLAT — no subfolders (rules/csharp.md, which says it of all
#     three). The reason is not tidiness: a subfolder is where a file goes to stop being asked the
#     questions that decide whether it belongs in the folder at all. Common/Interfaces/,
#     Common/Options/ and Common/Validators/ existed for exactly that long, and check 6d's scope was
#     narrowed around them until they were removed — the folder was quietly moving things IN while
#     every review round moved something OUT.
#
#     Common/ is scanned here too rather than left to 6f: 6f rejects a non-DTO file wherever it
#     sits, but an empty `Common/Options/`, or one holding only `*Dto.cs`, is a landing site nobody
#     has used yet and is a violation before it is used.
while IFS= read -r directory; do
  [ -n "$directory" ] || continue
  report "$directory: a subfolder of $(basename "$(dirname "$directory")")/ — Common/, Models/ and Mappers/ are flat (rules/csharp.md)"
done < <(for folder in Common Models Mappers; do
    find backend/src/Maran.Modules -maxdepth 2 -type d -name "$folder" \
      -not -path '*/obj/*' -not -path '*/bin/*' 2>/dev/null \
      | while IFS= read -r parent; do
          find "$parent" -mindepth 1 -type d -not -path '*/obj/*' -not -path '*/bin/*'
        done
  done | sort)

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
#
#    The pattern matched the literal `mod tests {`, one space and that name exactly, so the rule
#    it enforced was "do not call it `tests`" rather than "do not inline it". Measured:
#    `mod tests{` — same module, brace closed up — passed the whole gate, and so would
#    `pub mod unit_tests {`. Any module whose name contains `test` is now the subject, and the
#    whitespace between the name and the brace is optional.
while IFS= read -r file; do
  if grep -qE '^[[:space:]]*(pub[[:space:]]+)?mod[[:space:]]+[A-Za-z0-9_]*test[A-Za-z0-9_]*[[:space:]]*\{' "$file"; then
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

try:
    lines = open(path, encoding="utf-8").readlines()
except FileNotFoundError:
    # The file was listed by find and deleted before this check opened it. In a tree several
    # sessions have open that is a race, not a violation — and it used to be reported as one,
    # because any non-zero exit from this script is read as a failed check: a run of this gate
    # printed "a property is declared after a method" for a file that no longer existed, above
    # its own traceback. A check must not report on what it could not observe (rules/testing.md).
    sys.exit(0)

first_method = None
for number, line in enumerate(lines, start=1):
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

try:
    lines = open(sys.argv[1], encoding="utf-8").readlines()
except FileNotFoundError:
    # Deleted between the find and this open — a race in a shared tree, not a violation (check 11).
    sys.exit(0)

first_function = None
for line in lines:
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
subject_named='adapter|adapter_for|detect|distro_info|os_release|family|path|name|domain|port'
subject_named="$subject_named|directory|current_uid|unit|pool|error|server"
subject_named="$subject_named|agent_options|options_error|peer_policy|peer_guard|render_validate_swap|rollback_guard"
subject_named="$subject_named|debian_paths|debian_packages|debian_services|rhel_paths|rhel_packages|rhel_services"

# 16b. Every name in that list still has a file. An exemption for a file that does not exist is
#      not harmless housekeeping: it is a named hole, and the day somebody adds a file of that
#      name check 16 waves through whatever public item it holds -- which is the exact failure
#      check 16 was written for (`adapter_selector.rs` came to hold `adapter_for`).
#
#      rules/security.md item 6: "an exemption for an entity that no longer exists fails too".
#      The C# gates apply that three times over
#      (AccountCascadeTests.Every_exemption_still_names_a_module_that_owns_tenant_rows,
#      AuditActionDisplayNameTests.Every_excused_journal_constant_still_names_a_member_that_exists,
#      and the audit-name census beside it); this list was the one exemption list in the tree
#      with no such guard, and it had already gone stale by three -- `ip_address`,
#      `cron_expression` and `user_config` named no file in `agent/crates` when this was added.
#
#      The search domain is check 16's own domain, character for character: an exemption is only
#      ever consulted for a file check 16 visits, so a guard searching anywhere else would report
#      on a population the exemption cannot apply to.
#      One traversal, not one per name: the population is read once into a set of bare file
#      names and every exemption is looked up in it.
subject_named_files="$(find agent/crates -name '*.rs' -not -path '*/target/*' -not -path '*/tests/*' 2>/dev/null |
                       sed -E 's|.*/||; s|\.rs$||' | sort -u)"
subject_named_count=0
while IFS= read -r exempt_name; do
  [ -n "$exempt_name" ] || continue
  subject_named_count=$((subject_named_count + 1))
  if ! printf '%s\n' "$subject_named_files" | grep -qxF "$exempt_name"; then
    report "scripts/lib/check-structure.sh: check 16 exempts '$exempt_name' from the file-name law and no agent/crates/**/$exempt_name.rs exists -- an exemption for a file that does not exist is a hole the next file of that name falls into (rules/security.md)"
  fi
done < <(printf '%s\n' "$subject_named" | tr '|' '\n')
# The vacuity guard, on the axis that can go blind: the list itself. If `subject_named` were
# renamed, emptied or split on the wrong character, the loop above would iterate over nothing and
# report nothing -- which reads exactly like a clean list (rules/testing.md, "a vacuity guard must
# be on the axis that can go blind").
if [ "$subject_named_count" -eq 0 ]; then
  report "scripts/lib/check-structure.sh: check 16's subject-named exemption list read as empty, so the staleness guard examined no exemption at all -- that is a failure to observe and not a clean list (rules/testing.md)"
fi
# And the other half of the same axis: the population. An empty file set would make EVERY
# exemption look stale, which is a loud failure rather than a silent one, but it would blame the
# exemptions for a traversal that found nothing. Named as what it is instead.
if [ -z "$subject_named_files" ]; then
  report "scripts/lib/check-structure.sh: no .rs files were found under agent/crates for check 16's staleness guard to look an exemption up in, so nothing below is a statement about the exemptions (rules/testing.md)"
fi

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

# 17b. No program spawned by a bare name. The agent runs as uid 0, so a program named without a
#      path is "whichever binary the first directory in PATH happens to hold" — one writable
#      directory away from arbitrary code as root, on operations an unprivileged customer can
#      trigger by creating an account. Rule 17 above catches a platform PATH written in `ops`;
#      this catches the opposite mistake, which is writing no path at all.
#
#      It exists because the unit tests could not: `ProcessSystemHost` is the one type that
#      spawns without going through `AccountOperations`, it is deliberately not unit-tested
#      ("cannot be tested without creating real users"), and a bare `id` survived there through a
#      review whose whole subject was this class of bug. A grep is a poor test and a fine gate.
while IFS= read -r file; do
  offenders="$(grep -nE '(Command::new|\.run|\.run_with_stdin|expect_success)\("[^/]' "$file" |
               grep -v '^[0-9]*:\s*//' || true)"
  if [ -n "$offenders" ]; then
    line="$(printf '%s' "$offenders" | head -1 | cut -d: -f1)"
    report "$file:$line: program spawned by a bare name — as root, PATH decides which binary that is; ask the DistroAdapter (rules/security.md)"
  fi
done < <(find agent/crates/ops/src agent/crates/agent/src -name '*.rs' -not -path '*/tests/*' 2>/dev/null | sort)

# 17c. The provisioned-password alphabet is the same string in all THREE languages that hold it.
#      The backend mints these passwords, the agent's `Password` type refuses anything outside its
#      set, and the SPA generates one for the first-run administrator. A character the agent
#      refuses is a provisioning that fails AFTER the panel has shown the customer a credential;
#      a character dropped from one copy is entropy lost silently. C# and Rust are already pinned
#      to each other by ProvisionedPasswordGeneratorTests; nothing pinned the third copy, which is
#      exactly the kind of drift no compiler in any of the three can see.
#      Both are extracted by the CONSTANT'S NAME rather than by the shape of the string, so a
#      narrowed or widened alphabet is reported as the difference it is instead of as "cannot be
#      read" — the first version of this check matched a literal of exactly 67 characters and told
#      an honest mismatch it could not find the value at all.
csharp_alphabet="$(grep -A2 'public const string Alphabet' backend/src/Maran.SharedKernel/Security/ProvisionedPasswordGenerator.cs 2>/dev/null |
                   grep -oE '"[^"]*"' | head -1 | tr -d '"')"
spa_alphabet="$(grep -E "^const ALPHABET = " frontend/src/utils/generate.ts 2>/dev/null |
                grep -oE "'[^']*'" | head -1 | tr -d "'")"
if [ -z "$csharp_alphabet" ] || [ -z "$spa_alphabet" ]; then
  report "frontend/src/utils/generate.ts: the password alphabet could not be read from both the SPA and the backend — one of them moved, and the two can no longer be compared"
elif [ "$csharp_alphabet" != "$spa_alphabet" ]; then
  report "frontend/src/utils/generate.ts: the SPA's password alphabet differs from ProvisionedPasswordGenerator.Alphabet (rules/security.md)"
fi

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

# 19. Every polygon suite is named in docker/README.md's run commands. The commands pass an explicit
#    --test list rather than running everything ignored, so a suite absent from the file is a suite
#    nobody runs — and that is not hypothetical: the file listed six of ten for the whole of plan 5,
#    omitting exactly the four newest (cron, firewall, monitor, binary_paths), while everyone
#    following it believed they had run the polygon.
#
#    A polygon suite is identified by what it CONTAINS, not by what it is called. The glob was
#    `*_on_a_real_host.rs`, which made the check's subject the naming convention rather than the
#    suite: a file named `quota_polygon.rs` holding `#[ignore]`-gated host tests was not merely
#    unlisted, it was unseen. Measured — that file, absent from docker/README.md, passed the
#    whole gate. `#[ignore]` is the marker that actually separates the two kinds of integration
#    test here: every polygon suite in this tree carries it, and `handshake.rs`,
#    `shutdown_signal.rs` and `golden_test.rs` do not.
#
#    No COUNT is written down in this comment, and that is deliberate. It used to say "the eleven
#    polygon suites" while the tree held thirteen, and nothing could notice: this rule gates the
#    suite NAMES in docker/README.md in both directions, and gates no numeral beside them, so a
#    reader met gated names and an ungated number in one paragraph with nothing to tell them apart.
#    The count is derivable — `find agent/crates/*/tests -maxdepth 1 -name '*.rs' | xargs grep -l
#    '#\[ignore'` is what the loop below does — so the honest form is to derive it where it is
#    needed and never to record it here. A numeral in prose cannot be gated in general anyway: a
#    checker cannot tell a claim about today from a record of one measured run, and this repository
#    has both ("three of eleven suites announced themselves as ...", scripts/lib/suite.sh, which
#    must NOT be rewritten when a suite is added).
#
#    The inverse runs too, because a refusing gate owes an accepted case and this one owes a
#    second direction: a `--test` naming no suite is a command that errors out for everyone who
#    copies it, and it appears the moment a suite is renamed.
polygon_suites=""
while IFS= read -r suite_file; do
  [ -e "$suite_file" ] || continue
  grep -q '#\[ignore' "$suite_file" || continue
  suite="$(basename "$suite_file" .rs)"
  polygon_suites="$polygon_suites $suite"
  if ! grep -q -- "--test $suite" docker/README.md 2>/dev/null; then
    report "docker/README.md: does not name the polygon suite '$suite' in a run command — a suite absent from that list is a suite nobody runs"
  fi
done < <(find agent/crates/*/tests -maxdepth 1 -name '*.rs' 2>/dev/null | sort)

if [ -z "$polygon_suites" ] && [ -d agent/crates/agent/tests ]; then
  report "agent/crates/*/tests: no polygon suite could be identified — this check had nothing to compare docker/README.md against (rules/testing.md)"
fi

while IFS= read -r named; do
  [ -n "$named" ] || continue
  case " $polygon_suites " in
    *" $named "*) ;;
    *) report "docker/README.md: names '--test $named', which is no suite in agent/crates/*/tests — the command it appears in cannot run" ;;
  esac
done < <(grep -oE -- '--test [A-Za-z0-9_]+' docker/README.md 2>/dev/null | sed 's/^--test //' | sort -u)

# 20. The agent's systemd hardening and its own idea of what it writes do not drift apart.
#     ReadWritePaths= in installer/systemd/maran-agent.service is a second, independent
#     description of every location AgentPaths (agent/crates/agent-core/src/agent_paths.rs)
#     names as a writable root outside /etc — /etc is not restricted by this unit's
#     ProtectSystem=true regardless of what is listed, so an /etc-rooted constant is not
#     checked here. A constant the unit's writable set does not cover is exactly the class
#     of drift that let the unit go on naming /var/lib/maran as the backup destination
#     while every backup.CreateBackup wrote to /var/backups/maran instead: nothing compiles,
#     lints or runs this file, so only a check like this one would have caught it before a
#     real host did.
#
#     The extraction is deliberately NOT filtered to a list of known prefixes. It used to
#     select only constants under /var, /run or /home, which meant a writable root added
#     under /opt or /srv was invisible to the very check that exists to notice a new
#     writable root — a check blind to the thing it polices (rules/testing.md). Every
#     absolute-path constant is now considered and only /etc is excluded, by the reason
#     stated above rather than by an accident of which prefixes someone thought of.
unit_file="installer/systemd/maran-agent.service"
paths_file="agent/crates/agent-core/src/agent_paths.rs"
if [ -f "$unit_file" ] && [ -f "$paths_file" ]; then
  unit_line="$(grep '^ReadWritePaths=' "$unit_file" | head -1 | sed 's/^ReadWritePaths=//')"
  if [ -z "$unit_line" ]; then
    report "$unit_file: no ReadWritePaths= line — this check cannot observe the unit's writable set (rules/testing.md)"
  fi
  # Extracted in Python so a declaration wrapped across lines is still seen: the shell
  # regex this replaced matched `pub const NAME: &'static str = "…"` on ONE line only, so
  # a rustfmt line break would have retired the constant from the check silently.
  writable_roots="$(python3 - "$paths_file" <<'PYPATHS'
import re
import sys

source = open(sys.argv[1], encoding="utf-8").read()
# `pub const NAME: &'static str = "value";` with any whitespace, newlines included.
pattern = re.compile(
    r"pub\s+const\s+[A-Z0-9_]+\s*:\s*&'static\s+str\s*=\s*\"([^\"]*)\"", re.S
)
for value in pattern.findall(source):
    if not value.startswith("/"):
        continue
    if value == "/etc" or value.startswith("/etc/"):
        continue
    print(value)
PYPATHS
)"
  # A vacuity guard on the axis that can actually go blind: the extraction. An empty
  # result reads exactly like a clean run, so it is reported as the failure to observe
  # that it is (rules/testing.md "a vacuity guard must be on the axis that can go blind").
  if [ -z "$writable_roots" ]; then
    report "$paths_file: no absolute-path constants could be read — the ReadWritePaths= comparison had nothing to compare (rules/testing.md)"
  fi
  while IFS= read -r writable_root; do
    [ -n "$writable_root" ] || continue
    covered=0
    for entry in $unit_line; do
      stripped="${entry#-}"
      case "$writable_root" in
        "$stripped"|"$stripped"/*) covered=1 ;;
      esac
    done
    if [ "$covered" -eq 0 ]; then
      report "$unit_file: ReadWritePaths= does not cover '$writable_root' ($paths_file) — a path the agent writes outside /etc must appear in the unit's writable set (rules/architecture.md)"
    fi
  done <<< "$writable_roots"
fi

# 21. No source file cites `.superpowers/`. That directory's own .gitignore is the single line `*`
#     and `git ls-files .superpowers` returns nothing, so EVERY path under it is absent from every
#     clone: a doc comment that sends a reader there sends them nowhere, and rules/architecture.md
#     makes a comment describing what the code does not do a defect of the same severity as the
#     behaviour. The worst instance was operator-facing — a red polygon assertion told whoever was
#     reading it to "read the options in .superpowers/sdd/ftps-real-host-report.md", which was also
#     the wrong file. Twenty-three such citations existed in nineteen files.
#
#     Why a grep for one literal rather than a general "does this cited path exist" check: the
#     general form is not decidable cheaply. Comments carry globs, `<placeholder>` segments, system
#     paths, prose fragments and paths of files that are legitimately untracked — on this very
#     branch most of the landed tree is untracked — so an index lookup would be mostly false
#     positives, which is a gate nobody keeps green. This directory is different: it is ignored BY
#     CONSTRUCTION, so any citation of it is dangling with no judgement required.
#
#     UNOBSERVED HERE: a citation of a path that exists but says something ELSE. The FTPS pin above
#     cited a real scratch file that held no options at all, and no mechanical check can see that.
#     `.md` and `.txt` are now swept too: the five citations in scripts/test-baseline.txt and the one
#     in docker/README.md that blocked this widening were replaced with committed sources, and a
#     citation in an operator-facing text file is worse than one in a doc comment, not better,
#     because the reader is likelier to try the path. TWO files are excluded, for the same reason
#     rather than two: a document that must NAME this defect in order to teach it cannot also be
#     forbidden from naming it. THIS file is excluded, because a check has to name the
#     thing it forbids in order to forbid it — measured: without that exclusion the gate reported
#     itself and nothing else. The cost of that exclusion is that a citation added to this one file
#     is invisible to it, which is the narrowest hole available and is stated rather than hidden.
#     rules/testing.md is excluded on the same ground: its "a negative search is a check" bullet
#     quotes `grep -rn 'owed' .superpowers/sdd` -> 0 beside /usr/bin/grep -> 587, which is the
#     measurement that bullet exists to teach. The cost of both exclusions is that a citation added
#     to either file is invisible here; the alternative is a rule that cannot show its own evidence.
#
#     `docs` is now swept too. The sweep originally covered only `agent backend frontend installer
#     scripts docker rules` and missed `docs/` entirely; a documents-only pass found 80 dangling
#     citations across 25 files under docs/superpowers/{notes,plans} — 21 threat notes and 4 plans —
#     none of them caught until that pass went looking by hand. A threat note or a plan is read by
#     the same reviewer this check protects everywhere else, so a dangling path there is the same
#     defect this rule exists to catch, not a lesser one because it sits in docs/ rather than a doc
#     comment.
while IFS= read -r file; do
  hits="$(grep -n '\.superpowers' "$file" || true)"
  if [ -n "$hits" ]; then
    line="$(printf '%s' "$hits" | head -1 | cut -d: -f1)"
    report "$file:$line: cites .superpowers/, which no clone contains (its .gitignore is \`*\`) — put the argument in the doc comment, a threat note under docs/superpowers/notes/, or rules/ (rules/architecture.md)"
  fi
done < <(find agent backend frontend installer scripts docker rules docs \
           \( -name '*.rs' -o -name '*.cs' -o -name '*.sh' -o -name '*.md' -o -name '*.txt' \) \
           -not -path '*/target/*' -not -path '*/obj/*' -not -path '*/bin/*' \
           -not -path '*/node_modules/*' -not -path '*/dist/*' \
           -not -name 'check-structure.sh' -not -path 'rules/testing.md' 2>/dev/null | sort)

# The vacuity guard, on the axis that can go blind: the file list. An empty sweep reads exactly
# like a clean one (rules/testing.md).
if [ "$(find agent backend scripts docker \( -name '*.rs' -o -name '*.cs' -o -name '*.sh' \) \
          -not -path '*/target/*' -not -path '*/obj/*' -not -path '*/bin/*' 2>/dev/null | wc -l)" -lt 100 ]; then
  report "scripts/lib/check-structure.sh: check 21 swept fewer than 100 source files, so it is not a statement about citations (rules/testing.md)"
fi

if [ "$violations" -gt 0 ]; then
  echo
  echo "$violations structural violation(s). See rules/ for the rule each one cites."
  exit 1
fi

echo "STRUCTURE-OK"
