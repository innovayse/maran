#!/usr/bin/env bash
# Local submission binding (issue #518): mail an account submits through `sendmail` is bound to the
# account's own sender domains and to its hourly send limit by the Rspamd Lua rule.
# The contract is docs/mail/local-submission.md.
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The cases (all through the real `sendmail`/`postdrop`, run as a real system user, into the real Postfix
# `postdrop` -> `pickup` -> `cleanup_local` road and the real Rspamd milter):
#   1. an allowed account sending from its own domain: accepted (delivered or queued). This also proves the
#      milter is handed the X-Maran-Local-Submitter header the cleanup_local table makes from pickup's own
#      Received line (topmost) and the daemon name MARAN_LOCAL
#      (otherwise the rule would refuse or pass the message for the wrong reason);
#   2. the envelope sender is another account's domain: refused WITHOUT a delivery failure notice
#      (discarded, no backscatter to the forged address);
#   3. the envelope sender is owned but the From: header is another account's domain: rejected 5.7.1;
#   4. a uid the data plane does not list: refused (by postdrop, or by the rule);
#   5. the N+1th message inside the hour: deferred 4.7.1, not rejected;
#   6. a customer-written Received line and X-Maran-Local-Submitter header naming another uid: ignored, only
#      the topmost submitter header counts;
#   7. more recipients than the cap: rejected (too many recipients);
#   8. PHP mail() without -f (envelope user@host): refused, not delivered (supported config is -f);
#   9. a hand-built postdrop stream carrying forged sasl_username / client_address attributes: still
#      judged by the uid, never taken for an authenticated login or an SMTP client;
#  10. the deferred message is retried by pickup without being counted again (needs redis-cli, see below);
#  11. the invariants the rule stands on: postdrop is 2755 root:<drop group> and the test accounts are in
#      neither the drop group nor the postfix group;
#  12. the last account removed: every map holds only the comment line the agent writes for an empty table,
#      Rspamd is reloaded, and the account that was bound is refused (an empty map must refresh the rule).
#
# WHERE IT RUNS: it needs Postfix, Rspamd and the key-value server running with the rule installed.
# The polygon images boot no mail daemons (see mail-uninstall.sh), so on those images the assertion
# says so in the output and checks nothing: that is a GAP stated, not a pass. It is runnable on any
# host where the mail stack exists (MARAN_MAIL_LOCAL_SUBMIT_REQUIRED=yes turns the gap into a failure).
#
# The maps are planted by hand in the format the agent renders (docs/mail/local-submission.md), and
# the originals are restored afterwards.

readonly LOCAL_SUBMIT_DIRECTORY="/etc/maran/mail"
readonly LOCAL_SUBMIT_LIMIT=3
readonly LOCAL_SUBMIT_RECIPIENT_CAP=100
readonly LOCAL_SUBMIT_PICKUP_WAIT=70
readonly LOCAL_SUBMIT_MAPS="local_submit_accounts.map account_sender_domains.map account_send_limits.map mailbox_accounts.map"
readonly LOCAL_SUBMIT_USER_OWN="maranls1"
readonly LOCAL_SUBMIT_USER_OTHER="maranls2"
readonly LOCAL_SUBMIT_USER_UNLISTED="maranls3"
# What sendmail printed to the submitting user, kept for the diagnosis printed when an outcome is unknown.
readonly LOCAL_SUBMIT_SENDMAIL_LOG="/tmp/local-submit-sendmail.log"

# local_submit_stack_present: the daemons this assertion drives exist and answer.
local_submit_stack_present() {
  command -v postfix >/dev/null 2>&1 \
    && command -v rspamd >/dev/null 2>&1 \
    && command -v sendmail >/dev/null 2>&1 \
    && postfix status >/dev/null 2>&1 \
    && rspamadm configtest >/dev/null 2>&1
}

# local_submit_mail_log: the file Postfix logs to on this host, empty when there is none.
local_submit_mail_log() {
  local candidate
  for candidate in /var/log/mail.log /var/log/maillog; do
    [ -r "$candidate" ] && { echo "$candidate"; return; }
  done
}

# local_submit_send <user> <envelope sender or empty> <from header domain> <marker> [extra header] [recipients]:
# submit one message through sendmail as <user>. The marker is the local part of the Message-ID, which
# Postfix's cleanup logs with the queue id, so the log lines of this message can be found. An empty envelope
# sender omits -f, which is what PHP mail() does without a configured sender. <recipients> is how many
# distinct recipients to name (default 1).
local_submit_send() {
  local user="$1" sender="$2" domain="$3" marker="$4" extra="${5:-}" count="${6:-1}"
  local recipients="" index from_flag=""
  for index in $(seq 1 "$count"); do recipients="${recipients} probe${index}@example.net"; done
  [ -z "$sender" ] || from_flag="-f '${sender}'"
  {
    [ -z "$extra" ] || printf '%s\n' "$extra"
    printf 'From: <%s@%s>\nTo: <probe@example.net>\nSubject: %s\nMessage-ID: <%s@polygon.test>\n\nbody\n' "$user" "$domain" "$marker" "$marker"
  } | su -s /bin/sh -c "sendmail -i ${from_flag} ${recipients}" "$user" >/dev/null 2>>"${LOCAL_SUBMIT_SENDMAIL_LOG}" || true
}

# local_submit_record <type> <data>: one postdrop queue record (type byte, one length byte, data). Only for
# data shorter than 128 bytes, which is all this assertion writes.
local_submit_record() {
  printf '%s' "$1"
  printf "\\$(printf '%03o' "${#2}")"
  printf '%s' "$2"
}

# local_submit_raw_postdrop <user> <envelope sender> <recipient> <marker> <attribute>...: hand a record stream
# with forged attribute records straight to postdrop, as the user. UNVERIFIED: which attribute records
# postdrop copies and which pickup forwards; the assertion only needs the outcome to stay "refused".
local_submit_raw_postdrop() {
  local user="$1" sender="$2" recipient="$3" marker="$4" attribute stream
  shift 4
  stream="$(mktemp)"
  {
    local_submit_record C "$(printf '%15d %15d %15d %15d %15d' 0 0 0 0 0)"
    local_submit_record T "$(date +%s)"
    local_submit_record S "$sender"
    local_submit_record R "$recipient"
    for attribute in "$@"; do local_submit_record A "$attribute"; done
    local_submit_record M ""
    local_submit_record N "From: <${user}@one.test>"
    local_submit_record N "To: <${recipient}>"
    local_submit_record N "Message-ID: <${marker}@polygon.test>"
    local_submit_record N ""
    local_submit_record N "body"
    local_submit_record X "0 0 0"
    local_submit_record E ""
  } > "$stream"
  chmod 0644 "$stream"
  su -s /bin/sh -c "postdrop -r < '${stream}'" "$user" >/dev/null 2>&1 || true
  rm -f -- "$stream"
}

# local_submit_outcome <marker>: print accepted, rejected, deferred or unknown from the mail log.
# Waits briefly: pickup runs asynchronously.
local_submit_outcome() {
  local marker="$1" log queue_id line attempt
  log="$(local_submit_mail_log)"
  [ -n "$log" ] || { echo unknown; return; }
  postqueue -f >/dev/null 2>&1 || true
  for attempt in 1 2 3 4 5 6 7 8 9 10; do
    queue_id="$(grep -F -- "$marker" "$log" 2>/dev/null | sed -nE 's/.*\]: ([0-9A-F]{8,}): .*/\1/p' | head -n 1 || true)"
    if [ -n "$queue_id" ]; then
      line="$(grep -F -- "$queue_id" "$log" | tr '\n' ' ')"
      case "$line" in
        *"milter-discard"*|*"DISCARD"*) echo discarded; return ;;
        *"too many recipients"*|*"5.5.3"*) echo rejected-recipients; return ;;
        *"5.7.1"*) echo rejected; return ;;
        *"4.7.1"*|*"status=deferred"*) echo deferred; return ;;
        *"status=sent"*) echo accepted; return ;;
        # Past the filter and queued for delivery. The rule's verdict is what these cases judge, and
        # delivery to the probe address may take longer than the wait (an image with port 25 egress
        # blocked sits in the connection attempt), so a message the queue manager took is accepted. A
        # refusal or deferral by the filter is logged by cleanup and never reaches the queue manager, and
        # the patterns above for those win over this one.
        *"nrcpt="*"(queue active)"*) echo accepted; return ;;
      esac
    fi
    sleep 1
  done
  # Said once, on stderr, so a lane that cannot tell what happened to a message shows what it saw.
  {
    echo "local_submit_outcome: no verdict for ${marker} in ${log}; what the host shows:"
    echo "--- sendmail said:"; cat "${LOCAL_SUBMIT_SENDMAIL_LOG}" 2>/dev/null || true
    echo "--- authorized_submit_users: $(postconf -h authorized_submit_users 2>&1)"
    echo "--- the mail log's last lines:"; tail -n 30 "$log" 2>/dev/null || true
    echo "--- the queue:"; postqueue -p 2>&1 | head -n 20 || true
  } >&2
  echo unknown
}

