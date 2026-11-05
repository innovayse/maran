#!/usr/bin/env bash
# The ONE place that knows how the panel's OpenAPI document is produced, SOURCED by
# scripts/lib/openapi.sh (`maran openapi`) and scripts/lib/release-openapi.sh (`maran release openapi`)
# so the gate and the release artefact can never describe the API by two different routes.
#
# The panel writes its own document and exits (`Maran.Host --cli openapi <path>`): no listener, no
# database connection, no agent call, and no endpoint that serves it (rules/security.md item 10). The
# Release build is run first and the built Host assembly is run directly, so the generator sees exactly
# the controllers the shipped binary has. The `Testing` environment is what the CLI verb is composed
# for; it is not a production setting.
#
# Needs from the caller: `root` (the repository root) and the .NET SDK on PATH.

# Exit statuses of openapi_emit. Build failure has no status of the CLI's own, so it takes EX_SOFTWARE.
OPENAPI_EMIT_BUILD_FAILED=70
# The CLI's own statuses (backend/src/Maran.Host/Cli/Enums/CliExitCode.cs): 64 a bad argument, 69 the
# generator was not registered in the composed container, 77 the process runs as root.
OPENAPI_EMIT_USAGE=64
OPENAPI_EMIT_UNAVAILABLE=69
OPENAPI_EMIT_ROOT=77

# openapi_emit <output-path>: builds the Host in Release, then runs it. Returns the Host's own status,
# or OPENAPI_EMIT_BUILD_FAILED when the build failed (nothing was run then).
openapi_emit() {
  local project="$root/backend/src/Maran.Host/Maran.Host.csproj"
  local dll="$root/backend/src/Maran.Host/bin/Release/net9.0/Maran.Host.dll"
  dotnet build "$project" -c Release -nologo -v:minimal || return "$OPENAPI_EMIT_BUILD_FAILED"
  [ -f "$dll" ] || {
    echo "the Release build produced no $dll" >&2
    return "$OPENAPI_EMIT_BUILD_FAILED"
  }
  ASPNETCORE_ENVIRONMENT=Testing dotnet "$dll" --cli openapi "$1"
}

# openapi_generate <output-path> <log-path>: runs openapi_emit and classifies the outcome, so a caller
# never produces a verdict about a document that was not generated. Returns 0 when the Host exited 0
# AND the file is non-empty; 2 (the harness's "did not run") when the answer is a fact about the
# environment (running as root); 1 otherwise. OPENAPI_GENERATE_REASON carries one honest sentence.
# Statuses 64 and 69 are FAILURES of this tree or this script (a malformed call, a generator missing
# from the composition), never an environment fact, so they are not softened into "did not run".
openapi_generate() {
  local out="$1" log="$2" status=0
  OPENAPI_GENERATE_REASON=""
  openapi_emit "$out" >"$log" 2>&1 || status=$?
  case "$status" in
    0) ;;
    "$OPENAPI_EMIT_ROOT")
      OPENAPI_GENERATE_REASON="the panel refuses to run as root, so no document was generated. Run this as an unprivileged user. This is the environment, not the API."
      return 2
      ;;
    "$OPENAPI_EMIT_BUILD_FAILED")
      OPENAPI_GENERATE_REASON="the Release build of the panel failed (log tail printed above), so no document was generated."
      return 1
      ;;
    "$OPENAPI_EMIT_USAGE")
      OPENAPI_GENERATE_REASON="the panel rejected its command line (exit 64): this script called it wrongly, and no document was generated."
      return 1
      ;;
    "$OPENAPI_EMIT_UNAVAILABLE")
      OPENAPI_GENERATE_REASON="the OpenAPI generator is not registered in the composed panel (exit 69): a composition fault in this tree, and no document was generated."
      return 1
      ;;
    *)
      OPENAPI_GENERATE_REASON="the panel exited $status instead of writing a document (log tail printed above)."
      return 1
      ;;
  esac
  if [ ! -s "$out" ]; then
    OPENAPI_GENERATE_REASON="the panel exited cleanly but wrote no document to the path it was given."
    return 1
  fi
  return 0
}
