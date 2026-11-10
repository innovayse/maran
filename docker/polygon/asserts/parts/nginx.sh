#!/usr/bin/env bash
# 80-nginx.sh: the certificate, the vhost swap, validation, and what the vhost may log
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The split is by SUBJECT and preserves the original order exactly, because these definitions
# are order-dependent in one respect that matters — a `readonly` set twice is a fatal error, so
# each constant lives beside the only assertions that use it.


# assert_the_panel_certificate_is_never_written_through_a_symlink: step 80 generates a self-signed
# certificate only when there is none, and "none" must be a question about the PATH, not about what
# a link at that path resolves to.
#
# `[ -f ]` follows the link. So the gate that exists to leave an operator's own certificate alone
# answered about the TARGET, and for a link whose target has gone it answered "there is nothing
# here" — after which `openssl req -keyout/-out` writes THROUGH the link and creates this
# installer's throwaway key and certificate at whatever path the link names. The state is this
# function's own documented case: an operator who swapped in a real certificate, which for a
# renewing ACME client means symlinks at exactly these two names, and a renewal that moved the
# archive or a lineage that was removed leaves them dangling. Measured on both families before the
# fix: a fresh private key written into /etc/letsencrypt/live/panel/, silently, by an installer
# whose whole doctrine elsewhere is that it never writes through a link out of the directory it
# owns — here with key material, which is the one file where a wrong write cannot be undone by
# writing again.
#
# It is the same defect as the vhost gates', asked in the direction of ABSENCE rather than of type,
# and it is in this file because the previous rounds each fixed one instance of it and left another.
#
# It runs AFTER assert_nginx_vhost_is_validated_before_it_is_served, which is not cosmetic: the
# function under test creates its TLS directory `-g` the service group, so without that group the
# prepare step makes, the certificate this one puts back cannot be written and the vhost assertion
# would fail on a missing `ssl_certificate` for a reason of this assertion's making.
assert_the_panel_certificate_is_never_written_through_a_symlink() {
  local cert key lineage outcome=""

  cert="$(run_nginx_step 'printf %s "$MARAN_CERT_PATH"')"
  key="$(run_nginx_step 'printf %s "$MARAN_KEY_PATH"')"
  lineage="$(mktemp -d)"
  rm -f "$cert" "$key"
  mkdir -p "$(dirname "$cert")"
  ln -s "${lineage}/fullchain.pem" "$cert"
  ln -s "${lineage}/privkey.pem" "$key"

  run_nginx_step 'generate_self_signed_cert' >/dev/null 2>&1 || true

  if [ -e "${lineage}/privkey.pem" ] || [ -e "${lineage}/fullchain.pem" ]; then
    outcome="step 80 wrote a certificate THROUGH an operator's symlink: ${lineage} now holds
$(ls -1 "$lineage"). A dangling link at ${key} is not 'no certificate here' — the test [ -f ] follows the
link and answers about the target, while the thing that gate protects is the path. What was
written is a private key, into a directory an ACME client owns."
  elif [ ! -L "$cert" ] || [ ! -L "$key" ]; then
    outcome="step 80 replaced an operator's certificate symlink at ${cert} or ${key} with a file of
its own. A link under either name says a certificate of the operator's is in place; this step
neither follows it nor takes the name."
  fi

  # The links go, and a real certificate goes back: the assertion after this one installs the panel
  # vhost and runs `nginx -t`, which refuses a configuration whose `ssl_certificate` file is not
  # there. Restoring it through the step's own function rather than by hand keeps this assertion
  # from becoming a second authority on where that certificate lives.
  rm -f "$cert" "$key"
  rm -rf "$lineage"
  run_nginx_step 'generate_self_signed_cert' >/dev/null 2>&1 || true
  [ -z "$outcome" ] || fail "$outcome"
  { [ -f "$cert" ] && [ -f "$key" ]; } \
    || fail "this assertion did not put a panel certificate back at ${cert} / ${key}; the vhost
assertion after it would fail on a missing ssl_certificate for a reason of this one's making."

  echo "Step 80 leaves a certificate symlink alone and never writes a key through one, including when the"
  echo "link dangles — the state a renewing ACME client leaves after a lineage is removed."
}

# assert_the_vhost_swap_reports_a_failed_rename: step 80's move_into_place must return the status of
# the RENAME, not of the flush that follows it.
#
# Every swap of the served path and every restoration goes through that one helper. It has SEVEN
# call sites, not four, and they were counted rather than remembered: three test the return value
# with `|| return 1` (try_restore_from's forward and reverse renames, and restore_panel_vhost's
# last arm), and FOUR are bare — in write_rollback_copy, where it is the last command so its status
# IS the function's; at the promotion of `<dest>.adopted` back to `<dest>.previous`; at the
# adoption that moves `<dest>.previous` aside; and at the swap itself. A bare call is not a
# swallowed one: `set -e` aborts the shell on it, which at the first three is before the guard is
# armed and the served path is untouched, and at the swap runs the EXIT trap with
# MARAN_VHOST_GUARD_SWAPPED still 0, so the restoration correctly does nothing. The
# count is written out because it is what a reader will use to decide whether a NEW call site needs
# a guard, and the answer depends on which of those two shapes it is in.
#
# It used to end on `sync "$(dirname "$destination")"` and so returned THAT: the target directory
# always exists, the flush always succeeds, and all three guards were therefore decorative — a
# failed `mv` reported success.
# Measured before the fix, with the destination on a read-only filesystem: "RESULT: move_into_place
# returned 0 DESPITE mv failing", source still in place, destination never created.
#
# What that costs is not hypothetical at the one site where it is load-bearing. In try_restore_from
# a forward rename that silently did nothing is followed by the REVERSE rename, which then moves the
# refused candidate — sitting on the served path — onto `<dest>.previous`, the name holding the only
# copy of the entry bytes. `restore_panel_vhost`'s protective `[ ! -e "$previous" ]` is defeated by
# a name existing rather than by the bytes being right, and the served vhost is gone.
#
# THE CALL IS MADE IN A CONDITION, exactly as those three call sites make it, and that is the whole
# subtlety of the check: `set -e` is suppressed inside a function invoked in an `if` or a `||` list,
# so the failing `mv` does NOT abort and the return value is all there is. Called bare, the same
# mutant dies of `set -e` and the assertion would pass while the defect was live.
#
# The failure is produced without a mount or a privilege a build has no way to get: `mv` of a
# DIRECTORY onto an existing regular FILE is refused for root as for anybody ("cannot overwrite
# non-directory with directory"), while the parent directory the helper flushes afterwards is
# present and healthy — so a helper returning the flush's status returns 0 here and one returning
# the rename's returns non-zero. Directory-onto-directory would not do: `mv` moves the source
# INSIDE an existing directory and succeeds, which is why this assertion also checks that the
# rename it expected to fail did fail.
assert_the_vhost_swap_reports_a_failed_rename() {
  local scratch source destination answer

  scratch="$(mktemp -d)"
  source="${scratch}/source"
  destination="${scratch}/destination"
  mkdir -p "$source"
  : > "$destination"

  answer="$(run_nginx_step "if move_into_place '${source}' '${destination}' 2>/dev/null; then
printf SWALLOWED; else printf PROPAGATED; fi")"
  if [ ! -d "$source" ]; then
    rm -rf "$scratch"
    fail "this assertion cannot test what it names: the rename it expected to fail SUCCEEDED, so
move_into_place was never asked to report a failure."
  fi
  [ "$answer" = PROPAGATED ] \
    || fail "move_into_place returned success for a rename that failed (answer: ${answer:-none}). Its exit
status is the trailing directory flush's, and that flush cannot fail: the directory always exists.
So the three '|| return 1' guards on this helper test a value that is always 0 — including the one
in try_restore_from, where a forward rename that silently does nothing is followed by the reverse
one moving the REFUSED candidate onto the name holding the only copy of the entry bytes."

  rm -rf "$scratch"
  echo "move_into_place reports a failed rename as a failure, called the way its guarded call sites call it."
}

