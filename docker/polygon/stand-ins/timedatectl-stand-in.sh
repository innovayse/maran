#!/bin/sh
# A stand-in for systemd's time client, installed at /usr/bin/timedatectl in the
# polygon images ONLY. Production never uses Docker (spec §2) and never sees this
# file.
#
# Why it exists: a container boots no init system, so the real `timedatectl`
# fails at once with "System has not been booted with systemd" and cannot say
# anything about the clock. The agent reads the clock through exactly one call,
#
#     timedatectl show --property=NTP --property=NTPSynchronized
#
# (ops::monitor::get_clock_sync, issue #150), so without a stand-in the dev stack
# (`maran dev`) answered "The clock could not be checked" on the system status
# page for ever, and every polygon test of that reading met a tool that cannot
# answer (issue #392).
#
# What it does, and deliberately no more:
#
#   show --property=<P> ...   prints `<P>=<value>` for each requested property of
#                             NTP and NTPSynchronized, in the order asked, taken
#                             from the recorded clock state below.
#   anything else             is refused with exit 1. Unlike the systemctl
#                             stand-in this one does NOT succeed silently: the
#                             agent asks nothing else, and an unknown question
#                             answered with silence would be a stand-in
#                             pretending.
#
# Clock state, and what it is honestly worth
# ------------------------------------------
# The file /run/polygon-clock/state holds one of the three states the agent
# distinguishes: `synchronized`, `notSynchronized` or `syncServiceDisabled`. A
# host with no file reads `synchronized` — the state a real host is in once the
# installer has run, which a container's missing init would otherwise contradict.
# A test that needs the agent to say no writes another word there from OUTSIDE the
# agent, the way the systemctl stand-in's unit state is changed.
#
#   state                NTP   NTPSynchronized
#   synchronized         yes   yes
#   notSynchronized      yes   no
#   syncServiceDisabled  no    no
#
# The consequence, stated rather than hidden: **this reports the file, not the
# kernel.** The container shares its host's kernel clock, whose real
# synchronisation flag says nothing about the server this image models. What the
# polygon settles is the agent's classification and that the process it spawns
# is at the path the adapter names; that `timedatectl` really prints these two
# properties as `yes` or `no` is held by the committed capture in
# ops::monitor's unit tests, and a drifting kernel clock is observable on no
# image.
set -eu

STATE_FILE="/run/polygon-clock/state"

state="synchronized"
if [ -r "$STATE_FILE" ]; then
  state="$(cat "$STATE_FILE")"
fi

case "$state" in
  synchronized) ntp=yes; synchronized=yes ;;
  notSynchronized) ntp=yes; synchronized=no ;;
  syncServiceDisabled) ntp=no; synchronized=no ;;
  *)
    echo "timedatectl stand-in: unknown clock state '$state' in $STATE_FILE" >&2
    exit 1
    ;;
esac

if [ "${1:-}" != "show" ]; then
  echo "timedatectl stand-in: only 'show --property=NTP|NTPSynchronized' is supported" >&2
  exit 1
fi
shift

for argument in "$@"; do
  case "$argument" in
    --property=NTP) echo "NTP=$ntp" ;;
    --property=NTPSynchronized) echo "NTPSynchronized=$synchronized" ;;
    *)
      echo "timedatectl stand-in: unsupported argument '$argument'" >&2
      exit 1
      ;;
  esac
done
