#!/usr/bin/env bash
# Port 25 is closed to customers, and local mail is signed and charged for what it is (issue #531).
# The contract is docs/mail/local-submission.md and the threat note amendment #531 in
# docs/superpowers/notes/2026-11-07-mail-module-threat-note.md.
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The cases (all through the real Postfix, Rspamd and nftables, run as real system users):
#   1. a customer uid cannot talk SMTP to 127.0.0.1:25, [::1]:25 or the host's public address: the nftables
#      `output` chain resets the connection (layer two);
#   2. root still reaches the port at the TCP level, so layer two is a uid rule and not a port rule; Postfix
#      itself still answers it with a rejection, because it rejects every client address of this host
#      (layer one);
#   3. the `output` chain of table `inet maran` exists and names the uid floor and port 25;
#   4. with the nftables table removed (layer two lost) a customer is STILL not greeted: Postfix refuses its
#      own address (layer one);
#   5. mail from a sender that is not a bound customer carries no DKIM signature, whatever its From: domain;
#   6. mail from the bound customer with its own From: domain is signed, and with a foreign From: domain is not;
#   7. an alias with N outside targets debits the sender's hourly count N times, also when the recipient names
#      an address extension (alias+tag@) and for a catch-all (needs redis-cli);
#   8. Rspamd listens on no TCP port at all: the normal worker (loopback 11333, which takes the sending road
#      from a request header) is switched off, and the proxy and controller sit on unix sockets;
#   9. the signing module reads the exact From: domain (use_esld is off), so a subdomain a tenant hosts is not
#      signed with the key of its parent domain;
#  10. an address and a routed block added to the host after the agent started reach BOTH layers (the nftables
#      sets self_v4 and Postfix's refused-client table) without any firewall rule changing, and leave them when
#      removed (issue #535); needs ip and the running agent, whose periodic check is the thing under test.
#
# WHERE IT RUNS: it needs Postfix, Rspamd, nftables and the rule installed and running. The polygon images
# boot no mail daemons (see mail-uninstall.sh), so on those images the assertion says so in the output and
# checks nothing: that is a GAP stated, not a pass. MARAN_MAIL_PORT25_REQUIRED=yes turns the gap into a failure.
# Cases 5 and 6 read the signature from the queued message (`postcat`), so they need a message that stays
# queued: the recipient domain is one that cannot be delivered.

readonly PORT25_USER_CUSTOMER="maranp25"
readonly PORT25_LOGIN_DEFS="/etc/login.defs"
readonly PORT25_CONNECT_TIMEOUT=5
readonly PORT25_FANOUT_TARGETS=3
readonly PORT25_CATCH_ALL_TARGETS=4
readonly PORT25_FOLLOW_ADDRESS="192.0.2.77"
readonly PORT25_FOLLOW_BLOCK="198.51.100.0/24"
readonly PORT25_SELF_CLIENTS_TABLE="/etc/maran/mail/self_clients.cidr"
# The agent checks the host's addresses once a minute (CHECK_INTERVAL in address_watch/run_host_address_watch.rs);
# the wait is that interval plus the time a refresh takes.
readonly PORT25_FOLLOW_WAIT_SECONDS=100

# port25_stack_present: the daemons this assertion drives exist and answer.
port25_stack_present() {
  command -v postfix >/dev/null 2>&1 \
    && command -v rspamd >/dev/null 2>&1 \
    && command -v nft >/dev/null 2>&1 \
    && command -v sendmail >/dev/null 2>&1 \
    && postfix status >/dev/null 2>&1 \
    && nft list table inet maran >/dev/null 2>&1
}

# port25_rspamd_tcp_listeners: the TCP listening sockets owned by an rspamd process, one per line, empty when
# there is none. Needs ss; prints nothing and returns 1 when ss is missing, so the caller can tell "no
# listener" from "cannot look".
port25_rspamd_tcp_listeners() {
  command -v ss >/dev/null 2>&1 || return 1
  ss -H -ltnp 2>/dev/null | grep -F 'rspamd' || true
}

# port25_uid_min: the first uid a customer can have, as the host defines it. The nftables rule is built from
# the same figure, so a host that changed it is still covered.
port25_uid_min() {
  local value
  value="$(awk '$1 == "UID_MIN" { print $2 }' "$PORT25_LOGIN_DEFS" 2>/dev/null | tail -n 1)"
  echo "${value:-1000}"
}

