#!/usr/bin/env bash
# 87-firewall.sh: rendering through the agent, firewalld handover, include wiring
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The split is by SUBJECT and preserves the original order exactly, because these definitions
# are order-dependent in one respect that matters — a `readonly` set twice is a fatal error, so
# each constant lives beside the only assertions that use it.


# assert_firewall_renders_through_the_agent: the installer seeds its ruleset by RUNNING the
# agent, and writes no nftables syntax of its own.
#
# One template source or two is the whole question. The agent renders the same templates at
# every later apply, so a copy of that text in shell would be a second source, and the first
# divergence between them is a firewall that changes shape the moment an administrator
# touches a rule. Asserted rather than trusted because the shortcut is so easy to take: a
# heredoc in a step file looks like the simplest thing in the world.
#
# The tokens below are the load-bearing syntax of a ruleset — a policy, a hook, a port
# match, the loopback exemption. A step that hand-rolled one would need them; a step that
# renders through the agent has no use for any of them.
assert_firewall_renders_through_the_agent() {
  # CODE, not prose, and that applies to the POSITIVE checks below as much as to the negative
  # ones. The step's doc comments name every token this function looks for — both subcommands
  # at :19, the plural flag the agent refuses, the policy the seed renders — so a check reading
  # the whole file is satisfied by the comment that warns about the defect. Measured twice: the
  # negative checks were corrected for it first, and the three positive ones below still passed
  # against a copy of 87-firewall.sh with EVERY code line stripped and only its comments left.
  # A check that cannot fail for the reason it names is worse than no check, because it is
  # counted.
  #
  # Full-line comments are dropped; a token on a code line, even after a trailing `#`, still
  # counts.
  local code
  code="$(grep -v '^[[:space:]]*#' "$FIREWALL_STEP" || true)"

  # The subcommand name, ending where it ends. A plain substring match was satisfied by
  # `render-firewall-bans-typo`, which is a subcommand the agent refuses — measured, that
  # mutation walked straight through this check.
  printf '%s\n' "$code" | grep -qE 'render-firewall-bans([^A-Za-z0-9_-]|$)' \
    || fail "87-firewall.sh no longer invokes 'render-firewall-bans'; the bans table must be seeded before
any include names it, or the next boot loads no firewall at all."
  printf '%s\n' "$code" | grep -qE 'render-firewall-ruleset([^A-Za-z0-9_-]|$)' \
    || fail "87-firewall.sh no longer invokes 'render-firewall-ruleset'"

  printf '%s\n' "$code" | grep -q -- '--ssh-port' \
    || fail "87-firewall.sh no longer passes --ssh-port to the agent"

  printf '%s\n' "$code" | grep -q -- '--ssh-ports' \
    && fail "87-firewall.sh passes --ssh-ports, which the agent refuses outright: the flag is --ssh-port,
singular and repeatable, once per port sshd listens on."

  local token
  for token in 'policy drop' 'hook input' 'dport' 'iif "lo"'; do
    if printf '%s\n' "$code" | grep -qF -- "$token"; then
      fail "87-firewall.sh contains nftables ruleset syntax of its own (found: ${token}). The seed must be
rendered by the agent so that one set of templates produces both the seed and every later apply."
    fi
  done

  echo "87-firewall.sh seeds both files through the agent and writes no ruleset text of its own."
}

# assert_firewall_seeding_composes: what the STEP does with a Firewall__SshPorts value —
# which argv reaches the agent, whether the files are written, and whether a bad list stops
# the install.
#
# It asserts on the caller, and that is the correction of a test that could not fail. The
# previous version ran `ssh_port_flags` in an explicit subshell, where its `exit` was
# observable, and never ran the function that consumes it. Meanwhile the real caller read it
# through a process substitution, where the same `exit` killed only the subshell: a list of
# `22,abc,2222` produced `--ssh-port 22 --panel-port 8443`, wrote the file, and returned 0.
# The assertion passed while the production path silently closed two live SSH ports.
#
# The agent is a stand-in here — the image has no agent binary at build time — and it is the
# right stand-in for this question: what is under test is the argv the installer BUILDS, not
# the ruleset the agent renders, which is golden-tested in its own crate. It records what it
# was called with and prints a table that satisfies the step's own checks.
assert_firewall_seeding_composes() {
  local agent="/usr/local/maran/agent/maran-agent"
  local panel_env="/etc/maran/panel.env"
  local argv_log="/tmp/maran-fake-agent-argv"

  # This assertion writes files at the paths the step really uses. On a host that already has
  # them it would destroy the real thing, so it refuses rather than assuming it is in a
  # throwaway image.
  local existing
  for existing in "$agent" "$panel_env" /etc/maran/firewall.nft /etc/maran/firewall-bans.nft; do
    [ -e "$existing" ] \
      && fail "${existing} already exists. This assertion writes and deletes that exact path, so it
refuses to run rather than destroy a real installation's file."
  done

  install -d -m 0755 "$(dirname "$agent")"
  cat > "$agent" <<'FAKE'
#!/usr/bin/env bash
printf '%s\n' "$*" >> /tmp/maran-fake-agent-argv
table=maran
priority=0
policy=accept
case "$1" in
  render-firewall-bans) table=maran_bans; priority=-5 ;;
esac
[ -n "${MARAN_FAKE_AGENT_BROKEN:-}" ] && policy=drpo
printf 'table inet %s {\n' "$table"
printf '    chain input {\n'
printf '        type filter hook input priority %s; policy %s;\n' "$priority" "$policy"
printf '    }\n}\n'
FAKE
  chmod 0755 "$agent"
  install -d -m 0750 /etc/maran

  # The good case: three ports, one flag each, in order, and both files written.
  : > "$argv_log"
  printf 'Firewall__SshPorts=22,2200,2222\nFirewall__PanelPort=8443\n' > "$panel_env"
  local step_output="/tmp/maran-step-output"
  run_installer_step 'seed_firewall_files' >"$step_output" 2>&1 \
    || fail "seed_firewall_files refused an ordinary three-port host:
$(cat "$step_output")"

  local rendered_argv expected_argv
  rendered_argv="$(grep '^render-firewall-ruleset' "$argv_log" || true)"
  expected_argv="render-firewall-ruleset --ssh-port 22 --ssh-port 2200"
  expected_argv="${expected_argv} --ssh-port 2222 --panel-port 8443"
  [ "$rendered_argv" = "$expected_argv" ] \
    || fail "the step invoked the agent as:
  ${rendered_argv}
and it must have been:
  ${expected_argv}
