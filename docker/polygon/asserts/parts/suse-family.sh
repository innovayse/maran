#!/usr/bin/env bash
# the SUSE family's own obligations: one firewall owner, nginx reading the directory the vhost is
# written to, and FTPS and mail skipped in plain words
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in. main() calls these only when
# this image is the SUSE family; nothing here is a check of the other two.
#
# NOTHING about openSUSE was measured by us. Every fact these assertions lean on is UNVERIFIED (the
# sentences the installer prints, the vhost directory, the way `nft list table` spells a port) and is
# read from the installer as it is in this tree, never from a template of ours. The first run on a
# real Leap image is what turns each of them into a fact or into a named failure.
#
# What a green run here does NOT mean, said once and repeated in the output: this image boots no
# systemd and has no firewall kernel state, so "firewalld is masked", "table inet maran is in force"
# and "nginx serves the vhost" are observed on the served artefacts only by the INSTALL polygon
# (scripts/lib/install-polygon-suse.sh), which runs a real init. What is observed HERE is the logic
# of the steps that produce them, against the files and the web server this image really has.

# The listing the stub nft prints for `list table inet maran`, and where it reads it from. A literal
# path in /tmp, removed by the assertion that creates it.
readonly SUSE_NFT_LISTING="/tmp/maran-polygon-nft-listing"
# The real nft is moved aside to this name while the stub stands in for it, and moved back.
readonly SUSE_NFT_ASIDE="/usr/sbin/nft.polygon-real"
readonly SUSE_NFT_PATH="/usr/sbin/nft"

# suse_nft_stub_install: puts a stand-in at /usr/sbin/nft (the path the firewall step executes) that
# answers `list table inet maran` from SUSE_NFT_LISTING and hands every other invocation to the real
# binary. The real one is MOVED, not copied, so the restore is a rename and the binary is the same
# inode it was.
suse_nft_stub_install() {
  [ ! -e "$SUSE_NFT_ASIDE" ] \
    || fail "${SUSE_NFT_ASIDE} is already here, so a previous assertion did not put nft back. Refusing to stack a second stand-in on top of it"
  mv -- "$SUSE_NFT_PATH" "$SUSE_NFT_ASIDE"
  printf '%s\n' '#!/bin/sh' \
    'if [ "$1 $2 $3 $4" = "list table inet maran" ]; then cat /tmp/maran-polygon-nft-listing; exit 0; fi' \
    "exec ${SUSE_NFT_ASIDE} \"\$@\"" > "$SUSE_NFT_PATH"
  chmod 0755 "$SUSE_NFT_PATH"
}

# suse_nft_stub_remove: puts the real nft back and proves it is the real one, by asking it for its
# version through the path the step uses.
suse_nft_stub_remove() {
  rm -f -- "$SUSE_NFT_PATH" "$SUSE_NFT_LISTING"
  mv -- "$SUSE_NFT_ASIDE" "$SUSE_NFT_PATH"
  "$SUSE_NFT_PATH" --version >/dev/null \
    || fail "${SUSE_NFT_PATH} does not run after the assertion put it back"
}

# suse_ssh_gate: runs the firewall step's require_ssh_ports_in_loaded_ruleset in a child, with
# Firewall__SshPorts set to $1 (omitted entirely when empty), and prints "<status> <output>" on one
# line. A child, for the reason run_installer_step gives: the step aborts with `exit`.
suse_ssh_gate() {
  local ports="$1" output status=0
  if [ -n "$ports" ]; then
    printf 'Firewall__SshPorts=%s\nFirewall__PanelPort=8443\n' "$ports" > /etc/maran/panel.env
  else
    printf 'Firewall__PanelPort=8443\n' > /etc/maran/panel.env
  fi
  output="$(run_installer_step 'require_ssh_ports_in_loaded_ruleset' 2>&1)" || status=$?
  printf '%s %s' "$status" "$(printf '%s' "$output" | tr '\n' ' ')"
}