# port25_public_address: the first address of this host that is neither loopback nor link-local, empty when
# there is none.
port25_public_address() {
  hostname -I 2>/dev/null | tr ' ' '\n' | grep -v -E '^(127\.|::1$|fe80:|$)' | head -n 1 || true
}

# port25_probe <user> <address>: print refused when no connection was made, greeted when the first line of
# the answer is a 220 greeting, and rejected when the connection was made and the answer is anything else.
port25_probe() {
  local user="$1" address="$2" line
  line="$(su -s /bin/bash -c "timeout ${PORT25_CONNECT_TIMEOUT} bash -c 'exec 3<>/dev/tcp/${address}/25 && head -n 1 <&3'" "$user" 2>/dev/null || true)"
  case "$line" in
    "") echo refused ;;
    220*) echo greeted ;;
    *) echo rejected ;;
  esac
}

# port25_queued_headers <marker>: the header block of the queued message whose Message-ID carries the marker,
# as the milter left it. Empty when the message is not in the queue.
port25_queued_headers() {
  local marker="$1" log queue_id attempt
  log="$(local_submit_mail_log)"
  [ -n "$log" ] || return 0
  for attempt in 1 2 3 4 5 6 7 8 9 10; do
    queue_id="$(grep -F -- "$marker" "$log" 2>/dev/null | sed -nE 's/.*\]: ([0-9A-F]{8,}): .*/\1/p' | head -n 1 || true)"
    if [ -n "$queue_id" ] && postcat -hq "$queue_id" 2>/dev/null; then
      return 0
    fi
    sleep 1
  done
  # Said once, on stderr, so a lane that cannot find the queued message shows what the host held.
  {
    echo "port25_queued_headers: no queued message for ${marker} (queue id '${queue_id}'); what the host shows:"
    echo "--- mail log, lines for the marker:"; grep -F -- "$marker" "$log" 2>/dev/null | tail -n 5
    echo "--- mail log, last lines:"; tail -n 15 "$log" 2>/dev/null
    echo "--- the queue:"; postqueue -p 2>&1 | head -n 20 || true
  } >&2
}

# port25_signed <marker>: yes when the queued message carries a DKIM-Signature header, no when it is queued
# without one, unknown when it never reached the queue.
port25_signed() {
  local headers
  headers="$(port25_queued_headers "$1")"
  [ -n "$headers" ] || { echo unknown; return; }
  if printf '%s\n' "$headers" | grep -qi '^DKIM-Signature:'; then echo yes; else echo no; fi
}