# assert_nginx_vhost_is_validated_before_it_is_served: step 80 must refuse to finish an install
# with a panel vhost nginx cannot load, and must finish one with a vhost it can.
#
# The defect this is written against, so that it cannot come back quietly: step 80 used to write
# the render to `maran.conf.staging` and then run `nginx -t`. Both families include
# `conf.d/*.conf` and nothing else, which `.staging` does not match — so the test parsed a tree
# the candidate was not in, printed "test is successful", and the step renamed a file nothing had
# ever read over the served path. A vhost with a syntax error installed clean and took the panel
# down at the next reload, with the successful `nginx -t` in the install log as evidence that it
# was fine. Measured on both polygon images before the fix.
#
# Both halves are asserted, because from the outside a gate that refuses everything and a gate
# that refuses nothing look the same, and only the pair tells them apart. That is also why the
# refusal cases require step 80 to NAME the directive nginx rejected: a refusal for some other
# reason — a missing binary, an absent group — would otherwise be read as the gate working.
#
# The third proposition, added after the fix for the defect above introduced a failure the defect
# did not have: an install KILLED inside the swap window leaves the served path exactly as it
# was. The step validates after the rename, which is the correct order and the one
# ops::safe_write uses, and the price of that order is a window in which a file no one has
# validated is the file nginx would read. A rollback written only on the paths that return does
# not close it; a trap does. Case 3 is the only check here that can tell the two apart.
#
# Case 4 is its other half, and it is about the one signal no trap catches. SIGKILL leaves the
# candidate on the served path and the last validated vhost under `.previous`; the step cannot
# prevent that, so what is asserted instead is that the NEXT run prefers that leftover copy to
# the file on the served path. The step used to delete it and back up the broken file in its
# place, which turned an interrupted install into a permanently broken one at the next refusal.
#
# The fourth: on every path that refuses, the nginx unit is neither enabled, reloaded nor
# restarted. This used to be read off the enablement file alone while the conclusion claimed the
# service was never touched — so a `systemctl reload nginx` inserted straight after the rename,
# and the step's own reload line moved inside the window, both passed green. The polygon's
# systemctl records a reload now, and nginx_service_records reads all three records under BOTH
# spellings of the unit: with the short name alone, the same mutants spelled `nginx.service` were
# green again, which is the same defect wearing a suffix.
#
# The fifth, and the reason this file grew three cases in its third round: the restoration is only
# worth having if what it restores has been checked. Case 4b truncates `<dest>.previous` — the
# ordinary outcome of a hard reset during the copy — and requires the step to refuse to serve it,
# to say so, and to leave a host on which nginx still starts. Case 1b interrupts the step with the
# SHIPPED template and requires exit 143, which is the only case here that can tell a guard that
# re-raises from one that restores and then carries on to report the panel reachable. Case 1c fails
# the step BEFORE the swap and requires the served path not to change at all, because a guard armed
# before the write must not put a rollback copy onto a file nothing has written to.
#
# It runs LAST in main: it is the only assertion here that writes into /etc/nginx and creates a
# system account, and putting it after the uninstaller cases keeps it out of the state they save
# and restore.
assert_nginx_vhost_is_validated_before_it_is_served() {
  local dest entry_copy="" template_backup outcome

  prepare_host_for_the_nginx_step

  dest="$(run_nginx_step 'nginx_conf_dest')"
  case "$dest" in
    /etc/nginx/*.conf) ;;
    *)
      fail "80-nginx.sh's nginx_conf_dest answered '${dest}'. Every case below drives the step by that
answer, and a destination that is not a .conf under nginx's own tree is a vhost nginx never reads."
      ;;
  esac

  # The host is left exactly as it was found. The image reaches here with no panel vhost, and the
  # two negative cases need to start from that state deliberately rather than by luck.
  if [ -e "$dest" ]; then
    entry_copy="$(mktemp)"
    cp -p "$dest" "$entry_copy"
  fi
  rm -f "$dest"

  template_backup="$(mktemp)"
  cp -p "$PANEL_VHOST" "$template_backup"

  outcome="$(nginx_gate_outcome "$dest")"

  # Put both back before anything is reported, so a failure here does not also break every check
  # that reads the shipped template afterwards.
  cp -p "$template_backup" "$PANEL_VHOST"
  rm -f "$template_backup"
  rm -f "$dest"
  if [ -n "$entry_copy" ]; then
    cp -p "$entry_copy" "$dest"
    rm -f "$entry_copy"
  fi

  [ -z "$outcome" ] || fail "$outcome"
  grep -q '__MARAN_API_SOCKET__' "$PANEL_VHOST" \
    || fail "this assertion did not put the shipped vhost template back the way it found it"
  nginx -t >/dev/null 2>&1 \
    || fail "this assertion did not put ${dest} back the way it found it; nginx no longer loads:
$(nginx -t 2>&1)"

  echo "Step 80 installs the shipped panel vhost, nginx confirms it reads the file that was installed,"
  echo "and a vhost nginx cannot load is refused — on a first install, over a working one, and when the"
  echo "step is killed in the middle of its own validation — with the previous configuration restored"
  echo "byte-for-byte, the step dying of the signal it was sent rather than resuming, and the nginx unit"
  echo "neither enabled, reloaded nor restarted, all three read from the polygon's systemctl under both"
  echo "spellings of the unit. A failure before the swap leaves the served path untouched. After a"
  echo "SIGKILL, which no trap can catch, the run that follows rolls back to the copy that was validated"
  echo "rather than to the one that was not — and refuses to serve that copy at all when it does not"
  echo "parse, leaving a host on which nginx still starts and saying which file it declined to install."
  echo "The vhost it installs is fsynced, and so is its directory, before the validation that commits"
  echo "the install — observed through a recording sync and nginx, in order. Whose file is whose is"
  echo "decided by the marker the step's own render stamps and not by the state of the host, so a"
  echo "re-install on a host some OTHER conf.d file has already broken still leaves the panel vhost it"
  echo "found byte-for-byte where it was, an operator's own backup under the step's working name"
  echo "survives an install that succeeds, and a directory under one of those names is moved aside"
  echo "rather than ending a successful install non-zero. That marker answers for BYTES and not for a"
  echo "name: a symlink under one of those names is parked rather than followed, so the served path is"
  echo "never a link out of the directory this step owns and what the link pointed at is left alone;"
  echo "and a file that is nothing but the marker — whose empty body has a perfectly valid digest — is"
  echo "parked too, rather than served as a panel vhost with no 'listen' line in it. A link whose target"
  echo "is GONE is parked on the same terms, at the working name and on the served path alike: a test"
  echo "that resolves the link answers about a target that is not there, so a dangling one used to be"
  echo "renamed over and lost without a word."
}

# THE PANEL VHOST'S ACCESS LOG, AND THE SECRET IT USED TO CARRY.
#
# The defect, measured rather than reasoned about
# (docs/superpowers/notes/2026-09-11-setup-token-in-a-url-threat-note.md): the vhost named
# `access_log <path>` with no format, so nginx used its built-in `combined`, which logs
# `"$request"` — the raw request line, query string included. The installer printed the operator a
# link reading `/setup?token=<48 hex characters>`, and that token is permission to become this
# server's administrator until the first one exists. Opening the link therefore wrote a live
# credential into /var/log/maran/nginx-access.log: a file inside a directory every uid in the
# panel's service group may read, which nothing rotated. rules/security.md item 8 forbids exactly
# that, by name, and two earlier passes over this branch recorded that it did not happen.
#
# WHY A CENSUS OVER `access_log` DIRECTIVES RATHER THAN A CHECK ON THE ONE THAT LEAKED. There is
# one access_log directive in the vhost today. A check written against that one directive is a
# check that stops being about anything the moment a `location` is given a log of its own — and a
# location with its own log is exactly how a per-endpoint log gets added, which is how this leak
# would come back. So every directive in the file is enumerated and each must either be `off` or
# name a format this file declares, and every format the file declares is enumerated and none may
# carry a variable that contains a query string. A directive added tomorrow is named by this
# assertion or it satisfies it; there is no third outcome, and neither of them is silence.
#
# AND WHY IT IS ALSO BEHAVIOURAL. The census is a fact about text, and what matters is a fact
# about nginx: rules/testing.md, a check must observe what it reports on. The variable list could
# be right and the claim still false — `$uri` is query-free and is REWRITTEN by `try_files`, so a
# format built on it logs `/index.html` for every SPA route and a reader would conclude the log
# names the page that was asked for when it does not. That was measured here, not assumed, and it
# is why the vhost maps the path out of `$request_uri` instead. So this assertion serves two real
# requests through this family's real nginx, with this family's real vhost, and reads the file
# nginx wrote.
#
# THE CONTROL RUNS FIRST AND IN THE OTHER DIRECTION. Before the shipped vhost is measured, the
# same two requests are served through a copy of it with the format name removed from the
# access_log line — which is, byte for byte, what shipped before this fix — and the token MUST
# appear. Without that half, "the token is not in the log" is satisfied by a probe that cannot see
# a token at all, by an nginx that never started, or by a request that never arrived. And in the
# other direction the shipped run requires the request to STILL BE THERE: a log that stopped
# recording /setup would pass a check for the token's absence while destroying the record an
# operator investigating an intrusion needs. Neither half is worth having without the other.
#
# WHAT THIS CANNOT SEE, stated rather than left to be discovered. nginx's ERROR log has no format
# and cannot be given one: for any request that errors, nginx writes `request: "<the raw request
# line>"` into it, query string and all. Nothing in this file, and nothing in the vhost, can close
# that — which is why the setup token no longer travels in a URL at all
# (installer/lib/90-finish.sh, held by the census in
# assert_the_finish_message_tells_the_truth_about_this_install) and why the access-log format is
# the second line of defence rather than the fix. This assertion also says nothing about a
# customer's site vhosts, which the agent renders from its own templates.
assert_the_panel_vhost_can_never_log_a_query_string() {
  local dest entry_copy="" served_copy outcome root_backup="" document_root port
  local ancestor created_dirs=()

  # The census first: it reads one file, touches no service, and reports the cheapest failure
  # before nginx is asked for anything.
  outcome="$(panel_vhost_log_census)"
  [ -z "$outcome" ] || fail "$outcome"

  prepare_host_for_the_nginx_step
  dest="$(run_nginx_step 'nginx_conf_dest')"

  # The host is left exactly as it was found, the same discipline
  # assert_nginx_vhost_is_validated_before_it_is_served follows: this writes the served path.
  if [ -e "$dest" ]; then
    entry_copy="$(mktemp)"
    cp -p "$dest" "$entry_copy"
  fi
  rm -f "$dest"

  # The installer's own step puts the vhost there — rendered by its renderer, validated by its
  # gate — because a vhost this script wrote itself would be a second authority and the census
  # above would be checking a file nginx never serves.
  run_nginx_step 'step_nginx' >/dev/null 2>&1 \
    || fail "step 80 refused to install the shipped panel vhost, so nothing below could be served. Run
assert_nginx_vhost_is_validated_before_it_is_served for the named reason."
  forget_nginx_service_records

  served_copy="$(mktemp)"
  cp -p "$dest" "$served_copy"

  # The SPA's document root, read out of the vhost rather than written here: a root this script
  # spelled itself would answer 404 for /setup the day the vhost moved, and a 404 is logged by the
  # ERROR log with the full request line — the probe would then measure the wrong file and read as
  # a leak the access log does not have.
  document_root="$(sed 's/^[[:space:]]*#.*//' "$PANEL_VHOST" \
    | awk '$1 == "root" { sub(/;$/, "", $2); print $2; exit }')"
  case "$document_root" in
    /*) ;;
    *) fail "no absolute 'root' directive was found in ${PANEL_VHOST} (read: '${document_root}'). The probe
below serves /setup out of that directory; without it every request 404s and this assertion would be
measuring nginx's error path instead of its access log." ;;
  esac
  if [ -e "${document_root}/index.html" ]; then
    root_backup="$(mktemp)"
    cp -p "${document_root}/index.html" "$root_backup"
  fi
  # Every directory the probe is about to create — the root and each missing parent — is recorded
  # first, deepest first, so the cleanup below removes exactly those and the image is left as it was
  # found. A later part (parts/release-layout.sh) refuses to run when /usr/local/maran/frontend exists.
  ancestor="$document_root"
  while [ "$ancestor" != "/" ] && [ ! -e "$ancestor" ]; do
    created_dirs+=("$ancestor")
    ancestor="$(dirname "$ancestor")"
  done
  install -d -m 0755 "$document_root"
  printf '<!doctype html><title>polygon</title>\n' > "${document_root}/index.html"

  port="$(installer_value MARAN_PANEL_PORT "$INSTALLER_ENTRY_POINT")"

  outcome="$(panel_vhost_log_outcome "$dest" "$served_copy" "$port")"

  # Everything back before anything is reported, so a failure here does not also break the
  # assertions that follow.
  nginx -s quit >/dev/null 2>&1 || true
  cp -p "$served_copy" "$dest"
  rm -f "$served_copy"
  rm -f "${document_root}/index.html"
  if [ -n "$root_backup" ]; then
    cp -p "$root_backup" "${document_root}/index.html"
    rm -f "$root_backup"
  fi
  # Only what this assertion created, and only while empty: rmdir refuses anything else.
  for ancestor in "${created_dirs[@]}"; do
    rmdir "$ancestor" 2>/dev/null || true
  done
  rm -f "$VHOST_LOG_ACCESS_LOG" "$VHOST_LOG_ERROR_LOG"
  rm -f "$dest"
  if [ -n "$entry_copy" ]; then
    cp -p "$entry_copy" "$dest"
    rm -f "$entry_copy"
  fi
  forget_nginx_service_records

  [ -z "$outcome" ] || fail "$outcome"
  nginx -t >/dev/null 2>&1 \
    || fail "this assertion did not put ${dest} back the way it found it; nginx no longer loads:
$(nginx -t 2>&1)"

  echo "The panel vhost declares a log format of its own and every access_log directive in it was"
  echo "enumerated: each names one of this file's own query-free formats, and no format the file declares"
  echo "carries a variable that holds a query string. Served through this family's real nginx, a request"
  echo "for /setup carrying a secret in its query string, and one carrying a secret in the query string of its"
  echo "Referer, are logged WITHOUT either secret and WITH the path,"
  echo "and the same two requests through a copy of the vhost with the format name removed — what shipped"
  echo "before this fix — put the secret in the log, so the probe can see the leak it reports absent."
}

# assert_the_file_transfer_location_carries_the_configured_body_limit: the upload and download
# location of the file manager renders its client_max_body_size from the panel.env variable the
# panel reads (Files__MaxUploadBytes), and renders a bad value as the default instead of as text.
#
# Five renders of the shipped template through step 80's own render_vhost, each against a panel.env
# written for the case: no variable (the default plus the allowance), a larger value (the number
# follows the file - the inverse control that stops a renderer that ignores panel.env from passing),
# and three values that must NOT reach the vhost as they are written: one above the panel's own
# ceiling, one carrying a unit and one carrying a second directive. The number is read out of the
# file-transfer location's own block, not grepped from the whole file, because the server-wide 64m
# also matches a naive search and would pass every case.
#
# UNOBSERVED HERE: nginx's own parsed value. The rendered file is the one step 80 installs (the
# assertion in sftp.sh compares them byte for byte), but this reads its text, not `nginx -T`; a
# directive nginx rejects is caught by the validation gate in
# assert_nginx_vhost_is_validated_before_it_is_served, which parses the same template.
assert_the_file_transfer_location_carries_the_configured_body_limit() {
  local env_file rendered case_name configured expected got default_limit
  default_limit=$((104857600 + 1048576))

  env_file="$(mktemp)"
  rendered="$(mktemp)"

  while read -r case_name configured expected; do
    if [ "$configured" = "-" ]; then
      : > "$env_file"
    else
      printf 'Files__MaxUploadBytes=%s\n' "$configured" > "$env_file"
    fi
    if ! run_nginx_step "render_vhost '${rendered}'" '' "$env_file" >/dev/null 2>&1; then
      rm -f "$env_file" "$rendered"
      fail "step 80's render_vhost failed for the case '${case_name}', so the file-transfer limit could not be read."
    fi
    got="$(awk '
      /^[[:space:]]*location[[:space:]]+~[[:space:]]+\^\/api\/v1\/files\/\(upload\|download\)\$/ { inside = 1 }
      inside && $1 == "client_max_body_size" { sub(/;$/, "", $2); print $2; exit }
    ' "$rendered")"
    if [ "$expected" = "default" ]; then
      expected="$default_limit"
    fi
    if [ "$got" != "$expected" ]; then
      rm -f "$env_file" "$rendered"
      fail "the file-transfer location rendered client_max_body_size '${got}' for the case '${case_name}'
(Files__MaxUploadBytes='${configured}'), expected '${expected}'. An empty answer means the location is
missing or no longer starts with 'location ~ ^/api/v1/files/(upload|download)\$'."
    fi
    grep -q 'client_max_body_size 64m;' "$rendered" \
      || { rm -f "$env_file" "$rendered"; fail "the server-wide client_max_body_size 64m is gone for the case '${case_name}'."; }
    grep -q '__MARAN_FILES_BODY_LIMIT__' "$rendered" \
      && { rm -f "$env_file" "$rendered"; fail "the case '${case_name}' left the __MARAN_FILES_BODY_LIMIT__ placeholder in the vhost."; }
  done <<'CASES'
unset - default
larger 200000000 201048576
toolarge 268435457 default
unit 100m default
injection 1048576;client_max_body_size default
CASES

  rm -f "$env_file" "$rendered"

  echo "The file-transfer location renders client_max_body_size from panel.env's Files__MaxUploadBytes plus"
  echo "a fixed allowance, and renders the default for an absent variable, a value above the panel's own"
  echo "ceiling, a value carrying a unit and a value carrying a second directive. A larger configured"
  echo "value moves the number, so a renderer that ignored panel.env could not have passed. The"
  echo "server-wide 64m is untouched. What this does not observe is nginx's own parsed value (nginx -T);"
  echo "see the function's comment."
}

# assert_the_account_move_locations_carry_the_package_limits: the three locations of an account move (issue #127)
# are present in the vhost step 80 renders, the upload's client_max_body_size is exactly the package ceiling the
# panel enforces plus the room for the two signed documents ahead of the artifact, the transfer locations switch
# nginx's buffering off, and the general location still carries the server-wide 64m (the upload limit is not
# widened for any other path).
#
# The number is checked against its parts, not against itself: 256 GiB (MigrationPackageLimits.MaxArtifactBytes)
# plus 4 MiB, computed here from those two figures, so a change to either side that forgets the other fails
# here and not on the first large upload. The block is read out of the rendered file by its own opening line, never
# grepped from the whole file, because the 64m also matches a naive search.
#
# Inverse control: the same reader is run against a copy of the vhost whose upload block was given the 64m, and
# must report the difference; a reader that always agreed would pass the shipped file for the wrong reason.
#
# UNOBSERVED HERE: nginx's own parsed value (nginx -T) and any real request. The rendered file is the one step 80
# installs, and the validation gate in assert_nginx_vhost_is_validated_before_it_is_served parses the same
# template, so a directive nginx rejects is caught there; what is not measured is a multi-gigabyte body through a
# real nginx, which the real-host suite for the export-to-import path names as its own blind spot.
assert_the_account_move_locations_carry_the_package_limits() {
  local rendered env_file expected got block control
  expected=$((256 * 1024 * 1024 * 1024 + 4 * 1024 * 1024))

  env_file="$(mktemp)"
  rendered="$(mktemp)"
  : > "$env_file"
  if ! run_nginx_step "render_vhost '${rendered}'" '' "$env_file" >/dev/null 2>&1; then
    rm -f "$env_file" "$rendered"
    fail "step 80's render_vhost failed, so the account-move locations could not be read."
  fi

  block_of() {
    awk -v opening="$2" '
      index($0, opening) { inside = 1 }
      inside { print }
      inside && /^[[:space:]]*}[[:space:]]*$/ { exit }
    ' "$1"
  }

  block="$(block_of "$rendered" 'location = /api/v1/account-imports {')"
  [ -n "$block" ] || { rm -f "$env_file" "$rendered"; fail "the vhost has no 'location = /api/v1/account-imports {' block, so the package upload would meet the server-wide 64m limit."; }
  got="$(printf '%s\n' "$block" | awk '$1 == "client_max_body_size" { sub(/;$/, "", $2); print $2; exit }')"
  if [ "$got" != "$expected" ]; then
    rm -f "$env_file" "$rendered"
    fail "the package upload location rendered client_max_body_size '${got}', expected ${expected} (256 GiB, MigrationPackageLimits.MaxArtifactBytes, plus 4 MiB)."
  fi
  printf '%s\n' "$block" | grep -q 'proxy_request_buffering off;' \
    || { rm -f "$env_file" "$rendered"; fail "the package upload location does not switch proxy_request_buffering off, so nginx would spool the whole package to the system disk."; }

  block="$(block_of "$rendered" 'location = /api/v1/account-imports/pull {')"
  [ -n "$block" ] || { rm -f "$env_file" "$rendered"; fail "the vhost has no pull location, so the request that waits for a whole package would meet the general 300s read timeout."; }
  printf '%s\n' "$block" | grep -q 'proxy_read_timeout 14400s;' \
    || { rm -f "$env_file" "$rendered"; fail "the pull location's proxy_read_timeout is not 14400s."; }

  block="$(block_of "$rendered" 'location ~ ^/api/v1/(account-exports/[0-9a-fA-F-]+/package|migration-pull/(head|package))$ {')"
  [ -n "$block" ] || { rm -f "$env_file" "$rendered"; fail "the vhost has no package download location."; }
  printf '%s\n' "$block" | grep -q 'proxy_buffering off;' \
    || { rm -f "$env_file" "$rendered"; fail "the package download location does not switch proxy_buffering off, so nginx would spool a second plaintext copy of the package."; }

  grep -q 'client_max_body_size 64m;' "$rendered" \
    || { rm -f "$env_file" "$rendered"; fail "the server-wide client_max_body_size 64m is gone."; }
  [ "$(grep -c 'client_max_body_size 274882101248;' "$rendered")" = "1" ] \
    || { rm -f "$env_file" "$rendered"; fail "the package ceiling appears in more than the one upload location, so another path was widened."; }

  # The inverse control: the reader must SEE a vhost whose upload block carries the wrong number.
  control="$(mktemp)"
  sed 's/client_max_body_size 274882101248;/client_max_body_size 64m;/' "$rendered" > "$control"
  got="$(block_of "$control" 'location = /api/v1/account-imports {' | awk '$1 == "client_max_body_size" { sub(/;$/, "", $2); print $2; exit }')"
  rm -f "$env_file" "$rendered" "$control"
  [ "$got" = "64m" ] \
    || fail "the control (an upload block given 64m) was read as '${got}', so this assertion could not have seen a wrong limit."

  echo "The vhost step 80 renders carries the account-move locations: the package upload accepts exactly 256 GiB plus"
  echo "4 MiB with request buffering off, the pull waits as long as a transfer, the package downloads switch response"
  echo "buffering off, the server-wide 64m is untouched and the package ceiling appears in one location only. A copy"
  echo "with the 64m in the upload block is read as 64m, so the reader can see a wrong limit. UNOBSERVED HERE: nginx's"
  echo "own parsed value and a real multi-gigabyte request; see the function's comment."
}

# panel_vhost_log_census: prints what is wrong with the vhost's logging directives, or nothing.
#
# A predicate rather than a series of `fail` calls, so that the propositions stay readable and so
# that the vacuity guards sit beside the checks they protect. Whole-line comments are stripped
# first and the file is then read as one line: every directive in nginx's configuration language
# ends in `;` and may be wrapped across as many lines as it likes, which the shipped log_format is.
panel_vhost_log_census() {
  local block name variable directive format
  local declared="" formats_seen=0 logs_seen=0
  local flattened
  flattened="$(sed 's/^[[:space:]]*#.*//' "$PANEL_VHOST" | tr '\n' ' ')"

  # Every format the file declares, and the variables in it. The deny list is the set of nginx
  # variables that contain a query string: `$request` is the raw request line, `$request_uri` the
  # original target, and `$args`/`$query_string`/`$is_args` the query itself. `$request_method` and
  # `$request_time` are not in it and must not be — which is why the comparison below is against
  # whole variable names and not a substring match, the mistake that would ban the method too.
  while IFS= read -r block; do
    [ -n "$block" ] || continue
    formats_seen=$((formats_seen + 1))
    name="$(printf '%s' "$block" | awk '{print $2}')"
    case "$name" in
      [A-Za-z_]*) ;;
      *) echo "a log_format in ${PANEL_VHOST} declares no usable name (read: '${name}'). Every access_log