# assert_suse_retires_firewalld_only_when_ssh_is_in_the_loaded_ruleset: the guard that stands between
# the operator's own SSH session and the removal of the only firewall that was protecting the host.
#
# Decision 220 makes the agent's nftables the one firewall owner on this family, and the installer
# retires firewalld only after it has READ the loaded table and found the SSH port in it. Five cases,
# the refusing ones each paired with the acceptance that shows the gate can say yes:
#   1. the real nft in a container with no table loaded: refused (the ordinary state of this image);
#   2. a listing that names the port as a whole word: accepted (the inverse control);
#   3. a listing that holds the port only inside a longer number (2222 for 22): refused, because a
#      substring match would let a closed port through;
#   4. two ports, one of them missing from the listing: refused;
#   5. no Firewall__SshPorts at all: refused, naming the missing list.
# The stub nft in cases 2 to 4 is FABRICATED text. What is exercised is the step's matching logic;
# how the real `nft list table` spells a rule (a bare number, a set, a range) is UNVERIFIED and is
# observed only by the install polygon.
assert_suse_retires_firewalld_only_when_ssh_is_in_the_loaded_ruleset() {
  local panel_env="/etc/maran/panel.env" result status text
  if [ -e "$panel_env" ] || [ -e "$SUSE_NFT_LISTING" ]; then
    fail "${panel_env} or ${SUSE_NFT_LISTING} already exists. This assertion writes and deletes those exact paths, so it refuses to run rather than destroy a real installation's file"
  fi
  install -d -m 0750 /etc/maran

  # 1. The real nft, which has no table `inet maran` here.
  result="$(suse_ssh_gate 22)"; status="${result%% *}"; text="${result#* }"
  [ "$status" -ne 0 ] \
    || { rm -f "$panel_env"; fail "require_ssh_ports_in_loaded_ruleset ACCEPTED a host with no 'table inet maran' loaded, so firewalld would be retired with nothing in force: ${text}"; }
  case "$text" in
    *"firewalld is left running"*) ;;
    *) rm -f "$panel_env"; fail "the gate refused, but not by saying firewalld is left running (so the operator cannot tell why): ${text}" ;;
  esac

  suse_nft_stub_install

  # 2. The inverse control: the same gate, a listing that names 22 as a whole word.
  printf 'table inet maran {\n\tchain input {\n\t\ttcp dport { 22, 8443 } accept\n\t}\n}\n' > "$SUSE_NFT_LISTING"
  result="$(suse_ssh_gate 22)"; status="${result%% *}"; text="${result#* }"
  if [ "$status" -ne 0 ]; then
    suse_nft_stub_remove; rm -f "$panel_env"
    fail "the gate REFUSED a listing that names port 22 as a whole word, so its refusals in this assertion come from a gate that refuses everything: ${text}"
  fi
  case "$text" in
    *"may be retired"*) ;;
    *) suse_nft_stub_remove; rm -f "$panel_env"; fail "the gate accepted port 22 but did not say firewalld may be retired: ${text}" ;;
  esac

  # 3. 2222 is not 22.
  printf 'table inet maran {\n\tchain input {\n\t\ttcp dport { 2222, 8443 } accept\n\t}\n}\n' > "$SUSE_NFT_LISTING"
  result="$(suse_ssh_gate 22)"; status="${result%% *}"; text="${result#* }"
  if [ "$status" -eq 0 ]; then
    suse_nft_stub_remove; rm -f "$panel_env"
    fail "the gate ACCEPTED a listing that holds 22 only inside 2222: a substring match would retire firewalld while the SSH port is closed: ${text}"
  fi

  # 4. Two ports, one missing.
  printf 'table inet maran {\n\tchain input {\n\t\ttcp dport { 22, 8443 } accept\n\t}\n}\n' > "$SUSE_NFT_LISTING"
  result="$(suse_ssh_gate 22,2200)"; status="${result%% *}"; text="${result#* }"
  if [ "$status" -eq 0 ]; then
    suse_nft_stub_remove; rm -f "$panel_env"
    fail "the gate ACCEPTED a list of two SSH ports when only one is in the loaded table, so a session on the other would be cut: ${text}"
  fi

  # 5. No list at all.
  result="$(suse_ssh_gate '')"; status="${result%% *}"; text="${result#* }"
  suse_nft_stub_remove
  rm -f "$panel_env"
  [ "$status" -ne 0 ] \
    || fail "the gate ACCEPTED a host whose panel.env names no SSH ports, so it could not have known which session to protect: ${text}"
  case "$text" in
    *"refusing to disable firewalld"*) ;;
    *) fail "the gate refused an empty SSH port list but not in the words that name the problem: ${text}" ;;
  esac

  echo "On the SUSE family firewalld is retired only after the loaded table names every SSH port as a whole word: refused with no table, accepted for 22 in { 22, 8443 }, refused for 22 inside 2222, refused when 2200 is missing from { 22, 8443 }, refused with no port list."
  echo "UNOBSERVED HERE: how the real 'nft list table inet maran' spells an accepted port (the listing in cases 2 to 4 is fabricated text served by a stand-in at ${SUSE_NFT_PATH}); the step's matching is exercised, nft's output grammar is not."
}