# port25_plant_dkim_key <domain>: give the domain a signing key and selector the way the data plane does, so a
# message that SHOULD be signed can be told from one that is not. Prints the paths it wrote, one per line, for
# the caller to remove.
port25_plant_dkim_key() {
  local domain="$1" selectors keys_directory selector="polygon"
  selectors="$(rspamadm configdump -j dkim_signing 2>/dev/null | sed -n 's/.*"selector_map": *"\([^"]*\)".*/\1/p' | head -n 1)"
  keys_directory="$(rspamadm configdump -j dkim_signing 2>/dev/null | sed -n 's/.*"path": *"\(.*\)\/\$domain\.\$selector\.key".*/\1/p' | head -n 1)"
  [ -n "$selectors" ] && [ -n "$keys_directory" ] || return 1
  mkdir -p "$keys_directory"
  openssl genrsa -out "${keys_directory}/${domain}.${selector}.key" 2048 >/dev/null 2>&1
  chmod 0640 "${keys_directory}/${domain}.${selector}.key"
  chgrp "$(stat -c '%G' "$keys_directory")" "${keys_directory}/${domain}.${selector}.key" 2>/dev/null || true
  printf '%s %s\n' "$domain" "$selector" >> "$selectors"
  echo "${keys_directory}/${domain}.${selector}.key"
  echo "$selectors"
}

# port25_counter <account>: the account's hourly send count, 0 when there is none.
port25_counter() {
  local value
  value="$(redis-cli -s "$MARAN_LOCAL_SUBMIT_REDIS_SOCKET" GET "maran:send:$1" 2>/dev/null || true)"
  echo "${value:-0}"
}

# assert_port_25_is_closed_to_customers: the whole subject of this part.
assert_port_25_is_closed_to_customers() {
  if ! port25_stack_present; then
    if [ "${MARAN_MAIL_PORT25_REQUIRED:-no}" = yes ]; then
      fail "the mail stack (postfix, rspamd, nftables table inet maran) is not running here and MARAN_MAIL_PORT25_REQUIRED=yes"
    fi
    echo "UNOBSERVED HERE (GAP): the port 25 lockdown assertions (ten cases, see the header of docker/polygon/asserts/parts/mail-port25-lockdown.sh) did not run: this image has no running Postfix, Rspamd and nftables table. They are runnable on any host with the mail stack installed (docs/mail/local-submission.md)."
    return 0
  fi

  local address result saved marker public uid_min chain listeners
  public="$(port25_public_address)"
  uid_min="$(port25_uid_min)"
  id "$PORT25_USER_CUSTOMER" >/dev/null 2>&1 || useradd -m -s /bin/sh "$PORT25_USER_CUSTOMER"
  saved="$(mktemp)"
  nft list table inet maran > "$saved"

  # 3. The second layer is present and states the uid floor and the port.
  chain="$(nft list chain inet maran output 2>/dev/null || true)"
  case "$chain" in
    *"skuid >= ${uid_min}"*"dport 25"*"reject"*) ;;
    *) rm -f -- "$saved"; fail "the output chain of table inet maran does not reject uid >= ${uid_min} to port 25 on this host's own addresses (found: ${chain:-no chain})" ;;
  esac

  # 8. Rspamd has no TCP listener: the normal worker would let any local process claim the sending road in a
  # request header and read the signature out of the answer.
  if listeners="$(port25_rspamd_tcp_listeners)"; then
    [ -z "$listeners" ] || { rm -f -- "$saved"; fail "Rspamd listens on a TCP port; the normal worker must be switched off and the proxy and controller must use unix sockets (found: ${listeners})"; }
  else
    echo "UNOBSERVED HERE (GAP): the Rspamd listener assertion did not run: ss is missing."
  fi

  # 9. The signing module reads the exact From: domain, never its registrable parent.
  case "$(rspamadm configdump -j dkim_signing 2>/dev/null | tr -d ' \n')" in
    *'"use_esld":false'*) ;;
    *) rm -f -- "$saved"; fail "the signing module still maps a From: subdomain to its parent domain's key (use_esld is not false)" ;;
  esac

  # 1. A customer uid is refused at the connection on every address of this host.
  for address in 127.0.0.1 ::1 $public; do
    result="$(port25_probe "$PORT25_USER_CUSTOMER" "$address")"
    [ "$result" = refused ] || { rm -f -- "$saved"; fail "a customer uid reached ${address}:25 (${result}); the output chain must reset the connection"; }
  done

  # 2. Root is not caught by the uid rule: it connects, and Postfix itself answers with a rejection because
  # the address is this host's own. The pair proves the two layers are independent.
  for address in 127.0.0.1 $public; do
    result="$(port25_probe root "$address")"
    [ "$result" = rejected ] || { rm -f -- "$saved"; fail "root's connection to ${address}:25 was ${result}; it must connect (the uid rule is for customers) and be rejected by Postfix as a self client"; }
  done

  # 4. Layer two lost: the table is removed, and Postfix must still refuse a customer its greeting. The
  # table is put back on every path out.
  nft delete table inet maran
  for address in 127.0.0.1 $public; do
    result="$(port25_probe "$PORT25_USER_CUSTOMER" "$address")"
    if [ "$result" = greeted ]; then
      nft -f "$saved"; rm -f -- "$saved"
      fail "with the nftables table gone a customer was greeted on ${address}:25; Postfix must refuse its own address by itself"
    fi
  done
  nft -f "$saved"
  rm -f -- "$saved"
  nft list table inet maran >/dev/null 2>&1 || fail "the nftables table inet maran could not be restored"

  # 10. The host's addresses are followed by both layers.
  assert_port_25_layers_follow_the_hosts_addresses

  # 5-7 need the local submission binding and the bound-domain proof, which only exist when Rspamd runs the rule.
  assert_port_25_local_mail_is_signed_and_charged_for_what_it_is
  echo "port 25 is closed to customers on both layers (the uid rule resets the connection, Postfix rejects its own address with the table gone), root is not caught by the uid rule, Rspamd has no TCP listener, local mail is signed only for the bound exact domain and an alias, an alias behind an extension and a catch-all charge their outside fan-out."
}