# local_submit_submit_table: print the lookup table item of Postfix's `authorized_submit_users` (type:path),
# which the setting lists after the names `root` and `$mail_owner`; nothing when there is none.
local_submit_submit_table() {
  postconf -h authorized_submit_users 2>/dev/null | tr ',' '\n' | sed 's/^[[:space:]]*//' | grep -E '^[a-z]+:/' | head -n 1 || true
}

# local_submit_admit_users: Postfix's own gate, `authorized_submit_users`, admits only the users the agent
# listed, and it runs before the rule this part judges. The three users here are the rule's subjects, so
# they are admitted at Postfix and the rule alone decides what happens to their mail. The table is the one
# the agent rendered (its path and type come from the live configuration, not from a literal); it is put
# back by local_submit_restore.
local_submit_admit_users() {
  local specification table user
  specification="$(local_submit_submit_table)"
  [ -n "$specification" ] || return 0
  table="${specification#*:}"
  [ -f "$table" ] || : > "$table"
  for user in "$LOCAL_SUBMIT_USER_OWN" "$LOCAL_SUBMIT_USER_OTHER" "$LOCAL_SUBMIT_USER_UNLISTED"; do
    grep -qx "${user} OK" "$table" || printf '%s OK\n' "$user" >> "$table"
  done
  postmap "$specification" >/dev/null 2>&1 || true
  postfix reload >/dev/null 2>&1 || true
}