A ruleset seeded from a shorter list opens the ports it names and drops the rest, under a policy with no
remote recovery."
  grep -q '^render-firewall-bans$' "$argv_log" \
    || fail "the step did not render the bans table; an include naming a missing file aborts the whole load"
  [ -s /etc/maran/firewall.nft ] && [ -s /etc/maran/firewall-bans.nft ] \
    || fail "the step did not write both rendered files"

  # The bad cases, asserted on the STEP: it must abort, and it must not have handed the agent
  # a truncated list on the way.
  local bad
  for bad in "22,abc,2222" "22,,2200" "22," ",22" "22,70000" ""; do
    : > "$argv_log"
    rm -f /etc/maran/firewall.nft
    printf 'Firewall__SshPorts=%s\nFirewall__PanelPort=8443\n' "$bad" > "$panel_env"
    if run_installer_step 'seed_firewall_files' >"$step_output" 2>&1; then
      fail "seed_firewall_files ACCEPTED Firewall__SshPorts='${bad}' and invoked the agent as:
  $(grep '^render-firewall-ruleset' "$argv_log" || echo '(nothing)')
Every port that list failed to name is a port the seeded policy drops, and one of them is how the
operator is connected to the machine."
    fi
    [ ! -e /etc/maran/firewall.nft ] \
      || fail "seed_firewall_files refused Firewall__SshPorts='${bad}' but wrote the ruleset anyway"
    grep -q '^render-firewall-ruleset' "$argv_log" \
      && fail "seed_firewall_files refused Firewall__SshPorts='${bad}' only AFTER invoking the agent with
  $(grep '^render-firewall-ruleset' "$argv_log")"
  done

  # And a rendered file that does not parse must not become the file a boot depends on. The
  # agent is trusted to render a ruleset, not assumed to: an unparseable file left at the live
  # path with the include already wired is nftables.service FAILED at the next boot even though
  # the install aborted.
  : > "$argv_log"
  rm -f /etc/maran/firewall.nft /etc/maran/firewall-bans.nft
  printf 'Firewall__SshPorts=22\nFirewall__PanelPort=8443\n' > "$panel_env"
  if run_installer_step 'MARAN_FAKE_AGENT_BROKEN=1 seed_firewall_files' >"$step_output" 2>&1; then
    fail "seed_firewall_files accepted a rendered ruleset that does not parse"
  fi
  # The TEXT, not only the status. This refusal and the two below have each survived a mutant
  # once by refusing for the wrong reason, which is exactly where the message earns its keep.
  grep -q 'does not parse' "$step_output" \
    || fail "seed_firewall_files refused an unparseable rendering, but not for that reason:
$(cat "$step_output")"
  [ ! -e /etc/maran/firewall-bans.nft ] && [ ! -e /etc/maran/firewall.nft ] \
    || fail "seed_firewall_files refused an unparseable rendering but left the file at the live path,
where the next boot would read it"

  rm -f "$agent" "$panel_env" "$argv_log" /etc/maran/firewall.nft /etc/maran/firewall-bans.nft
  echo "The step builds one --ssh-port per port, refuses a list or a rendering it cannot read, and stops"
  echo "before anything reaches the live path."
}

# firewalld_state: put this container's stand-in systemctl into one of the four host states the
# installer's firewalld handling has to tell apart, and only those.
#
# The state directory is wiped first, so each case starts from "this host has no firewalld" and
# adds exactly what its name says. `firewalld` and `firewalld.service` are written BOTH ways on
# purpose: a real systemd treats them as one unit, the stand-in keeps one state file per literal
# name (its own header says so), and `disable_firewalld` asks `list-unit-files` with the suffix
# and `is-enabled`/`disable` without it. Writing one spelling would make a case pass because the
# fixture was half-applied rather than because the code was right.
firewalld_state() {
  local wanted="$1"
  rm -rf "$UNIT_STATE_DIRECTORY"
  mkdir -p "$UNIT_STATE_DIRECTORY"
  case "$wanted" in
    absent) ;;
    query-broken)
      printf 'Connection reset by peer\n' > "${UNIT_STATE_DIRECTORY}/firewalld.service.query-broken"
      ;;
    present | disable-refused)
      : > "${UNIT_STATE_DIRECTORY}/firewalld.service.installed"
      printf 'enabled\n' > "${UNIT_STATE_DIRECTORY}/firewalld.service.enabled"
      printf 'enabled\n' > "${UNIT_STATE_DIRECTORY}/firewalld.enabled"
      printf 'active\n' > "${UNIT_STATE_DIRECTORY}/firewalld"
      if [ "$wanted" = disable-refused ]; then
        printf 'Unit firewalld.service is masked.\n' > "${UNIT_STATE_DIRECTORY}/firewalld.refuse-disable"
      fi
      ;;
    *) fail "firewalld_state: '${wanted}' is not one of the four states this assertion models" ;;
  esac
}

# firewalld_disable_was_attempted: 0 when the stand-in was actually asked to disable firewalld.
#
# Asked of the STATE the verb writes, not of a log: `disable` records the unit's enablement, and
# a unit nothing disabled has no such file. That is what separates "the step decided there was
# nothing to disable" from "the step tried and the host refused" — two outcomes an earlier
# version printed the same sentence for.
firewalld_disable_was_attempted() {
  [ -e "${UNIT_STATE_DIRECTORY}/firewalld.enabled" ]
}

# assert_firewalld_handling_tells_its_three_answers_apart: `disable_firewalld` against all four
# host states, because until this existed it was exercised by NOTHING.
#
# Neither polygon image installs firewalld and the stand-in had no `list-unit-files` arm at all,
# so every build took its catch-all `*) exit 0` — an empty answer, which the step read as "no
# firewalld here". Every other path through the function, including both of the ones that leave
# a host with firewalld still in charge of the ruleset, was unreachable from this repository.
# That is why a firewalld failure reported from a real host could not be reproduced: there was
# no coverage of it to reproduce it with.
#
# The two states that matter are the middle two, and they are the ones a mutation discriminates:
# restoring the historical `2>/dev/null || true` on the query makes the BROKEN case say "No
# firewalld unit on this host" at rc 0, and restoring `|| true` on the disable makes the REFUSED
# case finish at rc 0 with firewalld still enabled. Measured, both. The ABSENT and PRESENT cases
# behave identically under that mutation, which is exactly why an assertion resting on them
# alone would have been a green light for the defect.
assert_firewalld_handling_tells_its_three_answers_apart() {
  local output status=0

  # 1. No firewalld, and systemctl said so. The one answer that may proceed quietly — and it
  #    must proceed without touching a unit it has just been told is not there.
  firewalld_state absent
  output="$(run_installer_step 'disable_firewalld' 2>&1)" || status=$?
  [ "$status" -eq 0 ] \
    || fail "disable_firewalld failed on a host that simply has no firewalld (exit ${status}):
${output}"
  case "$output" in
    *"No firewalld unit on this host"*) ;;
    *) fail "disable_firewalld said nothing about a host with no firewalld. Every path through it must
