#!/usr/bin/env bash
# A LIBRARY, not a command: `scripts/lib/run-dev.sh` sources this file.
#
# One question, asked of the socket itself: IS ANYTHING LISTENING ON IT — and the single decision
# that follows from the answer, which is whether the file at that path may be removed.
#
# The guard that used to live at the agent stage asked whether THIS instance's container was
# running and unlinked the socket when it was not. That is a question about a container name, and
# the thing it was written to protect is not about container names at all: its own comment says
# "removing the name under a running agent breaks every future connection to it while `-S` keeps
# looking healthy — which is precisely how one running stack could sabotage another." The first
# outside user of this command reproduced exactly that. A second agent container, from an earlier
# manual pass, bind-mounted the same `.dev-agent` directory and held the socket; the guard saw
# `maran-agent-dev` not running, answered "stale", and unlinked a socket that belonged to
# somebody else. Nothing was connected at that moment, which is luck, not design.
#
# So the question is answerable, cheaply and exactly, by the same operation the panel itself
# performs — an AF_UNIX `connect()`. A pathname socket is addressed through the filesystem and not
# through a network namespace, which is why a host process (the panel) reaches a listener inside a
# container at all; the same reach makes the host's connect the honest oracle here. `ss -lx` is
# not: it reads `/proc/net/unix` for the caller's own network namespace, so every socket bound
# inside a container is invisible to it — the very holders this guard exists to see.
#
# What lives here:
#
#   dev_socket_probe          one word about a path: live, stale, absent or unknown:<errno>
#   dev_socket_mount_holders  the running containers that CAN reach a path, for naming a holder
#   dev_socket_reconcile      removes a socket only when proved free; refuses, named, otherwise
#
# Caller's contract: `dev_socket_reconcile` reads `$agent_container` — this instance's agent
# container name — to tell OUR listener from a foreign one. It is the only ambient value any
# function here reads, and it is what makes "this is ours, leave it" distinguishable from
# "somebody else is listening, refuse".

# dev_socket_probe: one word about a unix socket path — `live` (a listener accepted a connection),
# `stale` (the connection was REFUSED, so the file outlives whatever bound it), `absent` (no such
# file) or `unknown:<reason>` (the connection failed for a reason that is not evidence of
# emptiness, `EACCES` above all: a socket we may not open is not a socket nobody holds).
# python3 is preferred because it names the errno; nc is the fallback and its message is parsed;
# with neither, the caller is told it has no probe and falls back to the weaker mount fact.
#
# The probe connects and closes at once, so a live agent sees one connection that hangs up
# immediately — the same thing any aborted client does, and it happens once per bring-up.
dev_socket_probe() {
  local path="$1" error
  if command -v python3 >/dev/null 2>&1; then
    python3 - "$path" <<'PROBE'
import errno
import socket
import sys

probe = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
probe.settimeout(2)
try:
    probe.connect(sys.argv[1])
    print("live")
except ConnectionRefusedError:
    # The definitive emptiness answer for AF_UNIX: the file exists, the kernel found no listener
    # behind it. Only a socket whose owner is gone answers this way.
    print("stale")
except FileNotFoundError:
    print("absent")
except (BlockingIOError, TimeoutError):
    # A connect that would BLOCK is a connect that found a listener — for AF_UNIX this is a full
    # listen backlog, which cannot exist without something listening. Measured, and it is not a
    # corner case: the first version of this probe called it `unknown`, and a busy agent would
    # therefore have had its own socket reported as unprovable and its own bring-up refused.
    print("live")
except OSError as failure:
    print("unknown:%s" % errno.errorcode.get(failure.errno, failure.errno))
finally:
    probe.close()
PROBE
    return 0
  fi
  if command -v nc >/dev/null 2>&1; then
    if error="$(nc -U -z -w 2 "$path" 2>&1 >/dev/null)"; then
      echo "live"
      return 0
    fi
    case "$error" in
      *"Connection refused"*) echo "stale" ;;
      *"No such file"*)       echo "absent" ;;
      *)                      echo "unknown:${error##*: }" ;;
    esac
    return 0
  fi
  echo "unknown:no-probe-tool"
}