directive below is checked against the names declared here, and an unnamed format makes that
comparison meaningless."
         return 0 ;;
    esac
    while IFS= read -r variable; do
      case "$variable" in
        '$request'|'$request_uri'|'$args'|'$query_string'|'$is_args'|'$http_referer')
          echo "log_format ${name} in ${PANEL_VHOST} logs ${variable}, which carries a query string (the
request's own, or for \$http_referer the one of the page that made the request). A secret an operator is handed in a URL — a one-time setup token, a reset link, a signed
download — then reaches /var/log/maran/nginx-access.log, which outlives the install and is readable by
every uid in the panel's service group (rules/security.md item 8: never in logs, never in URLs)."
          return 0 ;;
      esac
    done < <(printf '%s\n' "$block" | grep -oE '\$[A-Za-z_][A-Za-z0-9_]*' || true)
    declared="${declared} ${name}"
  done < <(printf '%s\n' "$flattened" | grep -oE 'log_format[[:space:]]+[^;]*;' || true)

  # Every access_log directive, against those names. A directive with a path and NO format is the
  # defect itself: nginx falls back to the built-in `combined`, which logs `"$request"`.
  while IFS= read -r directive; do
    [ -n "$directive" ] || continue
    logs_seen=$((logs_seen + 1))
    format="$(printf '%s' "$directive" | sed 's/;[[:space:]]*$//' | awk '{print $3}')"
    case "$format" in
      "")
        case "$(printf '%s' "$directive" | sed 's/;[[:space:]]*$//' | awk '{print $2}')" in
          off) continue ;;
        esac
        echo "${PANEL_VHOST} has '${directive}' — an access log with no format named, which is nginx's