# local_submit_plant: write the four maps for the three users: maranls1 is account acc1 (domain
# one.test, limit LOCAL_SUBMIT_LIMIT), maranls2 is account acc2 (domain two.test); maranls3 is listed
# nowhere. Reloads Rspamd.
local_submit_plant() {
  local own_uid other_uid map
  own_uid="$(id -u "$LOCAL_SUBMIT_USER_OWN")"
  other_uid="$(id -u "$LOCAL_SUBMIT_USER_OTHER")"
  printf '%s acc1\n%s acc2\n' "$own_uid" "$other_uid" > "${LOCAL_SUBMIT_DIRECTORY}/local_submit_accounts.map"
  printf 'acc1 one.test\nacc2 two.test\n' > "${LOCAL_SUBMIT_DIRECTORY}/account_sender_domains.map"
  printf 'acc1 %s\nacc2 %s\n' "$LOCAL_SUBMIT_LIMIT" "$LOCAL_SUBMIT_LIMIT" > "${LOCAL_SUBMIT_DIRECTORY}/account_send_limits.map"
  # acc2's mailbox login is planted so a forged sasl_username naming it would matter if it were believed.
  printf 'info@two.test acc2\n' > "${LOCAL_SUBMIT_DIRECTORY}/mailbox_accounts.map"
  for map in $LOCAL_SUBMIT_MAPS; do chmod 0644 "${LOCAL_SUBMIT_DIRECTORY}/${map}"; done
  local_submit_admit_users
  systemctl reload rspamd.service >/dev/null 2>&1 || rspamadm control reload >/dev/null 2>&1 || true
}