# assert_suse_masks_firewalld_when_the_unit_exists_and_says_what_it_did: mask_firewalld_unit against
# the two host states it must tell apart, with the polygon's systemctl recording them.
#
# The mask ITSELF is not observed here: the stand-in's catch-all accepts `mask` and writes nothing, so
# this proves the step took the branch for the state it was given and reported it. That firewalld is
# MASKED (a link to /dev/null, `systemctl is-enabled` answering `masked`) is observed on a real init by
# scripts/lib/install-polygon-suse.sh, and the assertion says so rather than letting a green line here
# read as that observation.
assert_suse_masks_firewalld_when_the_unit_exists_and_says_what_it_did() {
  local output
  firewalld_state present
  output="$(run_installer_step 'mask_firewalld_unit' 2>&1)" \
    || { rm -rf "$UNIT_STATE_DIRECTORY"; fail "mask_firewalld_unit failed on a host that has firewalld: ${output}"; }
  case "$output" in
    *"firewalld.service masked"*) ;;
    *) rm -rf "$UNIT_STATE_DIRECTORY"; fail "mask_firewalld_unit did not say it masked a firewalld that is there: ${output}" ;;
  esac
  firewalld_state absent
  output="$(run_installer_step 'mask_firewalld_unit' 2>&1)" \
    || { rm -rf "$UNIT_STATE_DIRECTORY"; fail "mask_firewalld_unit failed on a host with no firewalld: ${output}"; }
  rm -rf "$UNIT_STATE_DIRECTORY"
  case "$output" in
    *"No firewalld unit on this host; nothing to mask."*) ;;
    *) fail "mask_firewalld_unit did not say there was nothing to mask on a host with no firewalld: ${output}" ;;
  esac
  echo "mask_firewalld_unit masks a firewalld that exists and says so, and says there is nothing to mask where there is none."
  echo "UNOBSERVED HERE: the mask itself. The polygon's systemctl accepts 'mask' and records nothing, so 'masked' is observed only by the install polygon on a real init (scripts/lib/install-polygon-suse.sh: is-enabled answering masked, the link to /dev/null)."
}