built-in 'combined', which logs \"\$request\" and therefore the query string. Name one of this file's own
query-free formats, or 'off'. This is the exact shape that wrote the installer's one-time setup token
into /var/log/maran/nginx-access.log (rules/security.md item 8)."
        return 0 ;;
    esac
    printf '%s\n' $declared | grep -qxF -- "$format" \
      || { echo "${PANEL_VHOST} has '${directive}', naming a log format '${format}' that this file does not
declare. Either it is a typo, in which case nginx refuses the whole configuration, or the format lives
somewhere this assertion cannot read and cannot check for a query string."
           return 0; }
  done < <(printf '%s\n' "$flattened" | grep -oE 'access_log[[:space:]]+[^;]*;' || true)

  # The two vacuity guards, and they are the point of writing this as a census. A file with no
  # access_log directive at all, or with none of its own formats, satisfies both loops above by
  # having nothing in them — and a check that agrees with everything is the failure rules/testing.md
  # names, not a pass.
  [ "$logs_seen" -gt 0 ] \
    && { [ "$formats_seen" -gt 0 ] || { echo "${PANEL_VHOST} names ${logs_seen} access_log directive(s) and declares no log_format of its
own, so the loop that checks formats for query-string variables checked nothing."; return 0; }; }
  [ "$logs_seen" -gt 0 ] \
    || { echo "no access_log directive was found anywhere in ${PANEL_VHOST}, so this census agreed with an
empty list. Either the panel vhost no longer logs — which is a decision, not an accident, and belongs in
this assertion — or the directive is spelled in a way this reader cannot see."
         return 0; }
  # The header half of issue #465: the document that issues the requests must tell the browser to
  # send no Referer at all. Read from the comment-stripped text, so a mention in prose cannot satisfy
  # it, and checked for the exact policy: `same-origin` is the value that leaked.
  printf '%s\n' "$flattened" | grep -qE 'add_header[[:space:]]+Referrer-Policy[[:space:]]+"no-referrer"[[:space:]]+always[[:space:]]*;' \
    || { echo "${PANEL_VHOST} does not send 'Referrer-Policy: no-referrer' (always). With any weaker policy
a browser names the full address of an SPA page, query string included, as the Referer of every request
the page makes, and a reset or invitation link carries its token in that query string (issue #465)."
         return 0; }
  # Nothing wrong. This predicate always returns zero and says what it found on stdout, because a
  # non-zero status from inside a command substitution is what `set -e` acts on: the caller would
  # exit with no message at all, which is the one failure mode a gate must never have.
  return 0
}

# panel_vhost_log_outcome: serves two real requests twice — once through a vhost with the format
# name stripped from its access_log line, once through the shipped one — and prints what went
# wrong, or nothing.
#
# THE TWO REQUESTS. The first carries a secret in its query string, the way the installer's old
# setup link did. The second carries none and is otherwise identical, and it is what makes the
# grep for the secret a discriminating one: it must be logged too, and its line must NOT match,
# so a probe that matched anything would be caught by the very run that is supposed to be clean.
panel_vhost_log_outcome() {
  local dest="$1" shipped="$2" port="$3" logged

  # The leaking vhost first: the shipped file with the format name taken off the access_log line,
  # which is character-for-character what this repository served before this fix.
  sed -E 's#^([[:space:]]*access_log[[:space:]]+[^ ;]+)[[:space:]]+[A-Za-z_][A-Za-z0-9_]*;#\1;#' \
    "$shipped" > "$dest"
  cmp -s "$dest" "$shipped" \
    && { printf '%s' "the control vhost is identical to the shipped one, so the run below cannot tell a logged
secret from an unlogged one. The access_log line no longer has the shape this sed strips."; return 0; }
  logged="$(panel_vhost_request_pair "$port")" || { printf '%s' "$logged"; return 0; }
  case "$logged" in
    *"$VHOST_LOG_PROBE_SECRET"*"$VHOST_LOG_PROBE_REFERER_SECRET"*|*"$VHOST_LOG_PROBE_REFERER_SECRET"*"$VHOST_LOG_PROBE_SECRET"*) ;;
    *) printf '%s' "served through a vhost whose access_log names NO format — nginx's built-in 'combined' —
the query string '${VHOST_LOG_PROBE_SECRET}' and the Referer '${VHOST_LOG_PROBE_REFERER_SECRET}' did not
both appear in ${VHOST_LOG_ACCESS_LOG}. That is the leak this assertion exists to keep closed, and this control is how it knows it can see it: without the secret
appearing here, the shipped run below proves only that nothing was logged at all. What the log holds:
${logged}"
       return 0 ;;
  esac

  # And now the shipped one.
  cp -p "$shipped" "$dest"
  logged="$(panel_vhost_request_pair "$port")" || { printf '%s' "$logged"; return 0; }
  case "$logged" in
    *"$VHOST_LOG_PROBE_REFERER_SECRET"*)
      printf '%s' "the shipped panel vhost logged the Referer '${VHOST_LOG_PROBE_REFERER_SECRET}' into
${VHOST_LOG_ACCESS_LOG}. A browser names the full address of an SPA page, query string included, as
the Referer of every request that page makes, so a reset or invitation link reaches a file that outlives
the request and is readable by every uid in the panel's service group (rules/security.md item 8, issue
#465). What the log holds:
${logged}"
      return 0 ;;
  esac
  case "$logged" in
    *"$VHOST_LOG_PROBE_SECRET"*)
      printf '%s' "the shipped panel vhost logged the query string '${VHOST_LOG_PROBE_SECRET}' into