reach the install log, or an operator whose firewall rules stopped applying cannot find out why:
${output}" ;;
  esac
  if firewalld_disable_was_attempted; then
    fail "disable_firewalld reported no firewalld unit and then disabled one anyway"
  fi

  # 2. The query BROKE. Not the same answer as 'this host has no firewalld', and the difference
  #    is a host that finishes the install with firewalld still rewriting the ruleset under a
  #    panel that reports its own rules as live.
  status=0
  firewalld_state query-broken
  output="$(run_installer_step 'disable_firewalld' 2>&1)" || status=$?
  case "$output" in
    *"No firewalld unit on this host"*)
      fail "disable_firewalld reported 'No firewalld unit on this host' for a query that FAILED. That is a
statement of fact the code did not establish, and the diagnosis that contradicts it was discarded:
${output}" ;;
  esac
  case "$output" in
    *"could not be answered here"*) ;;
    *) fail "disable_firewalld did not report that the firewalld query failed:
${output}" ;;
  esac
  case "$output" in
    *"Connection reset by peer"*) ;;
    *) fail "disable_firewalld reported a failed query without saying what systemctl said. The diagnosis is
the only thing that tells an operator whether this host has firewalld:
${output}" ;;
  esac
  firewalld_disable_was_attempted \
    || fail "disable_firewalld could not find out whether firewalld is here and then skipped the disable.
'I could not find out' is a reason to act and check, not a reason to do nothing."

  # 3. firewalld is here and the disable works: announced, and gone.
  status=0
  firewalld_state present
  output="$(run_installer_step 'disable_firewalld' 2>&1)" || status=$?
  [ "$status" -eq 0 ] \
    || fail "disable_firewalld failed against a firewalld it disabled successfully (exit ${status}):
${output}"
  case "$output" in
    *"Disabling firewalld"*) ;;
    *) fail "disable_firewalld disabled firewalld without saying so. An operator whose firewalld rules just
stopped applying must be able to find out why from the install log:
${output}" ;;
  esac
  [ "$(systemctl is-enabled firewalld 2>/dev/null || true)" = disabled ] \
    || fail "disable_firewalld returned success but firewalld is still enabled"

  # 4. firewalld is here and the disable is REFUSED. The install must stop: firewalld flushes
  #    and rewrites the ruleset on its own reloads, so finishing here means shipping a host
  #    whose panel reports rules that something else is about to erase.
  status=0
  firewalld_state disable-refused
  output="$(run_installer_step 'disable_firewalld' 2>&1)" || status=$?
  [ "$status" -ne 0 ] \
    || fail "disable_firewalld ACCEPTED a host whose 'systemctl disable --now firewalld' was refused, and
firewalld is still enabled and active. The install would finish reporting 'Firewall active' while
firewalld erases the panel's table at its next reload:
${output}"
  case "$output" in
    *"firewalld is still there"*) ;;
    *) fail "disable_firewalld refused the host whose disable failed, but not for that reason:
${output}" ;;
  esac
  case "$output" in
    *"Unit firewalld.service is masked."*) ;;
    *) fail "disable_firewalld refused without repeating what systemctl said, so the operator cannot see
why the disable failed:
${output}" ;;
  esac

  rm -rf "$UNIT_STATE_DIRECTORY"
  echo "disable_firewalld tells an absent firewalld, an unanswerable query, a working disable and a"
  echo "refused one apart, and says which of the four it met."
}

# assert_firewall_keeps_firewalld_until_its_own_table_is_loaded: the ORDER of step_firewall,
# asserted by the state an aborted install leaves the host in.
#
# The step used to disable and stop firewalld before a single ruleset byte existed, and every
# line after that can still abort: a render that fails or prints nothing or prints a non-table,
# a rendering nft rejects, an unusable Firewall__SshPorts, a damaged marker pair in the include
# target, a candidate that does not parse, a failing `systemctl enable --now`, the closing kernel
# gate. Any one of them left a RHEL host with its working firewall stopped, nothing in its
# place, and the log's last word on the subject a present-tense promise that Maran was now
# managing the firewall.
#
# So the assertion is about the aborted run, not the successful one: this container cannot reach
# the end of the step (the closing gate asks the kernel for `table inet maran` and nothing loads
# it here), and the aborted run is the case that matters anyway. The agent is a stand-in that
# exits non-zero — the shape of a real half-unpacked or wrong-architecture binary — so the abort
# happens at the first render, which is the earliest point after which the old order had already
# taken the firewall away.
#
# Mutation-confirmed: with `disable_firewalld` moved back in front of `seed_firewall_files`, this
# same run leaves `is-enabled` at `disabled` and `is-active` at `inactive`.
assert_firewall_keeps_firewalld_until_its_own_table_is_loaded() {
  local agent="/usr/local/maran/agent/maran-agent"
  local panel_env="/etc/maran/panel.env"
  local existing
  for existing in "$agent" "$panel_env" /etc/maran/firewall.nft /etc/maran/firewall-bans.nft; do
    [ -e "$existing" ] \
      && fail "${existing} already exists. This assertion writes and deletes that exact path, so it
refuses to run rather than destroy a real installation's file."
  done

  install -d -m 0755 "$(dirname "$agent")"
  printf '#!/bin/sh\nexit 9\n' > "$agent"
  chmod 0755 "$agent"
  install -d -m 0750 /etc/maran
  printf 'Firewall__SshPorts=22\nFirewall__PanelPort=8443\n' > "$panel_env"
  firewalld_state present

  local output status=0
  # `pkg_install` lives in 20-dependencies.sh, which this image does not carry and must not run:
  # the packages are already installed and an assertion has no business invoking a package
  # manager. Everything else in the step is the real thing.
  output="$(run_installer_step 'pkg_install() { :; }
step_firewall' 2>&1)" || status=$?

  local enabled active
  enabled="$(systemctl is-enabled firewalld 2>/dev/null || true)"
  active="$(systemctl is-active firewalld 2>/dev/null || true)"
  rm -f "$agent" "$panel_env"
  rm -rf "$UNIT_STATE_DIRECTORY"

  [ "$status" -ne 0 ] \
    || fail "step_firewall reported success with an agent binary that exits 9. Nothing was rendered, so
there is no ruleset for the include block to name:
${output}"
  case "$output" in
    *"was not touched"*) ;;
    *) fail "step_firewall aborted, but not at the render it was made to abort at. This assertion is then