# assert_suse_nginx_reads_the_directory_the_vhost_is_written_to: nginx's own answer to "do you read
# this?" for the directory step 80 writes the panel vhost into on this family.
#
# UNVERIFIED: that openSUSE's stock nginx.conf does not include conf.d/*.conf. Whatever it does,
# after `ensure_conf_d_include_in_nginx_conf` a file in /etc/nginx/conf.d must appear in `nginx -T`'s
# list of the files it loaded, because a vhost in a directory nothing includes passes `nginx -t` and
# is never served. The positive control is a file planted in conf.d; the inverse control is a file in
# a directory nothing includes, which must NOT be listed; and nginx.conf itself must be listed, so the
# search is shown able to see a header at all.
assert_suse_nginx_reads_the_directory_the_vhost_is_written_to() {
  local probe="/etc/nginx/conf.d/zz-polygon-probe.conf" off_tree="/etc/nginx/polygon-not-included" listing before after
  if [ -e "$probe" ] || [ -e "$off_tree" ]; then
    fail "${probe} or ${off_tree} already exists. This assertion writes and deletes those exact paths"
  fi
  run_nginx_step 'ensure_conf_d_include_in_nginx_conf' >/dev/null 2>&1 \
    || fail "ensure_conf_d_include_in_nginx_conf failed on this family's nginx.conf. If the stock file shapes its http block in a way the step does not understand, the panel vhost has no directory nginx reads"
  before="$(cat /etc/nginx/nginx.conf)"
  run_nginx_step 'ensure_conf_d_include_in_nginx_conf' >/dev/null 2>&1 \
    || fail "ensure_conf_d_include_in_nginx_conf failed on its second run"
  after="$(cat /etc/nginx/nginx.conf)"
  [ "$before" = "$after" ] \
    || fail "a second ensure_conf_d_include_in_nginx_conf run changed nginx.conf, so the include is appended once per install"

  : > "$probe"
  mkdir -p "$off_tree"
  : > "${off_tree}/zz.conf"
  listing="$(nginx -T 2>&1 | grep -F '# configuration file ' || true)"
  rm -f "$probe"
  rm -rf "$off_tree"
  case "$listing" in
    *"# configuration file /etc/nginx/nginx.conf:"*) ;;
    *) fail "'nginx -T' lists no header for nginx.conf, so this search cannot see which files nginx loaded and its other answers below mean nothing" ;;
  esac
  case "$listing" in
    *"# configuration file ${probe}:"*) ;;
    *) fail "'nginx -T' does not list ${probe} after the include step ran, so nginx would never serve a vhost written to /etc/nginx/conf.d (listed: $(printf '%s' "$listing" | tr '\n' ' ' | cut -c1-300))" ;;
  esac
  case "$listing" in
    *"${off_tree}/zz.conf"*) fail "'nginx -T' lists a file in ${off_tree}, which nothing includes, so its listing of conf.d does not mean what this assertion takes it to mean" ;;
  esac
  echo "nginx loads files from /etc/nginx/conf.d on this family once the installer's include is in place: 'nginx -T' lists a file planted there, lists nginx.conf, and does not list a file in a directory nothing includes; a second run of the include step changes nothing."
  echo "UNOBSERVED HERE: that the stock nginx.conf lacked the include before the step (the image's step ran first, and the premise is UNVERIFIED); and the served answer, which only a running nginx on a real init gives (scripts/lib/install-polygon-suse.sh)."
}

# suse_ftps_footprint: everything step 89 would create for FTPS, as one comparable text. Asked of the
# host (a group lookup through the C library, stat on the paths, the unit directory's listing) and not
# of the step.
suse_ftps_footprint() {
  printf 'group:%s\n' "$(getent group "$MARAN_FTPS_GROUP" || true)"
  local path
  for path in "$MARAN_FTPS_PAM_SERVICE" "$MARAN_FTPS_JAIL_ROOT" "$MARAN_FTPS_CONFIG_DIR" \
    "$MARAN_FTPS_LOGROTATE_DEST" "${MARAN_FTPS_UNIT_DIR}/${MARAN_FTPS_UNIT_NAME}" "$MARAN_FTPS_PACKAGED_UNIT_PATH" /usr/sbin/vsftpd; do
    if [ -e "$path" ] || [ -L "$path" ]; then printf 'present:%s\n' "$path"; else printf 'absent:%s\n' "$path"; fi
  done
  printf 'units:%s\n' "$(ls -A -- "$MARAN_FTPS_UNIT_DIR" 2>/dev/null | sort | tr '\n' ' ')"
}