${VHOST_LOG_ACCESS_LOG}. A secret handed to an operator in a URL therefore reaches a file that outlives
the install and is readable by every uid in the panel's service group (rules/security.md item 8). What
the log holds:
${logged}"
      return 0 ;;
  esac
  # The other direction, and the reason the absence above means anything: the request is still on
  # the record. A vhost that had simply stopped logging would satisfy the check above, and it would
  # cost the operator investigating an intrusion the one file that says the setup page was reached.
  case "$logged" in
    *"$VHOST_LOG_PROBE_PATH"*) ;;
    *) printf '%s' "the shipped panel vhost logged no line naming '${VHOST_LOG_PROBE_PATH}' at all. The secret
is absent from ${VHOST_LOG_ACCESS_LOG} because nothing was recorded, not because the format omits it —
and an access log that does not say the setup page was reached is a record an operator investigating an
intrusion no longer has. What the log holds:
${logged}"
       return 0 ;;
  esac
  # Twice, because two requests were made: a format that logged only one of them would still name
  # the path. This is what tells "the query string was dropped" apart from "the request carrying a
  # query string was dropped".
  case "$(printf '%s\n' "$logged" | grep -cF -- "$VHOST_LOG_PROBE_PATH")" in
    2) ;;
    *) printf '%s' "the shipped panel vhost logged $(printf '%s\n' "$logged" | grep -cF -- "$VHOST_LOG_PROBE_PATH") line(s) naming
'${VHOST_LOG_PROBE_PATH}' and two requests were made for it — one with a query string and one without.
A log that keeps the request without the query string and drops the request that had one hides exactly
the requests worth investigating. What the log holds:
${logged}"
       return 0 ;;
  esac
  # Nothing wrong; zero for the reason panel_vhost_log_census gives.
  return 0
}