# port25_layers_hold <address-or-block>: yes when the nftables set self_v4 AND Postfix's refused-client table both
# list it, no when neither does, and split when only one does (the failure this case exists for).
port25_layers_hold() {
  local needle="$1" in_set=no in_table=no
  nft list set inet maran self_v4 2>/dev/null | grep -qF -- "$needle" && in_set=yes
  grep -qF -- "$needle" "$PORT25_SELF_CLIENTS_TABLE" 2>/dev/null && in_table=yes
  if [ "$in_set" = yes ] && [ "$in_table" = yes ]; then echo yes
  elif [ "$in_set" = no ] && [ "$in_table" = no ]; then echo no
  else echo "split(set=${in_set},table=${in_table})"
  fi
}

# port25_wait_for_layers <address-or-block> <yes|no>: poll until both layers agree with the wanted answer,
# for at most PORT25_FOLLOW_WAIT_SECONDS. Prints the last answer.
port25_wait_for_layers() {
  local needle="$1" wanted="$2" waited=0 answer
  while :; do
    answer="$(port25_layers_hold "$needle")"
    if [ "$answer" = "$wanted" ] || [ "$waited" -ge "$PORT25_FOLLOW_WAIT_SECONDS" ]; then
      echo "$answer"
      return 0
    fi
    sleep 2
    waited=$((waited + 2))
  done
}

# port25_follow_cleanup: take the test address and block off the host again.
port25_follow_cleanup() {
  ip addr del "${PORT25_FOLLOW_ADDRESS}/32" dev lo >/dev/null 2>&1 || true
  ip route del local "$PORT25_FOLLOW_BLOCK" dev lo >/dev/null 2>&1 || true
}

# assert_port_25_layers_follow_the_hosts_addresses: case 10 (issue #535). An address and a routed block appear
# on the host while the agent runs; both layers must list them without a firewall rule being allowed or denied,
# and must drop them again when they go.
assert_port_25_layers_follow_the_hosts_addresses() {
  if ! command -v ip >/dev/null 2>&1 || ! pgrep -f 'maran-agent' >/dev/null 2>&1; then
    if [ "${MARAN_MAIL_PORT25_REQUIRED:-no}" = yes ]; then
      fail "case 10 needs ip and a running maran-agent and MARAN_MAIL_PORT25_REQUIRED=yes"
    fi
    echo "UNOBSERVED HERE (GAP): the address-follow case did not run: ip or the running agent is missing."
    return 0
  fi

  local before_rules answer
  before_rules="$(nft list chain inet maran input 2>/dev/null || true)"
  [ "$(port25_layers_hold "$PORT25_FOLLOW_ADDRESS")" = no ] || fail "the test address ${PORT25_FOLLOW_ADDRESS} is already on this host's lockdown layers"

  ip addr add "${PORT25_FOLLOW_ADDRESS}/32" dev lo || fail "the test address could not be added to lo"
  ip route add local "$PORT25_FOLLOW_BLOCK" dev lo >/dev/null 2>&1 || {
    port25_follow_cleanup
    fail "the test block ${PORT25_FOLLOW_BLOCK} could not be routed local on lo"
  }

  answer="$(port25_wait_for_layers "$PORT25_FOLLOW_ADDRESS" yes)"
  [ "$answer" = yes ] || { port25_follow_cleanup; fail "an address added to the host did not reach both layers within ${PORT25_FOLLOW_WAIT_SECONDS}s (${answer})"; }
  answer="$(port25_wait_for_layers "$PORT25_FOLLOW_BLOCK" yes)"
  [ "$answer" = yes ] || { port25_follow_cleanup; fail "a block routed local on the host did not reach both layers as a range (${answer})"; }
  [ "$(nft list chain inet maran input 2>/dev/null || true)" = "$before_rules" ] \
    || { port25_follow_cleanup; fail "the input chain changed while the address sets were refreshed; only the sets may change"; }

  # A customer is refused at the new address too: the kernel layer now knows it.
  answer="$(port25_probe "$PORT25_USER_CUSTOMER" "$PORT25_FOLLOW_ADDRESS")"
  [ "$answer" = refused ] || { port25_follow_cleanup; fail "a customer uid reached the new address ${PORT25_FOLLOW_ADDRESS}:25 (${answer})"; }

  # Inverse control: when the address and the block go, so do their entries.
  port25_follow_cleanup
  answer="$(port25_wait_for_layers "$PORT25_FOLLOW_ADDRESS" no)"
  [ "$answer" = no ] || fail "an address removed from the host stayed in a layer (${answer})"
  answer="$(port25_wait_for_layers "$PORT25_FOLLOW_BLOCK" no)"
  [ "$answer" = no ] || fail "a block no longer routed local stayed in a layer (${answer})"
}