measuring the wrong moment:
${output}" ;;
  esac
  [ "$enabled" = enabled ] && [ "$active" = active ] \
    || fail "step_firewall aborted and left firewalld disabled (is-enabled=${enabled}, is-active=${active}).
The host's working firewall is gone, nothing replaced it, and the install failed — which is strictly
worse than the two firewalls overlapping for the length of three lines. firewalld must be taken away
only after the kernel has confirmed the table that replaces it:
${output}"

  echo "An aborted step_firewall leaves firewalld exactly as it found it: enabled, active, and in charge."
}

# assert_firewall_marker_records_only_our_own_enabling: the marker that decides whether the
# UNINSTALLER may take a firewall away, on both of its branches.
#
# Both branches, because the interesting half is the one that must NOT happen: a host whose
# firewall was enabled before Maran arrived must keep it after Maran leaves. Neither branch had
# any coverage — the polygon's systemctl stand-in answers `is-enabled` from its catch-all, so
# every run took the same path and nothing would have noticed the other one break.
assert_firewall_marker_records_only_our_own_enabling() {
  local marker="/etc/maran/firewall-service-enabled-by-maran"
  [ -e "$marker" ] \
    && fail "${marker} already exists; this assertion creates and deletes exactly that path"
  install -d -m 0750 /etc/maran

  # A unit that is NOT enabled: we are about to enable it, so the uninstaller may disable it.
  run_installer_step 'systemctl() { [ "$1" = "is-enabled" ] && return 1; return 0; }
record_firewall_service_enablement' \
    || fail "record_firewall_service_enablement failed for a service that was not enabled"
  [ -f "$marker" ] \
    || fail "no marker was written for a firewall service this installer had to enable. The uninstaller
would then leave nftables enabled on a host that had no firewall before Maran."
  rm -f "$marker"

  # A unit that is ALREADY enabled: not ours, and the uninstaller must not take it away.
  run_installer_step 'systemctl() { return 0; }
record_firewall_service_enablement' \
    || fail "record_firewall_service_enablement failed for a service that was already enabled"
  if [ ! -e "$marker" ]; then
    echo "The service marker is written when this installer enables the firewall, and not when it did not."
    return 0
  fi

  rm -f "$marker"
  fail "a marker was written for a service that was ALREADY enabled. The uninstaller would then disable a
firewall this installer never turned on."
}

# assert_firewall_include_wiring: the include block, wired by the installer's own function,
# with real files behind the includes and real nft reading them.
#
# Written this way because of what this repository keeps learning: a check that greps for an
# include line proves the line is spelled right and nothing else. So the payload files exist
# at the paths the block names, nft is made to resolve them, and the failure the whole step
# is ordered around — an include whose target is missing — is produced on purpose and the
# installer's own guard is required to refuse it.
#
# What it deliberately does NOT do is load the ruleset into the kernel. This script runs at
# image BUILD time, where there is no CAP_NET_ADMIN: measured, `nft -c -f` on a perfectly
# valid file fails there with "cache initialization failed", while a missing include still
# fails with "File not found" because the parser reaches it first. So the include RESOLUTION
# is checkable here and the kernel load is not; the load is the agent's own privileged
# polygon suite's job, and a skip disguised as a pass would be worse than either.
#
# The payloads are written here rather than rendered by the agent — the image has no agent
# binary at build time — so what this proves is the WIRING: one block after two runs, bans
# included before rules, every include resolving, and a missing one refused.
assert_firewall_include_wiring() {
  local scratch target wire_output existing
  # It writes and deletes /etc/maran/firewall*.nft, so like its sibling it refuses to run on a
  # host that already has them rather than destroying a real installation's files.
  for existing in /etc/maran/firewall.nft /etc/maran/firewall-bans.nft; do
    [ -e "$existing" ] \
      && fail "${existing} already exists. This assertion writes and deletes that exact path, so it
refuses to run rather than destroy a real installation's file."
  done
  wire_output="/tmp/maran-wire-output"
  scratch="$(mktemp -d)"
  target="${scratch}/nftables.conf"
  printf '#!/usr/sbin/nft -f\nflush ruleset\n' > "$target"

  # At the paths the block names, which are the step's own constants — not rewritten copies,
  # so the assertion exercises the real ones.
  install -d -m 0750 /etc/maran
  cat > /etc/maran/firewall-bans.nft <<'PAYLOAD'
table inet maran_bans {
    chain input {
        type filter hook input priority -5; policy accept;
    }
}
PAYLOAD
  cat > /etc/maran/firewall.nft <<'PAYLOAD'
table inet maran {
    chain input {
        type filter hook input priority 0; policy accept;
    }
}
PAYLOAD

  run_installer_step "wire_firewall_includes \"$target\"" >/dev/null \
    || fail "wire_firewall_includes refused an ordinary target with both files present"
  run_installer_step "wire_firewall_includes \"$target\"" >/dev/null \
    || fail "wire_firewall_includes refused on a second run over its own block"

  local blocks
  blocks="$(grep -c '^# BEGIN Maran firewall' "$target" || true)"
  [ "$blocks" -eq 1 ] \
    || fail "the include target carries ${blocks} Maran blocks after two runs, not 1"

  local bans_line rules_line
  bans_line="$(grep -n 'firewall-bans.nft' "$target" | cut -d: -f1)"
  rules_line="$(grep -n '"/etc/maran/firewall.nft"' "$target" | cut -d: -f1)"
  [ -n "$bans_line" ] && [ -n "$rules_line" ] \
    || fail "the include block does not name both rendered files"
  [ "$bans_line" -lt "$rules_line" ] \
    || fail "the bans file is included AFTER the rules file. File order is load order, and the rules table's
chain hooks at a priority that assumes the bans table already exists."

  # Every include resolves: nft reads the target, follows both includes, and gets as far as
  # the kernel — which is where the missing capability, and nothing about the files, stops it.
  local output
  output="$(/usr/sbin/nft -c -f "$target" 2>&1 || true)"
  case "$output" in
    *"File not found"*)
      fail "nft could not resolve an include in the target the installer wired: ${output}"
      ;;
  esac

  # And the failure the ordering exists to prevent, produced rather than described: with one
  # rendered file missing, the whole load aborts — which is why 87-firewall.sh seeds both
  # files before either is included, and why its own guard must refuse such a target.
  rm -f /etc/maran/firewall-bans.nft
  output="$(/usr/sbin/nft -c -f "$target" 2>&1 || true)"
  case "$output" in
    *"File not found"*) ;;
    *) fail "nft accepted an include target whose file is missing (${output}). The ordering in 87-firewall.sh