# local_submit_diagnose <marker>: on stderr, what the host holds when the first case does not come out as
# the rule says it must: the maps as planted, the user's id, Rspamd's and Postfix's last lines for the message.
local_submit_diagnose() {
  local marker="$1" map log
  {
    echo "local_submit_diagnose: ${marker}"
    echo "--- ids: $(id "$LOCAL_SUBMIT_USER_OWN" 2>&1)"
    for map in $LOCAL_SUBMIT_MAPS; do echo "--- ${map}:"; cat "${LOCAL_SUBMIT_DIRECTORY}/${map}" 2>&1 | head -n 6; done
    log="$(local_submit_mail_log)"
    echo "--- mail log, lines for the message:"; grep -F -- "$marker" "$log" 2>/dev/null | head -n 5
    echo "--- mail log, last lines:"; tail -n 12 "$log" 2>/dev/null
    echo "--- the directory and who may read a map:"
    ls -ld "$LOCAL_SUBMIT_DIRECTORY" "${LOCAL_SUBMIT_DIRECTORY}/local_submit_accounts.map" 2>&1
    su -s /bin/sh -c "head -n 2 '${LOCAL_SUBMIT_DIRECTORY}/local_submit_accounts.map'" "$(ps -o user= -C rspamd 2>/dev/null | head -n 1 | tr -d ' ')" 2>&1 | head -n 3
    echo "--- rspamd log, lines about the rule or a map (other than the vendor's):"
    grep -hE "maran_local_submit|local_submit|maran/mail|SIGHUP|reload" /var/log/rspamd/rspamd.log 2>/dev/null | grep -v "rspamd.com" | tail -n 25
    echo "--- rspamd log, last lines:"
    for log in /var/log/rspamd/rspamd.log /var/log/rspamd/rspamd.err; do [ -r "$log" ] && tail -n 25 "$log"; done
  } >&2 || true
}

# local_submit_wait_for_the_maps: Rspamd loads the planted maps asynchronously after its reload, and a message
# that reaches it first is judged against the ones it had (the agent's empty tables). The probe is a message
# from the first user naming more recipients than the rule allows: once the planted accounts map is loaded the
# rule finds the account and refuses the recipient count, which counts nothing against the hourly limit, and
# before that it discards the sender as unknown. It waits for the first answer and fails by name otherwise.
local_submit_wait_for_the_maps() {
  local attempt marker result=""
  for attempt in $(seq 1 40); do
    marker="lsub-ready-$$-${attempt}"
    local_submit_send "$LOCAL_SUBMIT_USER_OWN" "${LOCAL_SUBMIT_USER_OWN}@one.test" one.test "$marker" "" 101
    result="$(local_submit_outcome "$marker" 2>/dev/null)"
    [ "$result" != rejected-recipients ] || return 0
    sleep 1
  done
  local_submit_diagnose "lsub-ready-$$-40"
  fail "Rspamd never loaded the planted accounts map: a probe of the first user was ${result}, not refused for its recipient count"
}

# local_submit_restore <backup dir>: put the maps back as they were, remove the ones that did not exist.
local_submit_restore() {
  local backup="$1" map specification
  for map in $LOCAL_SUBMIT_MAPS; do
    if [ -e "${backup:?}/${map}" ]; then
      cp -p -- "${backup:?}/${map}" "${LOCAL_SUBMIT_DIRECTORY:?}/${map}"
    else
      rm -f -- "${LOCAL_SUBMIT_DIRECTORY:?}/${map:?}"
    fi
  done
  specification="$(local_submit_submit_table)"
  if [ -n "$specification" ] && [ -e "${backup:?}/authorized_submit_users" ]; then
    cp -p -- "${backup:?}/authorized_submit_users" "${specification#*:}"
    postmap "$specification" >/dev/null 2>&1 || true
    postfix reload >/dev/null 2>&1 || true
  fi
  rm -rf -- "${backup:?}"
}

