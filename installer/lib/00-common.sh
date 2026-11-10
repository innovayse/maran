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

# MARAN_OS_UPDATE_HOLD: the packages the OPT-IN unattended OS security updates (step 68, flag
# --os-security-updates, issue #163) never touch: database engines (mariadb*, mysql*, postgresql*),
# the web server (nginx*), the PHP runtimes (php*) and the FTP daemon (vsftpd*).
#
# The single source of the list. Space-separated glob patterns of lowercase letters, digits and
# hyphens with an optional trailing `*`, which are the same words on both families: step 68 writes
# them as dnf's `exclude=` under [base] on the RHEL family and, turned into anchored regular
# expressions, as `Unattended-Upgrade::Package-Blacklist` on the Debian family and, one glob lock each, in
# the locks file only the unattended service reads on the SUSE family. No SUSE spelling is needed: the
# SUSE names (`php8-fpm`, `postgresql17-server`, `nginx`, `mariadb`) all begin with these words. It is a plain
# assignment (this file carries no readonly state) and is never expanded as a glob by the step.
#
# Why these: an unattended restart of any of them drops every customer's connections, and a version
# bump can change a runtime Maran manages. That is an event an operator schedules, not one a timer
# picks at an arbitrary hour. This is a POLICY about when a change is made, not a security control: a
# held package with a known vulnerability stays vulnerable until the operator applies it, and the
# panel keeps listing it as pending. The hold lives only in the unattended run's own configuration,
# so a manual `apt-get upgrade` or `dnf upgrade` still applies it. The patterns are generous on
# purpose (`php*` also matches a package that merely starts with those letters): that errs toward
# holding too much, never toward applying a package that should have been held.
MARAN_OS_UPDATE_HOLD="mariadb* mysql* postgresql* nginx* php* vsftpd*"
