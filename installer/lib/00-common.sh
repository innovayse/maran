#!/usr/bin/env bash
# 00-common.sh: helpers more than one step needs, and nothing else.
#
# It exists because of a defect this file is the fix for. `host_display_name` was defined in
# install.sh, which is where the installer's other shared helpers live — and that was wrong for a
# reason install.sh cannot see: the step files are ALSO consumed on their own.
# docker/polygon/asserts/assert-installer-steps.sh sources a step and calls its functions with no
# install.sh anywhere, so a helper defined there is missing exactly where the steps are exercised.
# 90-finish.sh died with `host_display_name: command not found`, exit 127, in the polygon image
# build, while every real install was fine — which is why the install polygon did not catch it
# either: it runs the real install.sh.
#
# Why not an existing step. 20-dependencies.sh is where `pkg_install` lives and is the precedent for
# "a helper in the step that owns its subject", but no step owns "what is this machine called".
# 10-preflight.sh would be the natural reader of that fact, and it is the one step the polygon
# deliberately does NOT source — it defines its own `fail`, which would replace the assert script's.
# So this file carries no `fail`, no readonly state and no step function: sourcing it can collide
# with nothing.
#
# Sourced by install.sh before any step runs, and by the polygon's assert script. A helper added here
# must stay free of both: no `fail`, and no name a step file already uses.
# host_display_name: this machine's name, for the self-signed certificate's CN and for the URL the
# installer prints at the end. Never empty, and never dependent on the `hostname` BINARY.
#
# Both callers used to spell `hostname -f 2>/dev/null || hostname`, which assumes a program that is
# not on every host. Oracle Linux 8's base image has no `hostname` package at all, so BOTH halves of
# that expression failed and the install died mid-step under `set -e`:
#
#     80-nginx.sh: line 209: hostname: command not found        (exit 127)
#
# The fallbacks are ordered by how much they know, and every one after the first needs no package
# this installer has not already required:
#
#   hostname -f        the fully-qualified name, when the program exists
#   hostnamectl        systemd's own answer — and systemd is a hard requirement of this product
#   /etc/hostname      the file systemd itself reads
#   uname -n           coreutils, which cannot be absent from a host that boots
#   localhost          so a certificate is still generated rather than one with an empty CN
#
# The last line matters more than it looks: an empty CN makes `openssl req` produce a certificate no
# browser will accept, which would turn a missing package into a panel that cannot be reached.
host_display_name() {
  local name=""
  if command -v hostname >/dev/null 2>&1; then
    name="$(hostname -f 2>/dev/null || hostname 2>/dev/null || true)"
  fi
  [ -n "$name" ] || name="$(hostnamectl --static 2>/dev/null || true)"
  [ -n "$name" ] || name="$(cat /etc/hostname 2>/dev/null || true)"
  [ -n "$name" ] || name="$(uname -n 2>/dev/null || true)"
  name="$(printf '%s' "$name" | tr -d '[:space:]')"
  [ -n "$name" ] || name="localhost"
  printf '%s\n' "$name"
}