is built on the opposite being true, so either nft changed or this check is looking at the wrong thing." ;;
  esac

  # Every half-seeded shape, because one of them is not enough and that was measured rather
  # than assumed. A target whose FIRST include is missing makes nft stop at the parser and
  # print only "File not found"; a target whose first include RESOLVES and declares a table
  # makes it print the missing file AND the capability error together — but only where the
  # target does not open with `flush ruleset`, which is exactly the shape RHEL ships and
  # Debian does not. So both orders are tried against both families' shapes: four cases, and
  # the one that matters is the one where a check looking for the capability error first
  # would wave a half-seeded host through.
  # An unmatched marker pair, with an operator's own rules below it. A `sed '/BEGIN/,/END/d'`
  # deletes from the opening marker to the END OF FILE when the closing one is gone — measured,
  # that removed a hand-written `table inet mine` from a real target. The step must refuse and
  # leave every byte of it alone: an installer that eats rules it never wrote has done more
  # damage than one that fails.
  # Both payloads back in place first, so the ONLY thing wrong with the target below is the
  # marker pair. Without this the refusal came from the missing include instead and the
  # assertion passed against a version with no marker checking at all — measured.
  cat > /etc/maran/firewall-bans.nft <<'PAYLOAD'
table inet maran_bans {
    chain input {
        type filter hook input priority -5; policy accept;
    }
}
PAYLOAD
  cat > /etc/maran/firewall.nft <<'PAYLOAD'
table inet maran {
    chain input {
        type filter hook input priority 0; policy accept;
    }
}
PAYLOAD

  local damaged="${scratch}/damaged.conf" operator_table
  operator_table='table inet mine {
    chain input {
        type filter hook input priority 10; policy accept;
    }
}'
  {
    printf '# a target with an operator table below a half-deleted Maran block\n'
    printf '%s\n' "$MARAN_FIREWALL_BEGIN_MARKER"
    printf 'include "/etc/maran/firewall-bans.nft"\n'
    printf '%s\n' "$operator_table"
  } > "$damaged"
  local before
  before="$(cat "$damaged")"
  if run_installer_step "wire_firewall_includes \"$damaged\"" >"$wire_output" 2>&1; then
    fail "wire_firewall_includes accepted a target whose Maran markers are not a matched pair. Deleting from
the opening marker to the end of the file takes an operator's own rules with it."
  fi
  grep -q 'not a matched pair' "$wire_output" \
    || fail "wire_firewall_includes refused the damaged target, but not for the marker reason:
$(cat "$wire_output")"
  [ "$(cat "$damaged")" = "$before" ] \
    || fail "wire_firewall_includes MODIFIED a target whose markers are not a matched pair:
$(diff <(printf '%s\n' "$before") "$damaged" || true)"
  grep -q '^table inet mine {' "$damaged" \
    || fail "the operator's own table was destroyed by the marker handling"

  # A payload that does not parse, behind an include, on the target shape where nft ALSO
  # complains about the kernel. This is the case a substring match got wrong: it classified
  # "cannot check here" and moved a broken ruleset into the live target, and the boot after
  # that has no firewall at all.
  cat > /etc/maran/firewall-bans.nft <<'PAYLOAD'
table inet maran_bans {
    chain input {
        type filter hook input priority -5; policy accept;
    }
}
PAYLOAD
  cat > /etc/maran/firewall.nft <<'PAYLOAD'
table inet maran {
    chain input {
        type filter hook input priority 0; policy drpo;
    }
}
PAYLOAD
  local broken_target="${scratch}/broken.conf"
  printf '# a target that ships nothing but comments\n' > "$broken_target"
  if run_installer_step "wire_firewall_includes \"$broken_target\"" >"$wire_output" 2>&1; then
    fail "wire_firewall_includes accepted a target whose included ruleset does not parse. On this shape nft
reports the syntax error and a capability error together, and treating the pair as 'cannot check here'
puts a file the next boot cannot load into the live path."
  fi
  grep -q 'does not parse' "$wire_output" \
    || fail "wire_firewall_includes refused the broken ruleset, but not for that reason:
$(cat "$wire_output")"
  grep -q '^# BEGIN Maran firewall' "$broken_target" \
    && fail "wire_firewall_includes refused the broken ruleset but wired the target anyway"

  local fresh="${scratch}/fresh.conf" head missing
  for head in "flush" "comments"; do
    for missing in "bans" "rules"; do
      rm -f /etc/maran/firewall-bans.nft /etc/maran/firewall.nft
      if [ "$missing" != "bans" ]; then
        cat > /etc/maran/firewall-bans.nft <<'PAYLOAD'
table inet maran_bans {
    chain input {
        type filter hook input priority -5; policy accept;
    }
}
PAYLOAD
      fi
      if [ "$missing" != "rules" ]; then
        cat > /etc/maran/firewall.nft <<'PAYLOAD'
table inet maran {
    chain input {
        type filter hook input priority 0; policy accept;
    }
}
PAYLOAD
      fi

      if [ "$head" = "flush" ]; then
        printf '#!/usr/sbin/nft -f\nflush ruleset\n' > "$fresh"
      else
        printf '# a target that ships nothing but comments\n' > "$fresh"
      fi

      if run_installer_step "wire_firewall_includes \"$fresh\"" >"$wire_output" 2>&1; then
        fail "wire_firewall_includes accepted a target whose ${missing} file does not exist, against a
${head}-shaped include target. A half-seeded host wired that way boots with nftables.service FAILED and no
firewall at all — which is the failure the step's seed-both-first ordering exists to prevent."
      fi
      grep -q 'names a file that does not exist' "$wire_output" \
        || fail "wire_firewall_includes refused the ${missing}-missing ${head}-shaped target, but not because
of the missing file:
$(cat "$wire_output")"
    done
  done

  rm -f /etc/maran/firewall.nft /etc/maran/firewall-bans.nft
  rm -f "$wire_output"
  rm -rf "$scratch"
  echo "The include block is wired once, bans before rules, every include resolves, and a missing one is refused."
}

