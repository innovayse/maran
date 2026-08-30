#!/usr/bin/env bash
# Scaffolds the canonical anatomy for one backend module (rules/csharp.md
# "Canonical backend layout", rules/architecture.md "Skeleton policy"). Keeps every
# module identical in shape; empty folders are held by zero-byte .gitkeep files.
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
for d in \
  "Controllers/Requests" \
  "Commands" \
  "Queries" \
  "Common" \
  "Common/Interfaces" \
  "Common/Options" \
  "Common/Validators" \
  "IntegrationEvents/Events" \
  "IntegrationEvents/Handlers" \
  "Services" \
  "Jobs" \
  "Authorization" \
  "Domain" \
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
