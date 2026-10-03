#!/usr/bin/env bash
# sshd port detection through includes, and the whitelist seed's address walk
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The split is by SUBJECT and preserves the original order exactly, because these definitions
# are order-dependent in one respect that matters — a `readonly` set twice is a fatal error, so
# each constant lives beside the only assertions that use it.


# assert_detection_covers_sshd: every port sshd will listen on is in detect_ssh_ports' answer.
#
# A SUPERSET is allowed and a subset is not, deliberately. Do not tighten this to an equality.
# The detector CAN name a port sshd does not serve — `Port 2244` beside two `ListenAddress`
# ports makes it answer 2200,2222,2244 where sshd opens 2200 and 2222 — and that is the whole
# of the cost: one extra `accept` in a default-drop ruleset, attack surface rather than a
# lockout, on a port number the host's own administrator wrote into sshd_config. The other
# direction costs the operator their server. Equality is not merely stricter here, it is
# unachievable: the installer parses a file, the oracle asks a daemon, and the two disagree
# in exactly this harmless direction by design.
assert_detection_covers_sshd() {
  local context="$1" detected expected port missing=""
  expected="$(sshd_effective_ports)"
  [ -n "$expected" ] \
    || fail "sshd -T named no port at all for ${context}; the oracle this assertion depends on is not working"

  detected="$(detect_ssh_ports "$SSHD_CONFIG")"
  case "$detected" in
    ''|*[!0-9,]*) fail "detect_ssh_ports answered '${detected}' for ${context}, which is not a port list" ;;
  esac

  for port in $expected; do
    case ",${detected}," in
      *",${port},"*) ;;
      *) missing="${missing} ${port}" ;;
    esac
  done
  [ -z "$missing" ] \
    || fail "for ${context}, sshd listens on${missing} but detect_ssh_ports answered '${detected}'.
The firewall would close a port this host's sshd is serving, and the operator would lose the server."

  echo "  ${context}: sshd names $(echo $expected | tr '\n' ' '), detect_ssh_ports answers ${detected}."
}

# assert_ssh_port_detection_follows_includes: the installer's own port detection, against
# this family's real sshd_config, its real sshd and a real drop-in.
#
# The middle case is the whole point. Ubuntu and Debian ship
# `Include /etc/ssh/sshd_config.d/*.conf` as the first line of sshd_config, and a modern port
# override is a file in there — so a parser that reads only the main file answers 22 for a
# host whose sshd is on 2222, and the firewall then locks the operator out of half the
# platforms we support on their default configuration. A fixture cannot prove this: whether
# the Include is there at all is a fact about the distribution, which is why it is asserted
# on the image. On a family that ships no Include, sshd -T reports no 2222 either and the
# assertion still holds — the oracle adjusts itself.
assert_ssh_port_detection_follows_includes() {
  assert_detection_covers_sshd "this family's stock sshd_config"

  mkdir -p "$(dirname "$SSHD_TEST_DROP_IN")"
  printf 'Port 2222\n' > "$SSHD_TEST_DROP_IN"
  assert_detection_covers_sshd "a drop-in adding Port 2222"

  # The same file with CRLF line endings, because sshd accepts one and serves the port while a
  # parser that leaves the carriage return on the value reads no port at all and the firewall
  # closes it. There was a guard for this in the installer and nothing that exercised it, which
  # is the shape this repository keeps finding: a fixture that names a mechanism is not one
  # that runs it.
  printf 'Port 2222\r\n' > "$SSHD_TEST_DROP_IN"
  assert_detection_covers_sshd "a CRLF drop-in adding Port 2222"

  # A host whose port comes ONLY from ListenAddress, with no Port directive anywhere. sshd
  # serves 2300 alone while still printing `port 22`, so this is the shape that tells a correct
  # oracle from one reading the wrong field — it is here because an earlier version of this
  # function failed the build on the detector's correct answer.
  printf 'ListenAddress 0.0.0.0:2300\n' > "$SSHD_TEST_DROP_IN"
  assert_detection_covers_sshd "a drop-in with only ListenAddress 0.0.0.0:2300"

  rm -f "$SSHD_TEST_DROP_IN"
  assert_detection_covers_sshd "the stock sshd_config again, drop-in removed"
}