# panel_vhost_restart_nginx: stops the nginx this assertion started, if any, and starts it again on
# the vhost now on the served path. Non-zero, with the reason on stdout, when nginx would not start -
# which must never be read as "the thing probed was absent".
panel_vhost_restart_nginx() {
  local status
  if nginx -s quit >/dev/null 2>&1; then
    # nginx does not exit synchronously on `quit`; it finishes its connections first, and starting
    # a second master while the first still holds the port fails on `bind()`. Waited for by the pid
    # file it removes on the way out, rather than by a sleep long enough to look safe.
    #
    # `-s` and not `-e`: these images reach here with an EMPTY /run/nginx.pid left behind by the
    # build's own `nginx -t`, so a test for the file's existence waits the whole bound out on every
    # probe and then starts nginx anyway. Measured in the image, as five seconds per request pair.
    local waited=0
    while [ -s /run/nginx.pid ] && [ "$waited" -lt 50 ]; do
      waited=$((waited + 1))
      sleep 0.1
    done
  fi
  status=0
  nginx || status=$?
  [ "$status" -eq 0 ] \
    || { printf '%s' "nginx refused to start on the vhost under test (exit ${status}), so no request was served and
nothing below observed anything:
$(nginx -t 2>&1)"; return 1; }
  return 0
}

# panel_vhost_request_pair: (re)starts nginx on the vhost now on the served path, makes the two
# probe requests, and prints the access log. Non-zero, with the reason on stdout, when the server
# or the requests did not happen — which must never be read as "the secret was not logged".
panel_vhost_request_pair() {
  local port="$1"

  : > "$VHOST_LOG_ACCESS_LOG"
  : > "$VHOST_LOG_ERROR_LOG"
  panel_vhost_restart_nginx || return 1

  # -k because the certificate is the self-signed one step 80 generates; the probe is about the log
  # line, not about trust. --fail is deliberately NOT passed: a non-2xx answer is still a logged
  # request and the checks above are about the log, so a refusal here must be visible as a missing
  # line rather than as a dead probe.
  curl -sk -o /dev/null "https://127.0.0.1:${port}${VHOST_LOG_PROBE_PATH}?token=${VHOST_LOG_PROBE_SECRET}" \
    || { printf '%s' "curl could not reach https://127.0.0.1:${port}${VHOST_LOG_PROBE_PATH} on this container, so the
secret was never sent and its absence from the log means nothing."; return 1; }
  curl -sk -o /dev/null -H "Referer: https://127.0.0.1:${port}/reset-password?token=${VHOST_LOG_PROBE_REFERER_SECRET}" \
    "https://127.0.0.1:${port}${VHOST_LOG_PROBE_PATH}" \
    || { printf '%s' "curl reached the panel with a query string and not without one, which leaves this assertion
no control request. https://127.0.0.1:${port}${VHOST_LOG_PROBE_PATH}"; return 1; }

  # WAIT for the two lines rather than reading once. curl returns when the RESPONSE is complete;
  # nginx writes its access-log entry after finishing the request, and with the write buffered the
  # line is regularly not there yet. Measured 2026-09-23: commit b2d6a7a passed this assertion on
  # dev and failed it on main — the same tree, the same image, a different machine's timing — and
  # the failure read "the shipped panel vhost logged no line naming '/setup' at all", which is the
  # sentence this assertion prints when the leak it hunts is absent. A flaky assertion that fails
  # with the words of a real finding is worse than no assertion: it teaches its reader to disbelieve
  # it.
  #
  # Bounded, and the bound is a failure rather than a fall-through: if the lines never arrive, the
  # caller still reads an empty log and says so, exactly as before. The same shape as the pid-file
  # wait above — wait for the observation, never sleep a length that looks safe.
  vhost_log_waited=0
  while [ "$(grep -cF -- "$VHOST_LOG_PROBE_PATH" "$VHOST_LOG_ACCESS_LOG" 2>/dev/null || echo 0)" -lt 2 ] \
        && [ "$vhost_log_waited" -lt 50 ]; do
    vhost_log_waited=$((vhost_log_waited + 1))
    sleep 0.1
  done

  cat "$VHOST_LOG_ACCESS_LOG"
  return 0
}

# THE MODULE FRAME LOCATION'S OWN HEADERS, AND THE DENY THE REST OF THE PANEL KEEPS (issue #185).
#
# A packaged module's screen runs in a sandboxed iframe whose document the API serves from
# /module-frames/, so that one location must be framable while everything else stays unframable. nginx
# makes that a property of HOW `add_header` is inherited, not of any single line: a location that
# declares no add_header of its own inherits the server's (X-Frame-Options DENY, a CSP with
# `frame-ancestors 'none'`), and a location that declares one inherits NONE of them. Two defects hide
# in that rule and a check on either response alone is blind to one of them:
#   * the frame location drops its own set -> it inherits DENY and every module screen is blank;
#   * DENY is dropped from the server level to "fix" the first -> the frame loads, and so does the
#     whole panel inside anyone's page (clickjacking on every administrative screen).
# So the assertion reads BOTH responses of the SERVED vhost and runs two inverse controls: a vhost whose
# frame location lost its own headers MUST be reported for it, and a vhost with DENY removed everywhere
# MUST be reported for /api/, with the frame location's good headers NOT reported in that run.
#
# UNOBSERVED HERE, and printed by the assertion: (1) the API's own Content-Security-Policy and its
# `frame-ancestors 'self'` - the polygon has no API behind the socket, so nginx answers 502 itself and
# the headers read are exactly the ones nginx adds (`always` makes them ride on a 502); the API's half is
# the Host tests' (ModuleFrameHeaders); (2) a browser actually framing the document - the shell's
# Playwright specs; (3) any nginx >= 1.25.1 parse of this file, as in the vhost's own comment.

# panel_vhost_location_block: prints the directives inside one top-level `location <path> {` of a
# vhost file, comments removed, or nothing when the location is absent. The vhost has no nested
# braces inside a location, so the first closing brace on a line of its own ends the block.
panel_vhost_location_block() {
  sed 's/^[[:space:]]*#.*//' "$1" | awk -v want="$2" '
    $1 == "location" && $2 == want && $3 == "{" { inside = 1; next }
    inside && $1 == "}" { exit }
    inside { print }
  '
}

# panel_vhost_frame_census: prints what is wrong with the text of the frame location, or nothing.
# A fact about the file only; the served answer is read by panel_vhost_frame_outcome.
panel_vhost_frame_census() {
  local block
  block="$(panel_vhost_location_block "$PANEL_VHOST" /module-frames/)"
  [ -n "$block" ] \
    || { echo "${PANEL_VHOST} has no 'location /module-frames/ {' block (or it is spelled so this reader cannot see it), so a packaged module's frame has no location of its own."; return 0; }
  local header
  for header in 'Strict-Transport-Security' 'X-Content-Type-Options' 'Referrer-Policy'; do
    printf '%s\n' "$block" | grep -qE "^[[:space:]]*add_header[[:space:]]+${header}[[:space:]].*always;" \
      || { echo "the /module-frames/ location of ${PANEL_VHOST} does not declare '${header}' with 'always' itself. Declaring ANY add_header there replaces the server-level set, so a header not repeated is a header the frame responses go without."; return 0; }
  done
  printf '%s\n' "$block" | grep -qiE 'X-Frame-Options|frame-ancestors|Content-Security-Policy' \
    && { echo "the /module-frames/ location of ${PANEL_VHOST} sets X-Frame-Options, a frame-ancestors or a Content-Security-Policy. The panel's own iframe could not load under DENY or 'none', and the API's frame CSP must pass through unchanged."; return 0; }
  sed 's/^[[:space:]]*#.*//' "$PANEL_VHOST" | grep -qE '^[[:space:]]*add_header[[:space:]]+X-Frame-Options[[:space:]]+"DENY"[[:space:]]+always;' \
    || { echo "${PANEL_VHOST} no longer sends 'X-Frame-Options: DENY' at server level, so /api/ and the SPA are framable by any page."; return 0; }
  return 0
}

# panel_vhost_probe_headers: <port> <path>. Prints the response header block of one request to the
# served vhost. Non-zero, with the reason on stdout, when the request did not happen - never an empty
# answer that a caller could read as "no such header".
panel_vhost_probe_headers() {
  local port="$1" path="$2" headers
  # -k: the certificate is the self-signed one step 80 generates. No --fail: a 502 from an absent API
  # is the expected answer and still carries the `always` headers.
  headers="$(curl -sk -o /dev/null -D - --max-time 10 "https://127.0.0.1:${port}${path}")" \
    || { printf '%s' "curl could not reach https://127.0.0.1:${port}${path} on this container, so the headers of that location were never read."; return 1; }
  printf '%s\n' "$headers"
}

# panel_vhost_frame_findings: <frame headers> <api headers>. Prints one finding per line, each starting
# with a code the controls match on, or nothing. Pure text in, text out, so the controls can feed it
# the answers of vhosts that are meant to be wrong.
panel_vhost_frame_findings() {
  local frame api
  frame="$(printf '%s\n' "$1" | tr -d '\r' | tr 'A-Z' 'a-z')"
  api="$(printf '%s\n' "$2" | tr -d '\r' | tr 'A-Z' 'a-z')"

  # The vacuity guard on the axis that can go blind: an answer with no status line means nothing was
  # served, and every "header is absent" below would then be true of a dead server.
  printf '%s\n' "$frame" | head -n 1 | grep -q '^http/' || echo "PROBE-NO-ANSWER the /module-frames/ request returned no HTTP status line."
  printf '%s\n' "$api" | head -n 1 | grep -q '^http/' || echo "PROBE-NO-ANSWER the /api/ request returned no HTTP status line."

  printf '%s\n' "$frame" | grep -q '^strict-transport-security:' || echo "FRAME-MISSING-HSTS the frame response carries no Strict-Transport-Security."
  printf '%s\n' "$frame" | grep -qE '^x-content-type-options: *nosniff' || echo "FRAME-MISSING-NOSNIFF the frame response carries no 'X-Content-Type-Options: nosniff'."
  printf '%s\n' "$frame" | grep -qE '^referrer-policy: *no-referrer' || echo "FRAME-MISSING-REFERRER the frame response carries no 'Referrer-Policy: no-referrer'."
  printf '%s\n' "$frame" | grep -q '^x-frame-options:' && echo "FRAME-HAS-X-FRAME-OPTIONS the frame response carries X-Frame-Options, so the panel's own iframe cannot load."
  printf '%s\n' "$frame" | grep -qE "^content-security-policy:.*frame-ancestors 'none'" && echo "FRAME-HAS-FRAME-ANCESTORS-NONE the frame response carries the shell's frame-ancestors 'none'."

  printf '%s\n' "$api" | grep -qE '^x-frame-options: *deny' || echo "API-LACKS-DENY the /api/ response carries no 'X-Frame-Options: DENY'."
  printf '%s\n' "$api" | grep -qE "^content-security-policy:.*frame-ancestors 'none'" || echo "API-LACKS-FRAME-ANCESTORS-NONE the /api/ response carries no CSP with frame-ancestors 'none'."
  return 0
}

# panel_vhost_frame_probe: <port>. Restarts nginx on the vhost now on the served path and prints the
# findings of the two real requests. Non-zero, with the reason on stdout, when nothing was served.
panel_vhost_frame_probe() {
  local port="$1" frame api
  panel_vhost_restart_nginx || return 1
  frame="$(panel_vhost_probe_headers "$port" /module-frames/polygon/1.0.0/)" || { printf '%s' "$frame"; return 1; }
  api="$(panel_vhost_probe_headers "$port" /api/v1/polygon-frame-probe)" || { printf '%s' "$api"; return 1; }
  panel_vhost_frame_findings "$frame" "$api"
}

# panel_vhost_frame_outcome: <served path> <shipped copy> <port>. Runs the two inverse controls and then
# the shipped vhost, and prints what went wrong, or nothing.
panel_vhost_frame_outcome() {
  local dest="$1" shipped="$2" port="$3" findings

  # Control A: the frame location without its own headers. It must be reported for X-Frame-Options.
  awk '
    $1 == "location" && $2 == "/module-frames/" { inside = 1 }
    inside && $1 == "add_header" { next }
    inside && $1 == "}" { inside = 0 }
    { print }
  ' "$shipped" > "$dest"
  cmp -s "$dest" "$shipped" \
    && { printf '%s' "control A is identical to the shipped vhost: the awk no longer finds the frame location's add_header lines, so the run below cannot tell a frame location that dropped its headers from one that kept them."; return 0; }
  findings="$(panel_vhost_frame_probe "$port")" || { printf '%s' "$findings"; return 0; }
  case "$findings" in
    *FRAME-HAS-X-FRAME-OPTIONS*) ;;
    *) printf '%s' "control A (the frame location with its add_header lines removed, so it inherits the server's) was NOT reported for X-Frame-Options. The probe cannot see an inherited DENY, so a clean answer for the shipped vhost proves nothing. Findings: ${findings:-none}"
       return 0 ;;
  esac

  # Control B: DENY removed from every place it is declared. It must be reported for /api/, and the
  # frame location's own good headers must NOT be reported in the same run.
  grep -vE '^[[:space:]]*add_header[[:space:]]+X-Frame-Options' "$shipped" > "$dest"
  cmp -s "$dest" "$shipped" \
    && { printf '%s' "control B is identical to the shipped vhost: no add_header X-Frame-Options line was removed."; return 0; }
  findings="$(panel_vhost_frame_probe "$port")" || { printf '%s' "$findings"; return 0; }
  case "$findings" in
    *API-LACKS-DENY*) ;;
    *) printf '%s' "control B (X-Frame-Options removed from the whole vhost) was NOT reported for /api/. A vhost that dropped DENY everywhere would pass this assertion. Findings: ${findings:-none}"
       return 0 ;;
  esac
  case "$findings" in
    *FRAME-*) printf '%s' "control B reported the frame location, whose headers it did not touch, so the two findings are not independent. Findings: ${findings}"
              return 0 ;;
  esac

  # The shipped vhost.
  cp -p "$shipped" "$dest"
  findings="$(panel_vhost_frame_probe "$port")" || { printf '%s' "$findings"; return 0; }
  [ -z "$findings" ] \
    || { printf '%s' "the shipped panel vhost, served by this family's nginx, answers wrongly:
${findings}"; return 0; }
  return 0
}