# run_uninstaller: runs uninstaller code the way the uninstaller runs it — in a shell with
# `set -euo pipefail` and installer/uninstall.sh sourced — and hands back its status.
#
# A CHILD, for a reason beyond the errexit one run_installer_step gives: uninstall.sh defines
# its own `main`, and sourcing it into THIS script would replace the `main` at the bottom of
# this file. The build step would then run an uninstall instead of a suite. uninstall.sh runs
# its `main` only when it is EXECUTED and not when it is sourced, which is what makes sourcing
# it in a child safe and what makes this assertion possible at all.
run_uninstaller() {
  local snippet="$1"
  bash -c 'set -euo pipefail
. "$1"
eval "$2"' _ "$UNINSTALLER" "$snippet"
}

# firewall_host_state: describe what an uninstall left behind, in one line, for a failure to
# quote back. Deliberately reads the host rather than remembering what was set up.
firewall_host_state() {
  local target="$1"
  printf 'wired=%s ruleset=%s bans=%s' \
    "$(grep -qE '^[[:space:]]*include[[:space:]]+"?/etc/maran/' "$target" 2>/dev/null \
        && echo yes || echo no)" \
    "$([ -e /etc/maran/firewall.nft ] && echo present || echo gone)" \
    "$([ -e /etc/maran/firewall-bans.nft ] && echo present || echo gone)"
}

# nft_reports_a_missing_include: 0 when real nft, reading this file, cannot find something it
# includes — the witness state this whole assertion exists to make unreachable.
#
# `File not found` and not a status, because a status is unusable here: this script runs at
# image BUILD time with no CAP_NET_ADMIN, so `nft -c -f` fails on a perfectly good file with a
# capability error. The parser reaches an include before the kernel is asked, so the missing
# include is reported at this privilege level and the load's other failure is not confused
# with it.
nft_reports_a_missing_include() {
  case "$(/usr/sbin/nft -c -f "$1" 2>&1 || true)" in
    *"File not found"*) return 0 ;;
  esac
  return 1
}

# nft_residue: everything real nft complains about in <file> EXCEPT this container's missing
# CAP_NET_ADMIN, one complaint per line. Empty means the next boot would load this file.
#
# The same filter remove_firewall applies to its own candidate, deliberately: the assertion then
# judges the host by the standard the uninstaller judged it by, so neither can be right about a
# file the other calls broken. A missing include is caught more precisely by the function above;
# this is the general form, and it is what notices the removal that leaves an operator's table
# unterminated — a state with no missing include in it at all.
nft_residue() {
  /usr/sbin/nft -c -f "$1" 2>&1 | grep 'Error:' | grep -v 'Operation not permitted' || true
}

# assert_uninstaller_never_leaves_a_dangling_include: the uninstaller's firewall half, driven
# against real files, a real include target and real nft — because nothing in this repository
# drove it at all.
#
# Nothing: no image ran it, no harness sourced it, and its hand-copied marker state machine
# could be replaced by the `sed '/BEGIN/,/END/d'` that once destroyed an operator's own
# `table inet mine` while `maran structure`, `bash -n` and both image builds stayed green. The
# installer's copy of that state machine is mutation-proved by assert_firewall_include_wiring;
# this is the other copy.
#
# What it asserts is ONE property, in six host states: an uninstall never leaves this machine
# with an include naming a file it deleted. That state is not "no Maran firewall" — `nft -f` on
# a missing include is `Error: File not found`, rc 1, and the ENTIRE load aborts, so the
# operator's own tables in the same file do not load either. nftables.service is FAILED at the
# next boot and the host has no firewall whatsoever.
#
# The six states are the six ways this script has actually reached it, each one reproduced
# with the real uninstaller before it was fixed:
#
#   1. the ordinary wired host          — the positive control, and it must really DELETE:
#                                         without it, an uninstaller that kept everything would
#                                         pass all five cases below.
#   2. markers that are not a pair      — the unwiring refuses, so the files are still included.
#   3. include lines with no markers     — an operator who followed 87-firewall.sh's own advice
#                                         to "remove both markers and everything between them".
#   4. an include with a trailing comment — nft reads the path between the quotes; a reader that
#                                         stripped one quote off each END of the line derived a
#                                         path matching nothing, and deleted the file.
#   5. an include reached only through another file — nft follows includes, so the question
#                                         "does anything still include this" must follow them too.
#   6. a removal nft would reject       — the OTHER refusal in remove_firewall, and the one
#                                         nothing here reached. remove_firewall carries two
#                                         hand-copied duplicates of 87-firewall.sh: the marker
#                                         state machine, which case 2 and m5 cover, and the
#                                         `nft -c -f` residue check on the candidate. With that
#                                         second one deleted whole, this suite finished at rc 0
#                                         — measured. A branch that guards an operator's file
#                                         and cannot be observed to guard it is not guarded.
#
# Both deleting functions run, in the order main() runs them, because the defect this replaces
# lived in the gap between them: remove_firewall decided to keep the files and said so, and
# remove_config_and_state deleted the whole directory four calls later at exit status 0.
assert_uninstaller_never_leaves_a_dangling_include() {
  local target backup_config backup_target extra_directory
  target="$(nftables_include_target)"
  extra_directory="/etc/maran-polygon-operator"

  # This drives the REAL deleters at the REAL paths, so it saves what it is about to destroy
  # and puts it back — including on the way out of a failure, the way the sshd assertion does.
  backup_config="$(mktemp -d)"
  cp -a /etc/maran "${backup_config}/maran"
  backup_target="$(mktemp)"
  if [ -e "$target" ]; then
    cp -a "$target" "$backup_target"
  else
    rm -f "$backup_target"
  fi

  local outcome=""
  local case_name state residue
  for case_name in wired damaged-markers no-markers trailing-comment indirect nft-rejects; do
    rm -rf /etc/maran "$extra_directory"
    install -d -m 0750 /etc/maran
    cat > /etc/maran/firewall-bans.nft <<'PAYLOAD'
table inet maran_bans {
    chain input {
        type filter hook input priority -5; policy accept;
    }
}
PAYLOAD
    cat > /etc/maran/firewall.nft <<'PAYLOAD'
table inet maran {
    chain input {
        type filter hook input priority 0; policy accept;
    }
}
PAYLOAD
    # panel.env holds the encryption key and must go in every one of these cases; it is here so
    # that "kept the directory" can never be confused with "kept everything in it".
    printf 'Firewall__SshPorts=22\n' > /etc/maran/panel.env

    printf '#!/usr/sbin/nft -f\nflush ruleset\n' > "$target"
    case "$case_name" in
      wired | damaged-markers)
        {
          printf '%s\n' "$MARAN_FIREWALL_BEGIN_MARKER"
          printf 'include "/etc/maran/firewall-bans.nft"\n'
          printf 'include "/etc/maran/firewall.nft"\n'
          [ "$case_name" = wired ] && printf '%s\n' "$MARAN_FIREWALL_END_MARKER"
          # An operator's own table, BELOW the block. On the damaged host it is what a
          # `sed '/BEGIN/,/END/d'` takes with it: a range whose end marker is missing deletes
          # to the END OF FILE, and that is not a hypothetical — it removed a real
          # `table inet mine` from a real target. The installer's copy of the state machine
          # that replaced it is mutation-proved by assert_firewall_include_wiring; this line
          # is what proves the uninstaller's copy, which nothing exercised at all.
          printf 'table inet mine {\n    chain input {\n'
          printf '        type filter hook input priority 10; policy accept;\n    }\n}\n'
        } >> "$target"
        ;;
      no-markers)
        printf 'include "/etc/maran/firewall-bans.nft"\ninclude "/etc/maran/firewall.nft"\n' >> "$target"
        ;;
      trailing-comment)
        printf 'include "/etc/maran/firewall-bans.nft"\n' >> "$target"
        printf 'include "/etc/maran/firewall.nft" # the panel rules\n' >> "$target"
        ;;
      indirect)
        install -d -m 0755 "$extra_directory"
        printf 'include "/etc/maran/firewall-bans.nft"\ninclude "/etc/maran/firewall.nft"\n' \
          > "${extra_directory}/maran.nft"
        printf 'include "%s/maran.nft"\n' "$extra_directory" >> "$target"
        ;;
      nft-rejects)
        # A MATCHED marker pair, so the state machine accepts and hands on a candidate — and
        # an opening marker the operator pasted INSIDE their own table, so the block the
        # markers delimit carries that table's two closing braces away with it. The file is
        # valid nft now and the candidate is not, which is the single thing the `nft -c -f`
        # on the candidate exists to notice.
        #
        # A syntax error rather than a rule the kernel refuses, because this script runs at
        # image build time with no CAP_NET_ADMIN: every rule here fails with `Operation not
        # permitted`, which remove_firewall filters out precisely so that an uninstall inside
        # a container still gets a clean removal. The parser runs before the kernel is asked,
        # so a syntax error is the one complaint that survives that filter and is therefore
        # the only way to reach this branch from here — measured both ways.
        {
          printf 'table inet mine {\n    chain input {\n'
          printf '        type filter hook input priority 10; policy accept;\n'
          printf '%s\n' "$MARAN_FIREWALL_BEGIN_MARKER"
          printf '    }\n}\n'
          printf 'include "/etc/maran/firewall-bans.nft"\n'
          printf 'include "/etc/maran/firewall.nft"\n'
          printf '%s\n' "$MARAN_FIREWALL_END_MARKER"
        } >> "$target"
        ;;
    esac

    local output status=0
    output="$(run_uninstaller 'remove_firewall