# assert_ftps_is_skipped_in_plain_words_and_touches_nothing: decision 222 on the SUSE family. FTPS is
# not offered (vsftpd is not packaged for Leap 16), so step 89's FTPS half must say so, exit 0, and
# leave no group, no directory, no PAM stack, no unit, no logrotate policy and no package behind.
#
# Four proofs, each shown able to see: the gate (`ftps_is_offered_on_family`) says no for this family
# and yes for the other two, so it is not a gate that answers the same for everyone; the footprint
# reader is shown to SEE a group planted under the real name; the step's words are matched; and the
# footprint is identical before and after.
assert_ftps_is_skipped_in_plain_words_and_touches_nothing() {
  local family before after output status=0 planted
  for family in debian rhel; do
    ( MARAN_OS_FAMILY="$family"; ftps_is_offered_on_family ) \
      || fail "ftps_is_offered_on_family says FTPS is not offered on the ${family} family, so its answer for this one proves nothing"
  done
  if ( MARAN_OS_FAMILY=suse; ftps_is_offered_on_family ); then
    fail "ftps_is_offered_on_family says FTPS IS offered on the SUSE family, which has no vsftpd package (decision 222)"
  fi

  before="$(suse_ftps_footprint)"
  case "$before" in
    *"present:"*) fail "something FTPS-shaped is already on this image before the assertion ran (${before//$'\n'/ }), so 'nothing was left behind' would be about whoever made it" ;;
  esac
  # The group is the one exception, and a stated one: the sshd assertions create ${MARAN_FTPS_GROUP}
  # through ensure_ftps_group on every family (the sshd Match block that denies it needs a member to
  # be tested), and they leave it. So its presence BEFORE the step is allowed and what is required is
  # that the step does not change it. It is also the footprint reader's positive control: when the
  # group is already there the reader is shown to see it, and when it is not, one is planted under the
  # real name and removed.
  case "$before" in
    *"group:${MARAN_FTPS_GROUP}:"*) ;;
    *)
      planted="$(groupadd "$MARAN_FTPS_GROUP" && suse_ftps_footprint; groupdel "$MARAN_FTPS_GROUP")" \
        || fail "could not plant the control group ${MARAN_FTPS_GROUP}"
      case "$planted" in
        *"group:${MARAN_FTPS_GROUP}:"*) ;;
        *) fail "the footprint reader does not see a group planted under the real name, so its answers around the step mean nothing" ;;
      esac
      ;;
  esac

  output="$(MARAN_OS_FAMILY=suse bash -c 'set -euo pipefail
. "$1"
. "$2"
. "$3"
step_ftps' _ "$COMMON_LIB" "$DEPENDENCIES_STEP" "$FTPS_STEP" 2>&1)" || status=$?
  [ "$status" -eq 0 ] \
    || fail "step_ftps exited ${status} on the SUSE family, where it must skip and succeed: ${output}"
  case "$output" in
    *"FTPS is not available on this OS family"*) ;;
    *) fail "step_ftps skipped without saying FTPS is not available on this OS family, so an operator looking for it is told nothing: ${output}" ;;
  esac
  after="$(suse_ftps_footprint)"
  [ "$before" = "$after" ] \
    || fail "step_ftps left something behind on the SUSE family. Before: ${before//$'\n'/ } After: ${after//$'\n'/ }"
  echo "FTPS is skipped on the SUSE family in the installer's words, with exit 0, and the host is unchanged: no change to the group ${MARAN_FTPS_GROUP} (the sshd assertions leave it), no PAM stack, jail root, configuration directory, logrotate policy or unit, no vsftpd.service and no /usr/sbin/vsftpd. The gate answers yes for the Debian and RHEL families and no for this one."
  echo "UNOBSERVED HERE: that vsftpd is unavailable from this family's repositories (decision 222 rests on a documentation reading; 'zypper search vsftpd' in the pinned image settles it), and the build-time FTPS assertions that exercise vsftpd, its PAM stack and the PAM witness, which do not run on this family."
}