# assert_local_submission_is_bound_to_the_account: the whole subject of this part.
assert_local_submission_is_bound_to_the_account() {
  if ! local_submit_stack_present; then
    if [ "${MARAN_MAIL_LOCAL_SUBMIT_REQUIRED:-no}" = yes ]; then
      fail "the mail stack (postfix, rspamd, sendmail) is not running here and MARAN_MAIL_LOCAL_SUBMIT_REQUIRED=yes"
    fi
    echo "UNOBSERVED HERE (GAP): the local submission assertions (twelve cases, see the header of docker/polygon/asserts/parts/mail-local-submit.sh) did not run: this image has no running Postfix and Rspamd. They are runnable on any host with the mail stack and the rule installed (docs/mail/local-submission.md)."
    return 0
  fi

  local user backup marker result sent map before after submit_table_specification
  backup="$(mktemp -d)"
  for user in "$LOCAL_SUBMIT_USER_OWN" "$LOCAL_SUBMIT_USER_OTHER" "$LOCAL_SUBMIT_USER_UNLISTED"; do
    id "$user" >/dev/null 2>&1 || useradd -m -s /bin/sh "$user"
  done
  mkdir -p "$LOCAL_SUBMIT_DIRECTORY"
  submit_table_specification="$(local_submit_submit_table)"
  [ -z "$submit_table_specification" ] || [ ! -f "${submit_table_specification#*:}" ] \
    || cp -p -- "${submit_table_specification#*:}" "${backup}/authorized_submit_users"
  for map in $LOCAL_SUBMIT_MAPS; do
    [ ! -e "${LOCAL_SUBMIT_DIRECTORY}/${map}" ] || cp -p -- "${LOCAL_SUBMIT_DIRECTORY}/${map}" "${backup}/${map}"
  done
  local_submit_plant
  local_submit_wait_for_the_maps

  # 1. Accepting control: own account, own domain, under the limit.
  marker="lsub-own-$$-$RANDOM"
  local_submit_send "$LOCAL_SUBMIT_USER_OWN" "${LOCAL_SUBMIT_USER_OWN}@one.test" one.test "$marker"
  result="$(local_submit_outcome "$marker")"
  [ "$result" = accepted ] || { local_submit_diagnose "$marker"; local_submit_restore "$backup"; fail "an allowed account sending from its own domain was ${result}, not accepted"; }

  # 2. Another account's domain as the ENVELOPE sender: refused, and Postfix must not mail a failure notice
  # to the forged address (a discard, not a bounce).
  marker="lsub-foreign-$$-$RANDOM"
  local_submit_send "$LOCAL_SUBMIT_USER_OWN" "${LOCAL_SUBMIT_USER_OWN}@two.test" two.test "$marker"
  result="$(local_submit_outcome "$marker")"
  [ "$result" = discarded ] || { local_submit_restore "$backup"; fail "a message with another account's envelope sender was ${result}, not discarded without a failure notice"; }

  # 3. Owned envelope sender, foreign From: header: rejected (its notice goes to the account itself).
  marker="lsub-fromhdr-$$-$RANDOM"
  local_submit_send "$LOCAL_SUBMIT_USER_OWN" "${LOCAL_SUBMIT_USER_OWN}@one.test" two.test "$marker"
  result="$(local_submit_outcome "$marker")"
  [ "$result" = rejected ] || { local_submit_restore "$backup"; fail "a message with another account's From: domain was ${result}, not rejected 5.7.1"; }

  # 4. A uid the data plane does not list.
  marker="lsub-unlisted-$$-$RANDOM"
  local_submit_send "$LOCAL_SUBMIT_USER_UNLISTED" "${LOCAL_SUBMIT_USER_UNLISTED}@one.test" one.test "$marker"
  result="$(local_submit_outcome "$marker")"
  case "$result" in
    accepted|deferred) local_submit_restore "$backup"; fail "an unlisted uid's message was ${result}; it must be refused" ;;
  esac

  # 5. The N+1th message in the hour: acc1 has already sent one above, so send the rest of the limit
  # and then one more. The over-limit one must be deferred (4.7.1), never rejected.
  for sent in $(seq 2 "$LOCAL_SUBMIT_LIMIT"); do
    local_submit_send "$LOCAL_SUBMIT_USER_OWN" "${LOCAL_SUBMIT_USER_OWN}@one.test" one.test "lsub-fill-${sent}-$$-$RANDOM"
  done
  sleep 2
  marker="lsub-over-$$-$RANDOM"
  local_submit_send "$LOCAL_SUBMIT_USER_OWN" "${LOCAL_SUBMIT_USER_OWN}@one.test" one.test "$marker"
  result="$(local_submit_outcome "$marker")"
  [ "$result" = deferred ] || { local_submit_restore "$backup"; fail "the message after the hourly limit was ${result}, not deferred 4.7.1"; }

  # 6. A forged Received line and a forged submitter header naming the listed uid, sent by the UNLISTED
  # user: a forgery that would work if the rule read any header but the topmost submitter header.
  marker="lsub-forged-$$-$RANDOM"
  local_submit_send "$LOCAL_SUBMIT_USER_UNLISTED" "${LOCAL_SUBMIT_USER_UNLISTED}@one.test" one.test "$marker" \
    "$(printf 'X-Maran-Local-Submitter: %s FORGED\nReceived: by forged.test (Postfix, from userid %s)\n\tid FORGED;' "$(id -u "$LOCAL_SUBMIT_USER_OWN")" "$(id -u "$LOCAL_SUBMIT_USER_OWN")")"
  result="$(local_submit_outcome "$marker")"
  case "$result" in
    accepted|deferred) local_submit_restore "$backup"; fail "a forged Received line and submitter header made an unlisted uid's message ${result}; only the topmost submitter header is trusted" ;;
  esac

  # 7. More recipients than the cap (sendmail has no smtpd_recipient_limit). acc2 is used so acc1's
  # exhausted bucket is not what refuses it.
  marker="lsub-rcpts-$$-$RANDOM"
  local_submit_send "$LOCAL_SUBMIT_USER_OTHER" "${LOCAL_SUBMIT_USER_OTHER}@two.test" two.test "$marker" "" "$((LOCAL_SUBMIT_RECIPIENT_CAP + 1))"
  result="$(local_submit_outcome "$marker")"
  [ "$result" = rejected-recipients ] || { local_submit_restore "$backup"; fail "a message with more than ${LOCAL_SUBMIT_RECIPIENT_CAP} recipients was ${result}, not rejected 5.5.3"; }

  # 8. PHP mail() without a configured sender uses <user>@<myorigin>, a domain no account owns. It must be
  # refused, which is why the supported configuration sets sendmail_path with -f.
  marker="lsub-phpdefault-$$-$RANDOM"
  local_submit_send "$LOCAL_SUBMIT_USER_OTHER" "" two.test "$marker"
  result="$(local_submit_outcome "$marker")"
  case "$result" in
    accepted|deferred) local_submit_restore "$backup"; fail "a message with the default PHP mail() envelope sender was ${result}; the supported configuration is sendmail -f" ;;
  esac

  # 9. A hand-built queue stream with forged SMTP attributes: acc2's mailbox login as sasl_username and a
  # foreign client address. Sent by acc1 with acc2's envelope sender: if either attribute were believed the
  # message would be counted as acc2's authenticated mail and accepted.
  marker="lsub-rawattrs-$$-$RANDOM"
  local_submit_raw_postdrop "$LOCAL_SUBMIT_USER_OWN" "${LOCAL_SUBMIT_USER_OTHER}@two.test" probe@example.net "$marker" \
    "sasl_username=info@two.test" "sasl_method=PLAIN" "client_address=203.0.113.9" "client_name=forged.example.net"
  result="$(local_submit_outcome "$marker")"
  case "$result" in
    accepted|deferred) local_submit_restore "$backup"; fail "a hand-built postdrop stream with forged sasl_username/client attributes was ${result}; the uid stamp must decide" ;;
  esac

  # 10. Retry: the deferred message of case 5 stays in the maildrop queue and pickup rescans it. The counter
  # must not grow while it is retried. Needs redis-cli and the key-value socket
  # (MARAN_LOCAL_SUBMIT_REDIS_SOCKET); without them the gap is stated.
  if command -v redis-cli >/dev/null 2>&1 && [ -n "${MARAN_LOCAL_SUBMIT_REDIS_SOCKET:-}" ]; then
    before="$(redis-cli -s "$MARAN_LOCAL_SUBMIT_REDIS_SOCKET" GET maran:send:acc1 2>/dev/null || true)"
    sleep "$LOCAL_SUBMIT_PICKUP_WAIT"
    after="$(redis-cli -s "$MARAN_LOCAL_SUBMIT_REDIS_SOCKET" GET maran:send:acc1 2>/dev/null || true)"
    [ "$before" = "$after" ] || { local_submit_restore "$backup"; fail "the retried message was counted again (bucket ${before} -> ${after}); a message refused for the limit must not charge the bucket on every retry"; }
  else
    echo "UNOBSERVED HERE (GAP): the retry-is-not-counted-twice assertion did not run: set MARAN_LOCAL_SUBMIT_REDIS_SOCKET and install redis-cli."
  fi

  # 11. The invariants the rule stands on.
  local drop_group postfix_group listed
  drop_group="$(postconf -h setgid_group 2>/dev/null || echo postdrop)"
  [ "$(stat -c '%a %U:%G' /usr/sbin/postdrop 2>/dev/null)" = "2755 root:${drop_group}" ] \
    || { local_submit_restore "$backup"; fail "/usr/sbin/postdrop is not 2755 root:${drop_group}"; }
  postfix_group="postfix"
  for user in "$LOCAL_SUBMIT_USER_OWN" "$LOCAL_SUBMIT_USER_OTHER" "$LOCAL_SUBMIT_USER_UNLISTED"; do
    listed=" $(id -nG "$user") "
    case "$listed" in
      *" ${drop_group} "*|*" ${postfix_group} "*) local_submit_restore "$backup"; fail "customer ${user} is in the ${drop_group} or ${postfix_group} group; it could write the queue without the uid stamp" ;;
    esac
  done

  # 12. The last account removed: maps that hold only the comment line must replace the populated copy in
  # Rspamd's memory, so the previously bound account is refused.
  for map in $LOCAL_SUBMIT_MAPS; do
    printf '# no entries\n' > "${LOCAL_SUBMIT_DIRECTORY}/${map}"
    chmod 0644 "${LOCAL_SUBMIT_DIRECTORY}/${map}"
  done
  systemctl reload rspamd.service >/dev/null 2>&1 || rspamadm control reload >/dev/null 2>&1 || true
  sleep 2
  marker="lsub-emptied-$$-$RANDOM"
  local_submit_send "$LOCAL_SUBMIT_USER_OWN" "${LOCAL_SUBMIT_USER_OWN}@one.test" one.test "$marker"
  result="$(local_submit_outcome "$marker")"
  case "$result" in
    accepted|deferred) local_submit_restore "$backup"; fail "after the last account was removed (maps hold only a comment line) its message was ${result}; Rspamd kept the stale binding" ;;
  esac

  local_submit_restore "$backup"
  echo "local submission is bound to the account: own domain accepted, a foreign envelope sender discarded, a foreign From: rejected, an unlisted uid refused, the message after the limit deferred, a forged second Received header ignored, the recipient cap enforced, a default PHP sender refused, forged postdrop attributes ignored, the drop road sealed, an emptied map refreshing the rule."
}