# dev_socket_mount_holders: the names of RUNNING containers that bind-mount a directory containing
# this path — the containers that CAN reach it. This is deliberately not the staleness answer:
# a mount is capability, not possession, and the self-check's socket lives under the dev
# instance's directory, so an ancestor mount would condemn a path its holder never touched. It
# is used to NAME a holder in a refusal, and as the weaker fallback when no probe tool exists.
dev_socket_mount_holders() {
  local path="$1" id name source
  for id in $(docker ps -q 2>/dev/null); do
    name="$(docker inspect -f '{{.Name}}' "$id" 2>/dev/null | sed 's#^/##')"
    [ -n "$name" ] || continue
    while read -r source; do
      [ -n "$source" ] || continue
      case "$path" in
        "$source"|"$source"/*) echo "$name"; break ;;
      esac
    done < <(docker inspect -f '{{range .Mounts}}{{.Source}}{{"\n"}}{{end}}' "$id" 2>/dev/null)
  done
}

# dev_socket_reconcile: remove the socket at a path when — and only when — it is PROVED that
# nothing holds it; return 1 with a named reason when it is held, or when the answer cannot be
# established. Returning rather than exiting is what lets the self-check exercise this exact
# function on a fixture instead of re-implementing its reasoning somewhere the real bring-up
# never runs.
#
# UNOBSERVED HERE: this is not the only thing that clears the path, and saying otherwise would
# overstate it. The container's entrypoint runs `rm -f "$socket"` as root and the agent unlinks a
# leftover before `bind` (agent/crates/agent/src/server.rs). So a foreign holder is not saved by
# this guard alone — it is saved by the REFUSAL this guard returns, which stops the bring-up
# before a container that would clobber the path is ever started.
dev_socket_reconcile() {
  local path="$1" verdict holders ours="no"
  if [ ! -e "$path" ]; then
    return 0
  fi
  if [ ! -S "$path" ]; then
    echo "     socket   $path exists and is not a socket — removing it; nothing can be listening"
    rm -f "$path"
    return 0
  fi
  holders="$(dev_socket_mount_holders "$path" | sort -u | tr '\n' ' ')"
  holders="${holders%" "}"
  if [ "$(docker inspect -f '{{.State.Running}}' "$agent_container" 2>/dev/null)" = "true" ]; then
    case " $holders " in *" $agent_container "*) ours="yes" ;; esac
  fi
  verdict="$(dev_socket_probe "$path")"
  case "$verdict" in
    absent)
      return 0
      ;;
    stale)
      echo "     socket   connect() to $path was REFUSED — nothing is listening, so the file is a"
      echo "              leftover and is removed${holders:+ (mounted by $holders, none of it listening)}"
      rm -f "$path"
      return 0
      ;;
    live)
      if [ "$ours" = "yes" ]; then
        echo "     socket   $agent_container is running and answering on $path — left in place"
        return 0
      fi
      echo "REFUSED — something is LISTENING on $path and it is not this instance's agent." >&2
      if [ -n "$holders" ]; then
        echo "    running container(s) mounting that directory: $holders" >&2
      else
        echo "    no running container mounts that directory, so the listener is a process on" >&2
        echo "    this host; 'lsof $path' or 'fuser $path' will name it." >&2
      fi
      echo "    $(stat -c '%A %U:%G' "$path") $path" >&2
      echo "Unlinking it would break every future connection to a live agent while the file kept" >&2
      echo "looking healthy to '-S'. Stop the holder (or 'maran dev --stop') and run this again." >&2
      return 1
      ;;
    unknown:no-probe-tool)
      # Degraded, and it says so rather than wedging the machine: with neither python3 nor nc
      # there is no way to ask the socket anything, so the weaker mount fact decides. It is
      # weaker in the safe direction — it can refuse a path nobody holds, never clear one
      # somebody does.
      if [ -n "$holders" ] && [ "$ours" != "yes" ]; then
        echo "REFUSED — $path may be held by running container(s): $holders." >&2
        echo "    UNOBSERVED HERE: neither python3 nor nc is installed, so nothing could ask the" >&2
        echo "    socket whether anyone is listening; the mount is all this machine can see." >&2
        return 1
      fi
      echo "     socket   removing $path on the mount fact alone"
      echo "              UNOBSERVED HERE: no python3 and no nc, so 'is anything listening' was"
      echo "              never asked — only 'does a running container mount that directory'"
      rm -f "$path"
      return 0
      ;;
    *)
      echo "REFUSED — $path could not be shown to be free: connect() failed with ${verdict#unknown:}." >&2
      echo "    $(stat -c '%A %U:%G' "$path") $path; this panel runs as uid $(id -u), group $(id -gn)." >&2
      [ -n "$holders" ] && echo "    running container(s) mounting that directory: $holders" >&2
      echo "A socket this run may not open is not a socket nobody holds, and the difference is the" >&2
      echo "whole defect: remove it and a live agent keeps a name that answers nothing. Stop the" >&2
      echo "holder, or remove the file by hand once you know what it belongs to." >&2
      return 1
      ;;
  esac
}