# assert_mail_is_skipped_in_plain_words_and_touches_nothing: the same, for step 89's mail half. The
# stack is not offered on this family until measured; the step must say so, exit 0, and install, mask
# and write nothing.
#
# Observed: the gate (yes for the other two families, no for this one), the step's words, the unit
# directory's listing and the configuration directory unchanged, and none of the packages the stack
# would install present (asked of rpm, the package database).
assert_mail_is_skipped_in_plain_words_and_touches_nothing() {
  local mail_step="${INSTALLER_LIB}/89-mail.sh" family before after output status=0 package
  [ -r "$mail_step" ] || fail "${mail_step} is not in this image; the Dockerfile must COPY installer/lib/89-mail.sh"
  for family in debian rhel; do
    ( . "$mail_step"; MARAN_OS_FAMILY="$family"; mail_is_offered_on_family ) \
      || fail "mail_is_offered_on_family says mail is not offered on the ${family} family, so its answer for this one proves nothing"
  done
  if ( . "$mail_step"; MARAN_OS_FAMILY=suse; mail_is_offered_on_family ); then
    fail "mail_is_offered_on_family says mail IS offered on the SUSE family, whose mail stack is not laid out until measured"
  fi

  before="$(ls -A -- /etc/systemd/system | sort | tr '\n' ' '); config:$( [ -e /etc/maran/mail ] && echo present || echo absent )"
  case "$before" in *"config:present"*) fail "/etc/maran/mail is already on this image, so 'the step wrote nothing' would be about whoever made it" ;; esac
  for package in postfix dovecot rspamd; do
    ! rpm -q "$package" >/dev/null 2>&1 \
      || fail "${package} is already installed on this image before the mail assertion ran, so 'the step installed nothing' would be about whoever installed it"
  done
  rpm -q rpm >/dev/null 2>&1 \
    || fail "rpm does not answer for the package 'rpm' itself, so its absence answers for postfix, dovecot and rspamd above and below mean nothing"

  output="$(MARAN_OS_FAMILY=suse bash -c 'set -euo pipefail
. "$1"
. "$2"
. "$3"
step_mail' _ "$COMMON_LIB" "$DEPENDENCIES_STEP" "$mail_step" 2>&1)" || status=$?
  [ "$status" -eq 0 ] \
    || fail "step_mail exited ${status} on the SUSE family, where it must skip and succeed: ${output}"
  case "$output" in
    *"Mail is not offered on this OS family"*) ;;
    *) fail "step_mail skipped without saying mail is not offered on this OS family: ${output}" ;;
  esac
  after="$(ls -A -- /etc/systemd/system | sort | tr '\n' ' '); config:$( [ -e /etc/maran/mail ] && echo present || echo absent )"
  [ "$before" = "$after" ] \
    || fail "step_mail changed /etc/systemd/system or created /etc/maran/mail on the SUSE family (before: ${before}; after: ${after})"
  for package in postfix dovecot rspamd; do
    ! rpm -q "$package" >/dev/null 2>&1 \
      || fail "step_mail installed ${package} on the SUSE family, where it must install nothing"
  done
  echo "Mail is skipped on the SUSE family in the installer's words, with exit 0, and the host is unchanged: no unit written or masked under /etc/systemd/system, no /etc/maran/mail, no postfix, dovecot or rspamd package. The gate answers yes for the Debian and RHEL families and no for this one."
  echo "UNOBSERVED HERE: that this family's mail packages cannot be laid out (they are 'not offered until measured', which is a decision and not a finding), and the mail assertions that need the stack installed, which do not run on this family."
}
