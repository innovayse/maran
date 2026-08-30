#!/usr/bin/env bash
# Generates THIRD-PARTY-NOTICES.md from the dependencies this repository actually builds with.
#
# The installer distributes BINARIES built from those dependencies, and the MIT, Apache-2.0 and
# BSD licences they carry all require their notices to travel with the distribution. Shipping
# without this file is a licence violation, quietly, in every release.
#
# Everything is read from metadata already on disk — cargo's own resolution, the .nuspec of each
# restored NuGet package, the manifest of each installed npm module — so the file can be
# regenerated with no network and says what the build actually used.
#
# An unreadable licence is an ERROR, never a placeholder. The first version wrote "see package"
# for anything missing from the local cache, which made the output depend on the reader's disk:
# the same file regenerated elsewhere differed, and the check comparing them could never pass.
#
# Usage:
#   scripts/maran licenses           write THIRD-PARTY-NOTICES.md
#   scripts/maran licenses --check   fail if the file is out of date, changing nothing
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"

# shellcheck disable=SC1091
. "$root/scripts/dev"

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required: apt-get install jq (or dnf install jq)" >&2
  exit 1
fi

check_only=0
if [ "${1:-}" = "--check" ]; then
  check_only=1
fi

output="$root/THIRD-PARTY-NOTICES.md"
generated="$(mktemp)"
missing="$(mktemp)"
trap 'rm -f "$generated" "$missing"' EXIT

# The licence a NuGet package declares. Its .nuspec is small, flat XML with one <license> or
# <licenseUrl> element, so it is read with sed rather than by pulling in an XML parser.
nuspec_licence() {
  local nuspec="$1" value
  [ -f "$nuspec" ] || return 1

  value="$(sed -n 's/.*<license[^>]*>\([^<]*\)<\/license>.*/\1/p' "$nuspec" | head -1)"
  [ -z "$value" ] && value="$(sed -n 's/.*<licenseUrl>\([^<]*\)<\/licenseUrl>.*/\1/p' "$nuspec" | head -1)"

  # A package that ships no licence metadata at all. Naming that is the honest answer; writing
  # "see package" would hide the one row a human has to go and check.
  printf '%s\n' "${value:-not declared}"
}

# Every crate the agent links, minus our own, from cargo's own resolution of Cargo.lock.
crates() {
  (cd "$root/agent" && cargo metadata --format-version 1 --all-features --offline) |
    jq -r '.packages[]
           | select(.name | startswith("maran-") | not)
           | [.name, .version, (.license // "not declared")]
           | @tsv' |
    sort -u
}

# The packages the projects actually RESOLVED, from every project.assets.json.
#
# Not from Directory.Packages.props: that file DECLARES versions, and some are referenced by
# nothing. An unused declaration is never downloaded, so its licence cannot be read — and it
# does not ship either, so it does not belong in a notices file in the first place.
nuget_packages() {
  local cache="$HOME/.nuget/packages" name version licence
  # The loop's own output is sorted, not the function's stdin: a bare `sort -u` after the loop
  # inherits the caller's stdin and waits on it forever, which is what it did.
  {
  while IFS=$'\t' read -r name version; do
    if licence="$(nuspec_licence "$cache/${name,,}/$version/${name,,}.nuspec")"; then
      printf '%s\t%s\t%s\n' "$name" "$version" "$licence"
    else
      printf '%s %s\n' "$name" "$version" >> "$missing"
    fi
  done < <(find "$root/backend" -name project.assets.json -path '*/obj/*' -print0 |
             xargs -0 -r jq -r '.libraries | to_entries[]
                                | select(.value.type == "package")
                                | .key | split("/") | @tsv' |
             sort -u)
  } | sort -u
}

# The application's runtime dependencies. `npm ls --omit=dev` walks what actually ships; the
# licence is in each installed package's own manifest.
npm_packages() {
  local name version licence manifest
  {
  while IFS=$'\t' read -r name version; do
    manifest="$root/frontend/node_modules/$name/package.json"
    if [ -f "$manifest" ]; then
      licence="$(jq -r 'if (.license | type) == "string" then .license else "not declared" end' "$manifest")"
      printf '%s\t%s\t%s\n' "$name" "$version" "$licence"
    else
      printf '%s %s\n' "$name" "$version" >> "$missing"
    fi
  done < <((cd "$root/frontend" && npm ls --omit=dev --all --parseable 2>/dev/null || true) |
             grep '/node_modules/' |
             sed "s|^$root/frontend/||" |
             sed 's|.*node_modules/||' |
             sort -u |
             while IFS= read -r package; do
               manifest="$root/frontend/node_modules/$package/package.json"
               [ -f "$manifest" ] || continue
               jq -r '[.name, .version] | @tsv' "$manifest"
             done)
  } | sort -u
}

table() {
  printf '## %s\n\n%s\n\n' "$1" "$2"
  printf '| Package | Version | Licence |\n|---|---|---|\n'
  while IFS=$'\t' read -r name version licence; do
    [ -n "$name" ] && printf '| `%s` | %s | %s |\n' "$name" "$version" "$licence"
  done
  printf '\n'
}

{
  cat <<'HEADER'
# Third-party notices

Maran is distributed as binaries built from the packages below. Each belongs to its own
authors and is used under its own licence; this file is the attribution those licences
require. It says nothing about the licence of Maran itself, which is `LICENSE`.

Generated by `maran licenses` from metadata on disk — cargo's resolution of `Cargo.lock`, the
`.nuspec` of each restored NuGet package, and the manifest of each installed npm module. Do not
edit it by hand; run the command.

HEADER
  crates          | table 'Rust — the agent' 'Resolved by cargo from `agent/Cargo.lock`.'
  nuget_packages  | table '.NET — the panel' 'Resolved by restore; versions are pinned in `backend/Directory.Packages.props`.'
  npm_packages    | table 'npm — the application' 'Runtime dependencies only; build and test tooling is not distributed.'
} > "$generated"

if [ -s "$missing" ]; then
  echo "Could not read a licence for these — they are not on disk:" >&2
  sort -u "$missing" | sed 's/^/  /' >&2
  echo "Run \`dotnet restore backend/Maran.sln\` and \`npm ci\` in frontend/, then try again." >&2
  exit 1
fi

if [ "$check_only" -eq 1 ]; then
  if [ ! -f "$output" ] || ! diff -q "$output" "$generated" >/dev/null; then
    echo "THIRD-PARTY-NOTICES.md is out of date. Run: maran licenses" >&2
    [ -f "$output" ] && diff -u "$output" "$generated" | head -40
    exit 1
  fi
  echo "NOTICES-OK"
  exit 0
fi

cp "$generated" "$output"
echo "wrote $(basename "$output")"
