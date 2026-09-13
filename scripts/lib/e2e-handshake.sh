#!/usr/bin/env bash
# Proves the two processes actually talk: starts the Rust agent on a temporary unix socket, starts
# the C# API pointed at that socket, and asserts /health reports the agent as connected.
#
# This is the one check neither side can pass alone — the backend suite stubs the agent, and the
# agent suite has no API — so it is what catches a contract that compiles on both sides and still
# does not match on the wire.
#
# THREE ANSWERS, NOT TWO, and this command needed them more than most: it BUILDS the agent, so a
# missing toolchain and a genuine contract mismatch used to share one word.
#
#   HANDSHAKE VERDICT: OK            exit 0   both processes started and the API reached the agent
#   HANDSHAKE VERDICT: FAILED        exit 1   they were both asked, and the seam does not hold —
#                                             the agent does not compile, does not start, refuses
#                                             the wrong command lines, or the API cannot reach it
#   HANDSHAKE VERDICT: DID NOT RUN   exit 2   the seam was never exercised: a toolchain this
#                                             command builds with is absent, or there is no
#                                             database for the API to boot against
#
# WHICH ARE FINDINGS AND WHICH ARE FACTS ABOUT THE MACHINE. This is a WIRED MERGE GATE —
# `.github/workflows/cross.yml` runs it, and it is the only check that observes the contract on the
# wire, because the backend suite stubs the agent and the agent suite has no API. So its FAILED must
# mean the branch: the agent's source, the API's source, or the proto between them. Everything the
# runner or the workstation owes this command — cargo, a C linker, protoc, the .NET SDK, curl, a
# reachable PostgreSQL — is a fact about the machine and refuses with DID NOT RUN. Nothing is
# claimed about the seam unless the seam was reached.
#
# Measured before that third answer existed, with `cargo` off PATH: this command printed
# `building the agent` and nothing else on stdout, `HANDSHAKE-FAILED: the agent did not build` on
# **stderr**, and returned **1** — the word FAILED, on a gate scored by status, about an agent whose
# source it had never read. A broken cargo install on a runner was recorded as a contract failure of
# this branch.
#
# Prints HANDSHAKE-OK and exits 0 on success. Requires a reachable panel database: the API refuses
# to start without one, and this script starts the development container when Docker is available.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"

# shellcheck disable=SC1091
. "$root/scripts/dev"

# For `suite_require_toolchain`, `suite_did_not_run` and `$SUITE_STATUS_DID_NOT_RUN`. Sourced, not
# reimplemented: this command refuses for the same reasons `maran test` and `maran format` refuse,
# and `maran lock selftest` is what keeps that one vocabulary honest across the harness.
# shellcheck disable=SC1091
. "$root/scripts/lib/suite.sh"

work="$(mktemp -d)"
socket="$work/agent.sock"
api_url="http://127.0.0.1:5081"
api_log="$work/api.log"
agent_log="$work/agent.log"
agent_pid=""
api_pid=""

cleanup() { # always stop both processes, whatever went wrong
  trap - EXIT INT TERM
  [ -n "$api_pid" ] && kill "$api_pid" 2>/dev/null || true
  [ -n "$agent_pid" ] && kill "$agent_pid" 2>/dev/null || true
  wait 2>/dev/null || true
  rm -rf "$work"
}
trap cleanup EXIT INT TERM

fail() { # a FINDING: both sides were asked and the seam does not hold. Reserved for that.
  # The verdict goes to STDOUT. The logs stay on stderr — they are diagnosis, and a CI step that
  # keeps stdout to score the gate should not have to wade through a 40-line API log to find the
  # one line that is the answer. Before this, the whole of the output was on stderr and stdout
  # carried nothing but `building the agent`.
  echo "HANDSHAKE VERDICT: FAILED — HANDSHAKE-FAILED: $1"
  echo "HANDSHAKE-FAILED: $1" >&2
  echo "--- agent log ---" >&2
  cat "$agent_log" >&2 2>/dev/null || true
  echo "--- api log ---" >&2
  tail -40 "$api_log" >&2 2>/dev/null || true
  exit 1
}