remove_maran_config_directory' 2>&1)" || status=$?
    state="$(firewall_host_state "$target")"

    if [ "$status" -ne 0 ]; then
      outcome="the uninstaller failed on the '${case_name}' host (exit ${status}):
${output}"
      break
    fi
    if [ -e /etc/maran/panel.env ]; then
      outcome="the uninstaller left /etc/maran/panel.env behind on the '${case_name}' host. It holds the
encryption key for every secret in the panel's database and must never survive on a host the panel has
been removed from."
      break
    fi

    # The property that covers all six, ahead of any case's own: whatever this uninstall decided,
    # the file the next boot reads must still load. Checked for every host and not only for the
    # ones that keep something, because the ordinary wired host is rewritten too — and a rewrite
    # that produces a target nft rejects costs the operator every rule in it, ours and theirs.
    #
    # Two messages off one condition, because the two ways to fail it want different words. A
    # missing include is the deletion defect and names the file that went; anything else is a
    # target this script broke by rewriting it, which has no missing file in it at all. Deciding
    # between them AFTER the residue is in hand keeps one gate over all six cases and still puts
    # the specific diagnosis in front of the reader.
    residue="$(nft_residue "$target")"
    if [ -n "$residue" ]; then
      if nft_reports_a_missing_include "$target"; then
        outcome="the uninstaller left the '${case_name}' host with an include naming a file it deleted
(${state}). nft answers:
${residue}

The next boot loads NOTHING from ${target} — not our tables and not the operator's — so
nftables.service is FAILED and the host has no firewall at all. The uninstaller said:
${output}"
      else
        outcome="the uninstaller left the '${case_name}' host with a ${target} that nft rejects:
${residue}

Nothing is missing from this host — the file itself no longer parses, so the uninstaller broke it by
rewriting it. The next boot loads nothing from it, ours or the operator's. The uninstaller said:
${output}"
      fi
      break
    fi

    # The two hosts whose target the uninstaller must not rewrite at all, for two different
    # reasons — a marker pair it cannot trust, and a removal nft would reject. Both are checked
    # by the same evidence: the operator's own table, which only an uninstaller that rewrote the
    # file anyway can lose.
    case "$case_name" in
      damaged-markers | nft-rejects)
        if ! grep -q '^table inet mine {' "$target"; then
          outcome="the uninstaller destroyed an operator's own 'table inet mine' on the '${case_name}' host.
Deleting from the opening marker to the end of the file, or replacing the target with a candidate nft
rejects, takes rules this installer never wrote; an uninstaller that eats them has done more damage than
one that leaves its own block behind and says so. ${target} is now:
$(cat "$target")"
          break
        fi
        ;;
    esac

    if [ "$case_name" = nft-rejects ]; then
      # The refusal has to name ITS reason. Both refusals in remove_firewall leave the block
      # wired, so "the files are still here" cannot tell them apart — and an operator whose
      # markers are fine is sent to look for a damaged pair by the wrong message.
      case "$output" in
        *"that nft rejects"*) ;;
        *)
          outcome="the uninstaller kept the block on a host where removing it would leave a ${target} that
nft rejects, but did not say that is why. The other refusal blames the markers, which are a matched pair
here, so the operator is sent to look for a fault that is not there:
${output}" ;;
      esac
      [ -z "$outcome" ] || break
    fi

    if [ "$case_name" = wired ]; then
      # The positive control. Without it every check below is satisfied by an uninstaller that
      # deletes nothing at all, which is the shape of vacuous pass this plan keeps finding.
      case "$state" in
        "wired=no ruleset=gone bans=gone") ;;
        *)
          outcome="the uninstaller left an ordinary wired host at '${state}'. It must remove its own include