# assert_port_25_local_mail_is_signed_and_charged_for_what_it_is: cases 5, 6 and 7.
assert_port_25_local_mail_is_signed_and_charged_for_what_it_is() {
  if ! command -v postcat >/dev/null 2>&1 || [ -z "$(local_submit_mail_log)" ]; then
    echo "UNOBSERVED HERE (GAP): the DKIM and fan-out cases did not run: postcat or the mail log is missing."
    return 0
  fi

  local backup marker result map before after planted_files user
  backup="$(mktemp -d)"
  for map in $LOCAL_SUBMIT_MAPS alias_fanout.map; do
    [ ! -e "${LOCAL_SUBMIT_DIRECTORY}/${map}" ] || cp -p -- "${LOCAL_SUBMIT_DIRECTORY}/${map}" "${backup}/${map}"
  done
  for user in "$LOCAL_SUBMIT_USER_OWN" "$LOCAL_SUBMIT_USER_OTHER" "$LOCAL_SUBMIT_USER_UNLISTED"; do
    id "$user" >/dev/null 2>&1 || useradd -m -s /bin/sh "$user"
  done
  mkdir -p "$LOCAL_SUBMIT_DIRECTORY"
  local_submit_plant
  # A limit high enough that the figures below are the fan-out and not the ceiling.
  printf 'acc1 1000\nacc2 1000\n' > "${LOCAL_SUBMIT_DIRECTORY}/account_send_limits.map"
  printf 'sales@one.test %s acc1\n' "$PORT25_FANOUT_TARGETS" > "${LOCAL_SUBMIT_DIRECTORY}/alias_fanout.map"
  chmod 0644 "${LOCAL_SUBMIT_DIRECTORY}/account_send_limits.map" "${LOCAL_SUBMIT_DIRECTORY}/alias_fanout.map"
  planted_files="$(port25_plant_dkim_key one.test || true)"
  systemctl reload rspamd.service >/dev/null 2>&1 || rspamadm control reload >/dev/null 2>&1 || true
  sleep 2

  if [ -z "$planted_files" ]; then
    port25_restore "$backup" ""
    echo "UNOBSERVED HERE (GAP): the DKIM cases did not run: the signing configuration of Rspamd could not be read here."
  else
    # 6a. Bound customer, own From: domain: signed.
    marker="p25-signed-$$-$RANDOM"
    local_submit_send "$LOCAL_SUBMIT_USER_OWN" "${LOCAL_SUBMIT_USER_OWN}@one.test" one.test "$marker"
    result="$(port25_signed "$marker")"
    [ "$result" = yes ] || { port25_restore "$backup" "$planted_files"; fail "a bound customer's message from its own domain was not DKIM-signed (${result})"; }

    # 6b. Bound customer, foreign From: domain: the rule refuses it before any signing.
    marker="p25-foreign-$$-$RANDOM"
    local_submit_send "$LOCAL_SUBMIT_USER_OWN" "${LOCAL_SUBMIT_USER_OWN}@one.test" two.test "$marker"
    result="$(port25_signed "$marker")"
    [ "$result" != yes ] || { port25_restore "$backup" "$planted_files"; fail "a bound customer's message with a foreign From: domain was DKIM-signed"; }

    # 6c. Bound customer, a subdomain of a signed domain that the account hosts too: no key exists for the
    # exact domain, so nothing is signed (with use_esld on, the parent's key would be used).
    marker="p25-subdomain-$$-$RANDOM"
    printf 'acc1 shop.one.test\n' >> "${LOCAL_SUBMIT_DIRECTORY}/account_sender_domains.map"
    sleep 2
    local_submit_send "$LOCAL_SUBMIT_USER_OWN" "${LOCAL_SUBMIT_USER_OWN}@shop.one.test" shop.one.test "$marker"
    result="$(port25_signed "$marker")"
    [ "$result" != yes ] || { port25_restore "$backup" "$planted_files"; fail "a message from a hosted subdomain was DKIM-signed with the key of its parent domain"; }

    # 5. A sender that is not a bound customer, naming the tenant's domain as From:: never signed.
    marker="p25-unbound-$$-$RANDOM"
    local_submit_send "$LOCAL_SUBMIT_USER_UNLISTED" "${LOCAL_SUBMIT_USER_UNLISTED}@one.test" one.test "$marker"
    result="$(port25_signed "$marker")"
    [ "$result" != yes ] || { port25_restore "$backup" "$planted_files"; fail "a message from a sender that is not bound to the domain was DKIM-signed"; }
  fi

  # 7. An alias with N outside targets debits N, also behind an address extension, and a catch-all debits its
  # own figure for an address nobody claims. Needs the key-value socket, as in the retry case.
  if command -v redis-cli >/dev/null 2>&1 && [ -n "${MARAN_LOCAL_SUBMIT_REDIS_SOCKET:-}" ]; then
    printf 'sales@one.test %s acc1\n@one.test %s acc1\n' "$PORT25_FANOUT_TARGETS" "$PORT25_CATCH_ALL_TARGETS" > "${LOCAL_SUBMIT_DIRECTORY}/alias_fanout.map"
    printf 'info@one.test acc1\n' > "${LOCAL_SUBMIT_DIRECTORY}/mailbox_accounts.map"
    chmod 0644 "${LOCAL_SUBMIT_DIRECTORY}/alias_fanout.map" "${LOCAL_SUBMIT_DIRECTORY}/mailbox_accounts.map"
    sleep 2
    port25_assert_debit "$backup" "$planted_files" sales@one.test "$PORT25_FANOUT_TARGETS" "an alias with ${PORT25_FANOUT_TARGETS} outside targets"
    port25_assert_debit "$backup" "$planted_files" sales+news@one.test "$PORT25_FANOUT_TARGETS" "the same alias behind an address extension"
    port25_assert_debit "$backup" "$planted_files" nobody@one.test "$PORT25_CATCH_ALL_TARGETS" "an address only the catch-all claims"
    # Inverse control: a mailbox of the account is one delivery whatever the catch-all says.
    port25_assert_debit "$backup" "$planted_files" info@one.test 1 "a mailbox of the account"
  else
    echo "UNOBSERVED HERE (GAP): the alias fan-out debit assertion did not run: set MARAN_LOCAL_SUBMIT_REDIS_SOCKET and install redis-cli."
  fi

  port25_restore "$backup" "$planted_files"
}