# assert_the_module_frame_location_has_its_own_headers_and_api_keeps_deny: the frame location answers
# with its own complete header set and without DENY, and /api/ still answers DENY - read from the vhost
# step 80 installed at the path nginx serves, not from the template. See the comment block above.
assert_the_module_frame_location_has_its_own_headers_and_api_keeps_deny() {
  local dest entry_copy="" served_copy outcome port

  outcome="$(panel_vhost_frame_census)"
  [ -z "$outcome" ] || fail "$outcome"

  prepare_host_for_the_nginx_step
  dest="$(run_nginx_step 'nginx_conf_dest')"

  # The host is left exactly as it was found, the discipline the log assertion follows.
  if [ -e "$dest" ]; then
    entry_copy="$(mktemp)"
    cp -p "$dest" "$entry_copy"
  fi
  rm -f "$dest"

  run_nginx_step 'step_nginx' >/dev/null 2>&1 \
    || fail "step 80 refused to install the shipped panel vhost, so nothing below could be served. Run
assert_nginx_vhost_is_validated_before_it_is_served for the named reason."
  forget_nginx_service_records

  served_copy="$(mktemp)"
  cp -p "$dest" "$served_copy"
  port="$(installer_value MARAN_PANEL_PORT "$INSTALLER_ENTRY_POINT")"

  outcome="$(panel_vhost_frame_outcome "$dest" "$served_copy" "$port")"

  # Everything back before anything is reported.
  nginx -s quit >/dev/null 2>&1 || true
  cp -p "$served_copy" "$dest"
  rm -f "$served_copy"
  rm -f "$VHOST_LOG_ACCESS_LOG" "$VHOST_LOG_ERROR_LOG"
  rm -f "$dest"
  if [ -n "$entry_copy" ]; then
    cp -p "$entry_copy" "$dest"
    rm -f "$entry_copy"
  fi
  forget_nginx_service_records

  [ -z "$outcome" ] || fail "$outcome"
  nginx -t >/dev/null 2>&1 \
    || fail "this assertion did not put ${dest} back the way it found it; nginx no longer loads:
$(nginx -t 2>&1)"

  echo "The panel vhost, as step 80 installed it and this family's nginx serves it, answers a request under"
  echo "/module-frames/ with Strict-Transport-Security, nosniff and no-referrer and WITHOUT X-Frame-Options or a"
  echo "frame-ancestors 'none', and answers a request under /api/ with X-Frame-Options DENY and the shell's CSP."
  echo "Two inverse controls were reported first: a vhost whose frame location lost its own headers (reported for"
  echo "the inherited X-Frame-Options) and a vhost with DENY removed everywhere (reported for /api/, with the"
  echo "frame location not reported). UNOBSERVED HERE: the API's own CSP and its frame-ancestors 'self' (no API"
  echo "behind the socket in this image: nginx answers 502 itself, and its 'always' headers are what was read;"
  echo "the API half is the Host tests'), a browser actually framing the document (the shell's Playwright specs),"
  echo "and any nginx >= 1.25.1 parse of this file."
}

# web_server_group_for_this_family: the group install.sh's own detect_web_server_identity decides.
#
# EXTRACTED from install.sh and run, rather than copied here. install.sh ends in `main "$@"` and
# cannot be sourced, and a family-to-group table written in this file would be a second authority —
# exactly the thing assert_panel_port_has_one_authority exists to prevent. MARAN_OS_FAMILY is set
# by the Dockerfile RUN that executes this script, which is what makes the answer this family's.
web_server_group_for_this_family() {
  bash -c 'set -euo pipefail
eval "$(sed -n "/^detect_web_server_identity()/,/^}/p" "$1")"
detect_web_server_identity
printf "%s" "$MARAN_WEB_SERVER_GROUP"' _ "$INSTALLER_ENTRY_POINT"
}

# run_services_step: runs step 70's code the way install.sh runs it — a child shell with
# `set -euo pipefail`, the step file sourced, and the values install.sh decides exported first.
#
# A CHILD, for the reason run_installer_step gives at length: `exit` and `set -e` mean different
# things inside this script's `if` than they do in the installer, and step 70 aborts with `exit 1`.
run_services_step() {
  local socket_path="$1" web_group="$2" snippet="$3"
  MARAN_API_SOCKET_PATH="$socket_path" \
    MARAN_WEB_SERVER_GROUP="$web_group" \
    MARAN_USER="$POLYGON_SERVICE_USER" \
    MARAN_GROUP="$POLYGON_SERVICE_GROUP" \
    LIB_DIR="$INSTALLER_LIB" \
    bash -c 'set -euo pipefail
. "$1"
. "$2"
eval "$3"' _ "$COMMON_LIB" "$SERVICES_STEP" "$snippet"
}