# assert_whitelist_seed_walks_this_login_session: the /proc ancestor walk and its session
# bound, on the image, with SSH_CLIENT absent from the process that runs the detection.
#
# It exists because every other case here SETS SSH_CLIENT, so the walk — the whole mechanism
# that makes the seed work under `sudo bash install.sh`, and the session check that keeps a
# tmux server's stale address out of the whitelist — had no coverage at all. That is this
# repository's own lesson applied to the fix for the finding that taught it: naming a mechanism
# in a comment is not exercising it.
assert_whitelist_seed_walks_this_login_session() {
  command -v setsid >/dev/null 2>&1 \
    || fail "setsid (util-linux) is not in this image, so the cross-session refusal cannot be exercised.
It is the control that proves the session check does the refusing rather than the test's own shape."

  local probe recovered refused
  probe="$(mktemp)"
  cat > "$probe" <<PROBE
. "${CONFIG_STEP}"
detect_seed_whitelist_cidr
PROBE

  # The shape sudo leaves behind: an ancestor in this login session holds the address, the
  # process running the detection does not. The trailing command keeps the holder alive —
  # without one bash exec-replaces it, the walk finds nothing, and the test passes for the
  # wrong reason. That false negative happened once already while this was being written.
  recovered="$(SSH_CLIENT="198.51.100.9 40000 22" \
    bash -c "env -u SSH_CLIENT bash '${probe}' 2>/dev/null; echo -n ''")"
  [ "$recovered" = "198.51.100.9/32" ] \
    || fail "with SSH_CLIENT only in an ancestor of the same login session, detect_seed_whitelist_cidr
answered '${recovered}' instead of 198.51.100.9/32. Under 'sudo bash install.sh' — the installer's own
documented usage — the whitelist would be seeded with nothing and the operator could ban themselves."

  # The same chain with one variable changed: the detection runs in its own session, as it does
  # inside a tmux or screen pane whose server is an ancestor carrying somebody else's address.
  refused="$(SSH_CLIENT="198.51.100.9 40000 22" \
    bash -c "setsid --wait env -u SSH_CLIENT bash '${probe}' 2>/dev/null; echo -n ''")"
  [ -z "$refused" ] \
    || fail "an ancestor OUTSIDE this login session seeded '${refused}'. That is a tmux or screen server's
address, which may be another operator on another machine from days ago, and it would be whitelisted here."

  rm -f "$probe"
  echo "The client-address walk recovers from an ancestor in this login session and refuses one outside it."
}

# assert_whitelist_seed_takes_only_addresses: the other lockout-relevant function, on the
# family's own bash.
#
# The three refusals are not pedantry. A malformed row reaches the panel as a whitelist
# entry that can never match a packet, so the operator reads their own address back out of
# the panel, believes they are exempt from the automatic bans, and is not.
assert_whitelist_seed_takes_only_addresses() {
  local seeded
  seeded="$(SSH_CLIENT="203.0.113.7 54321 22" detect_seed_whitelist_cidr 2>/dev/null)"
  [ "$seeded" = "203.0.113.7/32" ] \
    || fail "detect_seed_whitelist_cidr made '${seeded}' out of an ordinary IPv4 SSH_CLIENT"

  seeded="$(SSH_CLIENT="2001:db8::7 54321 22" detect_seed_whitelist_cidr 2>/dev/null)"
  [ "$seeded" = "2001:db8::7/128" ] \
    || fail "detect_seed_whitelist_cidr made '${seeded}' out of an IPv6 SSH_CLIENT"

  local malformed
  for malformed in "999.999.999.999" "1.2.3.4:5" "::::::::::" "01.2.3.4" "$(printf '203.0.113.7\nInjected=1')"; do
    seeded="$(SSH_CLIENT="${malformed} 1 22" detect_seed_whitelist_cidr 2>/dev/null)"
    [ -z "$seeded" ] \
      || fail "detect_seed_whitelist_cidr seeded '${seeded}' from a client address that is not an address"
  done

  echo "detect_seed_whitelist_cidr seeds real addresses and refuses the rest."
}
