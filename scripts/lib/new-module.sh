#!/usr/bin/env bash
# Scaffolds the canonical anatomy for one backend module (rules/csharp.md
# "Canonical backend layout", rules/architecture.md "Skeleton policy"). Keeps every
# module identical in shape. It creates directories only; a directory stays untracked
# until its first real file lands in it.
#
# Usage: scripts/maran module <Name>        (e.g. scripts/maran module Accounts)
# Creates backend/src/Maran.Modules/<Name>/ (short folder name;
# the csproj inside carries the full name Maran.Modules.<Name>.csproj)
# and the matching test project folder. Does NOT touch the solution file —
# adding the csproj to Maran.sln is a deliberate, reviewed step.
set -euo pipefail

if [ $# -ne 1 ] || ! [[ "$1" =~ ^[A-Z][A-Za-z0-9]+$ ]]; then
  echo "usage: maran module <ModuleName>   (PascalCase, e.g. Accounts)" >&2
  exit 1
fi

name="$1"
root="$(cd "$(dirname "$0")/../.." && pwd)"
mod="$root/backend/src/Maran.Modules/$name"
tests="$root/backend/tests/Maran.Modules.$name.Tests"

if [ -e "$mod" ]; then
  echo "refusing: $mod already exists" >&2
  exit 1
fi

# The canonical module anatomy (kept in sync with rules/csharp.md).
#
# Interfaces/, Options/, Validators/, Models/ and Mappers/ are deliberately NOT scaffolded. They
# are real module folders in rules/csharp.md's map — the same place Maran.Sdk, Maran.SharedKernel
# and Maran.Host put theirs — but a module that has no seam, no settings record, no shared input
# validator, no internal carrier and no mapper should not be given five empty folders to file
# into. Pre-created empty folders are how a layout spreads without anyone deciding it: forty-seven
# such folders existed across sixteen modules and thirty-six of them were empty .gitkeep
# scaffolding. Create the folder with its first real file.
#
# Controllers/Requests/ is not scaffolded either, and unlike the five above it is not merely
# deferred: it is abolished. An endpoint binds its Command or Query directly (rules/csharp.md,
# "An endpoint binds its command directly"), so there is no per-endpoint request model to file.
for d in \
  "Controllers" \
  "Commands" \
  "Queries" \
  "Common" \
  "IntegrationEvents/Events" \
  "IntegrationEvents/Handlers" \
  "Services" \
  "Jobs" \
  "Authorization" \
  "Domain" \
  "Domain/Enums" \
  "Domain/Events" \
  "Domain/Interfaces" \
  "Persistence/Configurations" \
  "Persistence/Interceptors" \
  "Persistence/Migrations" \
  "Seeders" \
  "Resources" \
  "Errors"; do
  mkdir -p "$mod/$d"
done
mkdir -p "$tests"

echo "created: $mod"
echo "created: $tests"
echo "next steps: add ${name}Module.cs + Manifest.cs + Maran.Modules.${name}.csproj, register in Maran.sln and ModuleRegistry."