# assert_the_panel_acme_block_is_a_file_of_its_own_that_a_suspended_host_keeps_out: the panel's port-80
# ACME block is installed as conf.d/maran-port80.conf, apart from the panel vhost, so that the agent can
# take it out of nginx's tree while Apache is the site web server (issue #517). Apache owns port 80 then,
# and a nginx still binding it would stop Apache starting.
#
# Four propositions, each with its inverse so that none can be satisfied by a step that does nothing:
#   1. step 80 installs the block and the panel vhost holds NO port-80 listen (nginx binds 8443 alone
#      once the block is suspended);
#   2. the installed block binds port 80 (the inverse of 1: the listen did not vanish, it moved);
#   3. with the block parked at `.suspended`, as the agent leaves it, a re-run of the step does NOT
#      put it back - an installer update on an Apache host must not take port 80 from Apache;
#   4. with the park removed, the same re-run DOES write it (the inverse of 3).
#
# UNOBSERVED HERE: Apache itself. The agent's side of the move is covered by the unit tests of
# maran_ops::apps; this proves the installer's half of the contract.
assert_the_panel_acme_block_is_a_file_of_its_own_that_a_suspended_host_keeps_out() {
  local vhost block parked vhost_copy="" block_copy=""

  prepare_host_for_the_nginx_step
  vhost="$(run_nginx_step 'nginx_conf_dest')"
  block="$(run_nginx_step 'echo "$MARAN_NGINX_PORT80_CONF"')"
  parked="$(run_nginx_step 'echo "${MARAN_NGINX_PORT80_CONF}${MARAN_PORT80_SUSPENDED_SUFFIX}"')"
  case "$block" in
    /etc/nginx/*.conf) ;;
    *) fail "80-nginx.sh names the port-80 block '${block}', which is not a .conf under nginx's tree; nginx would never read it." ;;
  esac
  [ "$parked" = "${block}.suspended" ] \
    || fail "the parked name of the port-80 block is '${parked}', not '${block}.suspended'; it must be a name nginx never opens, and the agent spells the same one."

  if [ -e "$vhost" ]; then vhost_copy="$(mktemp)"; cp -p "$vhost" "$vhost_copy"; fi
  if [ -e "$block" ]; then block_copy="$(mktemp)"; cp -p "$block" "$block_copy"; fi
  rm -f "$vhost" "$block" "$parked"

  run_nginx_step 'step_nginx' >/dev/null 2>&1 \
    || fail "step 80 refused to install the panel vhost and its port-80 block."
  forget_nginx_service_records

  [ -f "$block" ] || fail "step 80 did not install the port-80 ACME block at ${block}."
  grep -Eq '^[[:space:]]*listen[[:space:]]+(\[::\]:)?80;' "$block" \
    || fail "${block} does not bind port 80, so the ACME challenge for the panel's name has no listener."
  if grep -Eq '^[[:space:]]*listen[[:space:]]+(\[::\]:|[0-9.]+:)?80([[:space:]]|;)' "$vhost"; then
    fail "the installed panel vhost ${vhost} still binds port 80; in apache mode nginx must bind the panel port only."
  fi

  mv "$block" "$parked"
  run_nginx_step 'step_nginx' >/dev/null 2>&1 \
    || fail "step 80 refused a re-run while the port-80 block was parked."
  forget_nginx_service_records
  [ ! -e "$block" ] \
    || fail "a re-run of step 80 wrote the port-80 block back while it was parked for Apache, which would take port 80 from it."

  rm -f "$parked"
  run_nginx_step 'step_nginx' >/dev/null 2>&1 \
    || fail "step 80 refused a re-run with nothing parked."
  forget_nginx_service_records
  [ -f "$block" ] \
    || fail "with nothing parked, a re-run of step 80 did not write the port-80 block; the previous check could not tell a skipped write from a broken one."

  rm -f "$vhost" "$block" "$parked"
  [ -z "$vhost_copy" ] || { cp -p "$vhost_copy" "$vhost"; rm -f "$vhost_copy"; }
  [ -z "$block_copy" ] || { cp -p "$block_copy" "$block"; rm -f "$block_copy"; }
  nginx -t >/dev/null 2>&1 \
    || fail "this assertion did not put the nginx tree back the way it found it:
$(nginx -t 2>&1)"

  echo "The panel's port-80 ACME block is its own file (conf.d/maran-port80.conf) and the panel vhost binds no port 80."
  echo "A block parked at .suspended, as the agent leaves it for Apache, is not written back by a re-run of step 80;"
  echo "with the park removed the same re-run writes it, so the first result is not a step that writes nothing."
}

# assert_the_packaged_default_port_80_server_is_disabled_and_restorable: the distribution's own default
# `listen 80` server in /etc/nginx/nginx.conf would hold port 80 whatever Maran's files say, so Apache -
# which owns 80/443 once it is the site web server (issue #517) - could not start. Step 80 comments that
# block out line by line (prefix "# maran-disabled: "), and uninstall.sh strips exactly the prefix.
#
# Three parts, each with its inverse so that none can pass on a stock file that never had the block:
#   1. a synthetic nginx.conf with a port-80 server, a panel-port server, an `8080` server and a
#      commented-out port-80 server: only the first is disabled (the others are the controls), the
#      prefix-stripped result equals the input byte for byte, and a second pass changes nothing;
#   2. this family's REAL stock nginx.conf: after the render no active `listen` on port 80 remains,
#      and on the RHEL family the stock file DID have one (the control that the render removed something);
#   3. the installer's own step function on the real file: nginx accepts the result, `nginx -T` shows no
#      listener on 80, and the original is put back at the end.
#
# UNOBSERVED HERE: the openSUSE stock file (this image set has a leap16 lane that runs the same
# assertion; if its stock file has a port-80 server part 2 proves it is disabled, and if it has none the
# render is a no-op, which part 1 proves is harmless).
assert_the_packaged_default_port_80_server_is_disabled_and_restorable() {
  local work conf="/etc/nginx/nginx.conf" copy rendered active_before
  local prefix="# maran-disabled: "
  prepare_host_for_the_nginx_step
  work="$(mktemp -d)"
  # Active (non-comment) listens on port 80 in a file, one per line.
  active_port80() {
    sed 's/#.*//' "$1" | grep -E 'listen[[:space:]]+(\[::\]:|[0-9.*]+:)?80([[:space:];])' || true
  }

  cat > "${work}/synthetic.conf" <<'CONF'
events {}
http {
    include mime.types;
    server {
        listen 80;
        listen [::]:80;
        server_name _;
        location / { return 200; }
    }
    server {
        listen 8443 ssl;
        server_name panel;
    }
    server {
        listen 8080;
        server_name other;
    }
    # server {
    #     listen 80;
    # }
}
CONF
  rendered="${work}/rendered.conf"
  run_nginx_step "render_nginx_conf_without_packaged_port80_server '${work}/synthetic.conf'" > "$rendered" \
    || fail "step 80's render of a synthetic nginx.conf failed."
  [ -z "$(active_port80 "$rendered")" ] \
    || fail "the synthetic port-80 server is still active after the render:
$(cat "$rendered")"
  grep -q 'listen 8443 ssl;' "$rendered" && grep -q 'listen 8080;' "$rendered" \
    || fail "the render disabled a server that does not listen on port 80 (8443 or 8080); the pattern is too wide."
  [ -n "$(active_port80 "${work}/synthetic.conf")" ] \
    || fail "the synthetic input has no active port-80 listen; this check would prove nothing."
  awk -v prefix="$prefix" 'index($0, prefix) == 1 { print substr($0, length(prefix) + 1); next } { print }' "$rendered" \
    | cmp -s - "${work}/synthetic.conf" \
    || fail "stripping the prefix from the render did not give the input back byte for byte; uninstall could not restore it."
  run_nginx_step "render_nginx_conf_without_packaged_port80_server '${rendered}'" | cmp -s - "$rendered" \
    || fail "a second render of the disabled file changed it; the step is not idempotent."

  [ -f "$conf" ] || fail "${conf} does not exist on this image."
  active_before="$(active_port80 "$conf")"
  if [ "${MARAN_OS_FAMILY:-}" = rhel ] && [ -z "$active_before" ]; then
    fail "the RHEL family's stock ${conf} has no active port-80 server on this image, so part 2 would prove nothing; the assumption behind issue #517's port-80 split is wrong."
  fi

  copy="${work}/nginx.conf.original"
  cp -p "$conf" "$copy"
  rm -f "${conf}.maran-previous"
  run_nginx_step 'disable_packaged_port80_server_in_nginx_conf' >/dev/null 2>&1 \
    || { cp -p "$copy" "$conf"; fail "step 80's disable_packaged_port80_server_in_nginx_conf refused this image's nginx.conf."; }
  nginx -t >/dev/null 2>&1 || { cp -p "$copy" "$conf"; fail "nginx rejects ${conf} after the packaged port-80 server was disabled:
$(nginx -t 2>&1)"; }
  if nginx -T 2>/dev/null | sed 's/#.*//' | grep -Eq 'listen[[:space:]]+(\[::\]:|[0-9.*]+:)?80([[:space:];])'; then
    cp -p "$copy" "$conf"; rm -f "${conf}.maran-previous"
    fail "nginx still has a listener on port 80 after step 80 disabled the packaged server:
$(nginx -T 2>/dev/null | sed 's/#.*//' | grep -E 'listen')"
  fi
  if [ -n "$active_before" ]; then
    cmp -s "${conf}.maran-previous" "$copy" \
      || { cp -p "$copy" "$conf"; fail "the pristine copy ${conf}.maran-previous is not the original nginx.conf."; }
  fi

  cp -p "$copy" "$conf"
  rm -f "${conf}.maran-previous"
  nginx -t >/dev/null 2>&1 || fail "this assertion did not put ${conf} back the way it found it:
$(nginx -t 2>&1)"
  rm -rf "$work"
  unset -f active_port80

  echo "A packaged http-level server with an active port-80 listen is commented out line by line; servers on other ports,"
  echo "an 8080 listener and a commented-out block are left alone, the prefix strips back to the original byte for byte, and a"
  echo "second pass changes nothing. On this image's real nginx.conf nginx loads the result and has no listener on port 80."
}