block and then its own two files, or an uninstall leaves the panel's firewall enforcing on a machine
the panel is gone from:
${output}"
          break
          ;;
      esac
      if [ -e /etc/maran ]; then
        outcome="the uninstaller kept /etc/maran on a host where nothing includes anything in it:
${output}"
        break
      fi
      continue
    fi

    # The five states where something still includes the rendered files. The host they leave has
    # already been checked to load; what is left to ask is whether the operator was told why a
    # file survived, since a file kept in silence is a file nobody removes.
    case "$output" in
      *"still includes"* | *"still names them"*) ;;
      *)
        outcome="the uninstaller kept files on the '${case_name}' host without telling the operator which
lines are keeping them there. A file kept in silence is a file nobody removes:
${output}"
        break
        ;;
    esac
  done

  # Put the host back before anything is reported, so a failure here does not also break every
  # assertion after it.
  rm -rf /etc/maran "$extra_directory"
  cp -a "${backup_config}/maran" /etc/maran
  rm -rf "$backup_config"
  if [ -e "$backup_target" ]; then
    cp -a "$backup_target" "$target"
  else
    rm -f "$target"
  fi
  rm -f "$backup_target"

  [ -z "$outcome" ] || fail "$outcome"
  [ -d /etc/maran/nginx/sites ] \
    || fail "this assertion did not put /etc/maran back the way it found it"

  # And main's ORDER, because that is precisely where the defect lived: remove_firewall decided
  # to keep the two rendered files and said so, and remove_config_and_state deleted the directory
  # holding them four calls later. The loop above drives those two in main's order — but it
  # drives them BY NAME, so a main that reordered them would leave all six cases green.
  #
  # Read out of `declare -f`, which prints the function bash actually parsed, rather than off the
  # file: a doc comment naming either function cannot satisfy this the way a grep over the source
  # would, and that shape of check has already been found here once (the three raw-file greps in
  # assert_firewall_renders_through_the_agent, satisfied by a comment).
  # `declare -f` re-prints each call with a trailing `;`, and the anchors carry it: without them
  # `remove_firewall` would also match the `remove_firewall_rendered_files` line inside it, which
  # is a different function and a different question.
  local main_body firewall_at config_at
  main_body="$(run_uninstaller 'declare -f main')"
  firewall_at="$(printf '%s\n' "$main_body" | grep -nE '^[[:space:]]*remove_firewall;?[[:space:]]*$' \
    | head -1 | cut -d: -f1 || true)"
  config_at="$(printf '%s\n' "$main_body" | grep -nE '^[[:space:]]*remove_config_and_state;?[[:space:]]*$' \
    | head -1 | cut -d: -f1 || true)"
  [ -n "$firewall_at" ] && [ -n "$config_at" ] \
    || fail "uninstall.sh's main no longer calls both remove_firewall and remove_config_and_state as plain
commands. Everything above drives those two by name, so this suite would keep passing while the
uninstaller did something else entirely. main is:
${main_body}"
  [ "$firewall_at" -lt "$config_at" ] \
    || fail "uninstall.sh's main runs remove_config_and_state BEFORE remove_firewall. /etc/maran holds both
rendered files and the marker saying whether this installer enabled nftables, so reading that marker after
the directory is gone makes every uninstall decide 'we did not enable it' — and the firewall unwiring then
asks about files that have already been deleted. main is:
${main_body}"

  echo "The uninstaller removes its own block and files from a wired host, and on five hosts that still"
  echo "include them keeps every named file and says which lines are keeping it. Every one of the six"
  echo "is left with an ${target} real nft still loads."
}

# sshd_effective_ports: the ports of the sockets sshd will actually open, one per line.
#
# The oracle for the assertion below, and the reason it is an oracle rather than a second
# opinion: `sshd -T` prints the effective configuration AFTER processing Include, so it knows
# about drop-in files, this family's own layout and its own defaults.
#
# It reads `listenaddress`, NOT `port`, and the difference is not cosmetic. `port` is the Port
# OPTION, which sshd prints always and defaults to 22 whether or not a socket uses it;
# `listenaddress` is the socket list. Measured on OpenSSH 9.6p1 and 9.9p1, which agree:
#
#   ListenAddress 0.0.0.0:2300, no Port directive  ->  port 22            listenaddress 0.0.0.0:2300
#   Port 2244 + ListenAddress 0.0.0.0:2200 + [::]:2222
#                                                  ->  port 2244         listenaddress 0.0.0.0:2200
#                                                                        listenaddress [::]:2222
#
# In the first, sshd serves 2300 and nothing else; an oracle reading `port` would demand 22 and
# FAIL the build over a detector that answered 2300 correctly. In the second, sshd serves 2200
# and 2222 and never 2244. Reading `port` inverted both. It stays as the fallback for the case
# where a version prints no listenaddress at all, where it is the only answer available.
sshd_effective_ports() {
  local dump ports
  dump="$("$(sshd_binary)" -T -f "$SSHD_CONFIG" 2>/dev/null || true)"
  if [ -z "$dump" ]; then
    # Some versions refuse `-T` outright while a `Match` block is present, and this image has
    # one by the time this runs — 86-sftp.sh appends it. `-C` names a connection to evaluate
    # the Match blocks against, which makes the dump well defined; `Port` and `ListenAddress`
    # are global keywords, so which connection is named cannot change the answer. (Measured:
    # both families' sshd accept the plain form, so this path has never been needed here.)
    dump="$("$(sshd_binary)" -T -C user=root,host=localhost,addr=127.0.0.1 -f "$SSHD_CONFIG" 2>/dev/null || true)"
  fi

  # `0.0.0.0:2300` and `[::]:2222` are the two shapes sshd prints; the port follows the last
  # colon in both. Parsed here rather than through the installer's own helper on purpose — an
  # oracle that shares code with the thing it checks is not an oracle.
  ports="$(printf '%s\n' "$dump" | awk '
      $1 == "listenaddress" {
        spec = $2
        n = split(spec, parts, ":")
        if (parts[n] ~ /^[0-9]+$/) { print parts[n] }
      }' | sort -n -u)"
  if [ -z "$ports" ]; then
    ports="$(printf '%s\n' "$dump" | awk '$1 == "port" { print $2 }' | sort -n -u)"
  fi
  printf '%s\n' "$ports"
}