did_not_run() { # NOT a finding: the seam was never reached. Verdict on stdout, status 2.
  suite_did_not_run "HANDSHAKE VERDICT" "$*"
  exit "$SUITE_STATUS_DID_NOT_RUN"
}

# THE TOOLCHAIN, before the database and long before the first line of anybody's source is read.
# rules/testing.md: a toolchain guard "must run BEFORE the first stage, and nothing above it may be
# allowed to kill the script first". Every tool below is one this command cannot do its job without,
# and each had its own way of turning its own absence into an accusation against the branch:
#
#   cargo, cc   `cargo build -p maran-agent` -> "the agent did not build". The C linker is required
#               here and not merely cargo, unlike the format gate: this builds a BINARY and links it.
#   protoc      the agent's build.rs generates the proto contract at compile time, so without it
#               cargo fails in a way indistinguishable, at the old exit paths, from a contract that
#               genuinely does not compile — the single worst confusion this command could produce.
#   dotnet 9    `dotnet run` for the API. Absent or too old, it never binds, the 90-second curl loop
#               expires and the answer was "the api never answered" — a claim about the API.
#   curl        the only thing that asks /health. Absent, it also reads as "the api never answered",
#               a finding about a question nobody managed to put.
suite_require_toolchain rust ||
  did_not_run "the Rust toolchain this command BUILDS the agent with is absent (named above), so" \
    "no agent source was compiled and nothing was asked of the seam."

command -v protoc >/dev/null 2>&1 || {
  echo "REFUSED: protoc is not on PATH — the agent's build.rs generates the proto contract at" >&2
  echo "         compile time, so the build would fail for the environment's reason, not the" >&2
  echo "         contract's. source scripts/dev first, and see: maran check" >&2
  did_not_run "protoc, which the agent's contract is generated by, is not on PATH."
}

suite_require_toolchain backend ||
  did_not_run "the .NET SDK this command starts the API with is absent or too old (named above)," \
    "so the API side of the seam was never started."

command -v curl >/dev/null 2>&1 || {
  echo "REFUSED: curl is not on PATH, and it is the only thing here that asks /health." >&2
  did_not_run "curl is not on PATH, so the API could not have been asked anything."
}

# The API's port is fixed, so a peer already listening on it would make `dotnet run` fail to bind
# and this command answer "the api never answered" — a finding about somebody else's process.
if (exec 3<>/dev/tcp/127.0.0.1/5081) 2>/dev/null; then
  echo "REFUSED: something is already listening on 127.0.0.1:5081, which is the port this" >&2
  echo "         command's API is fixed to. It would fail to bind and be reported as an API that" >&2
  echo "         never answered. Stop it (maran dev teardown) and retry." >&2
  did_not_run "127.0.0.1:5081 is already in use, so this command's own API could not have bound it."
fi

# A database the API can reach. In CI a service container already listens; locally the development
# compose file is the same database `scripts/maran dev` uses.
if ! (exec 3<>/dev/tcp/127.0.0.1/5432) 2>/dev/null; then
  if command -v docker >/dev/null 2>&1; then
    echo "starting the development database"
    docker compose -f "$root/docker/docker-compose.dev.yml" up -d >/dev/null 2>&1
    healthy=0
    for _ in $(seq 60); do
      if [ "$(docker inspect -f '{{.State.Health.Status}}' maran-postgres 2>/dev/null)" = "healthy" ]; then
        healthy=1
        break
      fi
      sleep 1
    done
    # Without this, the loop above simply expired and the run carried on to start an API that could
    # not boot, which arrived 90 seconds later as "the api never answered" — a finding about the API
    # for a database that was never up.
    if [ "$healthy" -ne 1 ]; then
      echo "REFUSED: the maran-postgres container did not become healthy within 60s." >&2
      docker logs --tail 20 maran-postgres >&2 2>/dev/null || true
      did_not_run "there is no healthy database for the API to boot against, so neither process" \
        "was started and the seam was not exercised."
    fi
  else
    # A fact about the machine, not about the contract. This was `fail` — the word FAILED for a
    # workstation with no PostgreSQL and no docker.
    echo "REFUSED: no database on 127.0.0.1:5432 and no docker to start one. The API refuses to" >&2
    echo "         boot without one (rules/architecture.md), so the seam cannot be reached." >&2
    did_not_run "no panel database is reachable and there is no docker to start one."
  fi