# port25_assert_debit <backup dir> <planted files> <recipient> <expected> <what>: send one message from the
# bound customer to the recipient and fail, restoring the maps first, unless the account's hourly count rose by
# exactly the expected figure.
port25_assert_debit() {
  local backup="$1" planted="$2" recipient="$3" expected="$4" what="$5" marker before after
  before="$(port25_counter acc1)"
  marker="p25-fanout-$$-$RANDOM"
  {
    printf 'From: <%s@one.test>\nTo: <%s>\nSubject: %s\nMessage-ID: <%s@polygon.test>\n\nbody\n' "$LOCAL_SUBMIT_USER_OWN" "$recipient" "$marker" "$marker"
  } | su -s /bin/sh -c "sendmail -i -f '${LOCAL_SUBMIT_USER_OWN}@one.test' '${recipient}'" "$LOCAL_SUBMIT_USER_OWN" >/dev/null 2>&1 || true
  sleep 3
  after="$(port25_counter acc1)"
  [ "$((after - before))" -eq "$expected" ] \
    || { port25_restore "$backup" "$planted"; fail "a message to ${what} (${recipient}) debited $((after - before)) from the hourly count, not ${expected}"; }
}

# port25_restore <backup dir> <planted files>: put the maps back as they were, remove the ones that did not
# exist, remove the planted signing key and selector line, and reload Rspamd.
port25_restore() {
  local backup="$1" planted="$2" map key selectors
  for map in $LOCAL_SUBMIT_MAPS alias_fanout.map; do
    if [ -e "${backup:?}/${map}" ]; then
      cp -p -- "${backup:?}/${map}" "${LOCAL_SUBMIT_DIRECTORY:?}/${map}"
    else
      rm -f -- "${LOCAL_SUBMIT_DIRECTORY:?}/${map:?}"
    fi
  done
  rm -rf -- "${backup:?}"
  if [ -n "$planted" ]; then
    key="$(printf '%s\n' "$planted" | sed -n 1p)"
    selectors="$(printf '%s\n' "$planted" | sed -n 2p)"
    rm -f -- "$key"
    sed -i '/^one\.test polygon$/d' "$selectors"
  fi
  systemctl reload rspamd.service >/dev/null 2>&1 || rspamadm control reload >/dev/null 2>&1 || true
}