fi

echo "building the agent"
(cd "$root/agent" && cargo build -p maran-agent) >"$agent_log" 2>&1 || fail "the agent did not build"

# Before starting one, check the two ways of NOT starting one. This is here rather than in a unit
# test because the thing that went wrong was the binary's behaviour, not the parser's: `--help` was
# swallowed as "no flags at all" and started a REAL root daemon on the production socket path,
# taking it from the agent already serving it. A unit test on the parser would not have caught the
# process that got launched.
echo "checking the agent refuses a command line it does not understand"
if ! "$root/agent/target/debug/maran-agent" --help >/dev/null 2>&1; then
  fail "--help must print usage and exit 0, not start anything"
fi
if "$root/agent/target/debug/maran-agent" --from-a-newer-unit-file >/dev/null 2>&1; then
  fail "an unknown flag must refuse to start, not be ignored"
fi

echo "starting the agent"
# --allow-uid is this user: the peer-cred guard permits exactly one uid, and in production the
# installer passes the panel user's. Same code path, different number.
"$root/agent/target/debug/maran-agent" --socket "$socket" --allow-uid "$(id -u)" \
  >>"$agent_log" 2>&1 &
agent_pid=$!

for _ in $(seq 30); do
  [ -S "$socket" ] && break
  sleep 1
done
[ -S "$socket" ] || fail "the agent never created its socket"

echo "starting the api"
(cd "$root/backend/src/Maran.Host" \
  && ASPNETCORE_ENVIRONMENT=Development \
     ASPNETCORE_URLS="$api_url" \
     Agent__SocketPath="$socket" \
     dotnet run) >"$api_log" 2>&1 &
api_pid=$!

for _ in $(seq 90); do
  curl -fsS --max-time 2 "$api_url/health/live" >/dev/null 2>&1 && break
  sleep 1
done

# "The api never answered" is the one FAILED in this file that can still be about the machine
# rather than the seam, so before claiming it, the two environmental reasons are OBSERVED rather
# than assumed: a database that went away under the API, and an API process that is no longer alive
# (which its log explains). rules/testing.md: a check must be able to observe what it reports on.
health_status=0
health="$(curl -fsS --max-time 5 "$api_url/health" 2>/dev/null)" || health_status=$?
if [ "$health_status" -ne 0 ]; then
  if ! (exec 3<>/dev/tcp/127.0.0.1/5432) 2>/dev/null; then
    echo "REFUSED: the API never answered AND 127.0.0.1:5432 is not accepting connections — the" >&2
    echo "         database went away under it, so this says nothing about the contract." >&2
    did_not_run "the panel database stopped being reachable while the API was starting."
  fi
  fail "the api never answered"
fi

case "$health" in
  *'"agent":"connected"'*)
    echo "$health"
    echo "HANDSHAKE VERDICT: OK — HANDSHAKE-OK: the API reached the agent over the unix socket."
    echo "    UNOBSERVED HERE: every rpc but the health probe. This proves the two processes agree"
    echo "    enough to connect and report each other, not that every command in the contract"
    echo "    round-trips (proto/, rules/proto.md, and maran proto for the contract's own rules)."
    echo "HANDSHAKE-OK"
    ;;
  *)
    fail "the api did not reach the agent: $health"
    ;;
esac
