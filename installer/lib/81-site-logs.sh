#!/usr/bin/env bash
# Step 81: move every existing site's nginx logs out of the customer's home.
#
# WHY THIS STEP EXISTS AT ALL, in one paragraph, because a migration step that
# does not say what it is undoing gets deleted by the next person who tidies up:
# a site's access and error logs used to be written to
# /home/<account>/logs/<domain>.{access,error}.log. That directory was created by
# a forked child that had dropped to the account's uid, so the CUSTOMER owned it.
# nginx's master process runs as root — it must, to bind :80/:443 — and it is the
# master that opens every access_log/error_log target, O_WRONLY|O_APPEND|O_CREAT,
# with no O_NOFOLLOW. A customer replaced their own log file with a symbolic link
# to any path root can write, and the next reload had root create that file and
# append a line whose content the customer chose, because an access-log line
# embeds the request target. One nginx serves every tenant and six site
# operations end in a reload, so any other customer's action was enough to spring
# it. It was proved against /etc/ld.so.preload on a polygon image, after which
# the dynamic loader honoured an attacker-chosen path in every process started on
# the host. See docs/superpowers/notes/2026-09-09-site-logs-threat-note.md.
#
# The agent binary this install ships renders the new location. That fixes every
# site created FROM NOW ON and nothing else: the vhosts already on disk in
# /etc/maran/nginx/sites still name a path under /home, and the next reload
# re-opens the customer's directory. Upgrading the binary alone fixes nothing,
# which is what this step is for.
#
# WHAT IT DELIBERATELY DOES NOT DO: it does not read, write, chown, move or
# delete anything under /home. Not the old log directory, not the old log files,
# not a symbolic link a customer planted there. The full argument is in §2.3 of
# the threat note; the short form is that a symlink in a customer's own home is
# only dangerous because ROOT opened it, and once nothing root runs names that
# path again it is inert — while deleting it would destroy the operator's
# evidence that somebody tried, and chowning it would be theatre, since the
# account owns ~ and re-creates the name in one command.
set -euo pipefail

# AgentPaths::SITE_LOG_ROOT. Created root:root 0750 by step 40, which also asserts it.
readonly MARAN_SITE_LOG_ROOT="/var/log/maran/sites"

# AgentPaths::NGINX_INCLUDE_DIRECTORY. The agent's own vhost directory: root-owned, written
# only by the agent, and the ONLY place this step reads from. Every value it extracts —
# account names, domains — comes out of a file root wrote into a directory no customer can
# enter, which is why no validation of them is needed beyond the shape check below.
# Named MARAN_SITE_VHOST_DIR and not MARAN_NGINX_SITES_DIR: 80-nginx.sh already declares that
# name `readonly` and run_step sources every step into the SAME shell, so re-declaring it here
# would abort the install with "readonly variable" on a line that looks like a constant.
readonly MARAN_SITE_VHOST_DIR="/etc/maran/nginx/sites"

# Where the logrotate policy this step installs lands. There was NO rotation for site logs
# before this change — `grep -rn logrotate` over the repository found only prose — so the
# logs grew until the account's disk quota stopped them. They are outside the quota now, so
# the rotation is not a tidy-up: it is the only bound that remains.
readonly MARAN_LOGROTATE_DEST="/etc/logrotate.d/maran-sites"

# assert_site_log_root: refuse to continue unless the tree this step migrates INTO is a real
# root-owned directory with the mode step 40 gave it.
#
# Step 40 already creates and asserts it, so on any ordinary run this is a second look at a
# fact that is already true. It is here anyway because this step is the one that starts
# pointing a ROOT daemon's writes at that tree, and the cost of asking is one stat against
# the cost of aiming root's appends at a directory somebody else owns. A gate that only ever
# runs where the answer is already known is exactly the gate that is worth having on the day
# the answer changes.
assert_site_log_root() {
  local owner mode
  if [ -L "$MARAN_SITE_LOG_ROOT" ] || [ ! -d "$MARAN_SITE_LOG_ROOT" ]; then
    echo "81-site-logs.sh: ${MARAN_SITE_LOG_ROOT} is not a real directory. Aborting." >&2
    exit 1
  fi
  owner="$(stat -c '%u' "$MARAN_SITE_LOG_ROOT")"
  mode="$(stat -c '%a' "$MARAN_SITE_LOG_ROOT")"
  if [ "$owner" -ne 0 ] || [ "$mode" != "750" ]; then
    echo "81-site-logs.sh: ${MARAN_SITE_LOG_ROOT} must be root-owned with mode 0750" >&2
    echo "  (found owner uid ${owner}, mode ${mode}). The root nginx master appends to files" >&2
    echo "  below this directory; refusing to point it at a tree another uid can reach." >&2
    exit 1
  fi
}

# account_log_dir_for: create <site log root>/<account> root:root 0750 and echo it.
#
# `install -d` and not `mkdir -p`, for the same reason step 40 uses it: the owner and the mode
# are stated rather than inherited from the installer's umask, which an operator's shell
# profile can have changed.
account_log_dir_for() {
  local account="$1" directory
  directory="${MARAN_SITE_LOG_ROOT}/${account}"
  install -d -o root -g root -m 0750 "$directory"
  printf '%s' "$directory"
}

# old_log_paths_in: print every access_log/error_log target under /home that VHOST names.
#
# One path per line, deduplicated, in the order they appear. The grep is anchored on the
# directive name at the start of a line's content and on the /home/ prefix, so an unrelated
# `access_log off;` or a log already at the new location produces nothing and the vhost is
# then left alone.
old_log_paths_in() {
  local vhost="$1"
  sed -n -E 's/^[[:space:]]*(access_log|error_log)[[:space:]]+(\/home\/[^;[:space:]]+);.*$/\2/p' \
    "$vhost" | awk '!seen[$0]++'
}

# account_of_old_log_path: the account name in /home/<account>/logs/<file>.
#
# Taken from the vhost's own text, which root wrote, and then held to the same grammar the
# agent's AccountName type enforces — lowercase letters, digits, underscore and hyphen, not
# starting with a hyphen. A path that does not match is not migrated and is reported, rather
# than being turned into a directory name nobody chose. This is not defence against the
# customer (they cannot write this file); it is defence against this step inventing a
# directory out of a vhost an operator hand-edited.
account_of_old_log_path() {
  local path="$1" account
  account="$(printf '%s' "$path" | awk -F/ '{ print $3 }')"
  case "$account" in
    '' | -* ) return 1 ;;
    *[!a-z0-9_-]* ) return 1 ;;
    * ) printf '%s' "$account" ;;
  esac
}

# report_planted_symlink: say so, loudly, if the name root used to open is a symbolic link.
#
# The link is NOT touched. It is left where it is so the operator can see where it pointed —
# it is evidence of an attempted escalation, and on a host where the escalation succeeded the
# target is the thing they must go and inspect. This is the one deliberate difference from
# install.sh's harden_log_directory, which MOVES a planted link aside: there, root keeps
# opening that name, so the link has to stop being at it. Here, after this step, no root
# process names that path again.
report_planted_symlink() {
  local path="$1" target
  [ -L "$path" ] || return 0
  target="$(readlink "$path" 2>/dev/null || printf '<unreadable>')"
  echo "SECURITY: ${path} is a SYMBOLIC LINK to ${target}, not a log file. The root nginx" >&2
  echo "  master opened that name on this server before this upgrade, so root writes may" >&2
  echo "  already have been redirected into that target — it may have been CREATED by root" >&2
  echo "  and it may contain lines whose content a hosting customer chose. Nothing root opens" >&2
  echo "  carries this name any more. The link has been left in place so you can see where it" >&2
  echo "  pointed. Inspect ${target} before trusting this host." >&2
}

# rewrite_log_paths: produce VHOST's text with every /home log target moved under the new root.
#
# Printed to stdout; the caller stages it. Only the two directives are touched, and only when
# their argument starts with /home/ — a `root /home/<account>/sites/<domain>;` line has a
# different directive name and is left exactly as it is, which matters because the DOCUMENT
# root is still in the home and must stay there.
rewrite_log_paths() {
  local vhost="$1"
  sed -E "s#^([[:space:]]*)(access_log|error_log)([[:space:]]+)/home/([a-z0-9_-]+)/logs/#\1\2\3${MARAN_SITE_LOG_ROOT}/\4/#" \
    "$vhost"
}

# stage_and_swap: write CONTENT over VHOST through render → stage → fsync → rename.
#
# The same protocol rules/rust.md fixes for every system configuration Maran writes, and the
# same one step 80 uses for the panel's own vhost: the candidate is staged in the TARGET's own
# directory (so the rename is within one filesystem and therefore atomic), both the file and
# its directory are fsynced (so a power loss cannot leave a rename pointing at unwritten
# bytes), and only then does it become the served file. A partially written vhost is a tree
# nginx refuses wholesale, which would take every tenant's site down.
stage_and_swap() {
  local vhost="$1" content="$2" staged
  staged="${vhost}.maran-site-logs-staging"
  # '%s\n', not '%s'. CONTENT reaches here through a command substitution, which strips every
  # trailing newline, so '%s' wrote each migrated vhost back with its final newline gone — a file
  # this step rewrote in place and nobody asked it to change. One newline restores the byte the
  # substitution took and leaves the file identical to the one that was read.
  printf '%s\n' "$content" > "$staged"
  chown root:root "$staged"
  chmod 0644 "$staged"
  sync "$staged"
  mv -f -- "$staged" "$vhost"
  sync "$(dirname -- "$vhost")"
}

# migrate_vhost: move one vhost's two log directives, creating the directories they need.
#
# Returns 0 when the file was changed, 1 when it had nothing to migrate. It does NOT validate
# or reload — the caller does that ONCE for the whole tree, because nginx validates and
# reloads a tree and not a file, and because a reload per vhost on a host with two hundred
# sites is two hundred reloads.
migrate_vhost() {
  local vhost="$1" path account rewritten
  local -a old_paths=()

  mapfile -t old_paths < <(old_log_paths_in "$vhost")
  [ "${#old_paths[@]}" -gt 0 ] || return 1

  for path in "${old_paths[@]}"; do
    if ! account="$(account_of_old_log_path "$path")"; then
      echo "81-site-logs.sh: ${vhost} names a log at ${path} whose account name does not" >&2
      echo "  match the grammar the agent enforces. NOT migrated; migrate it by hand." >&2
      return 1
    fi
    account_log_dir_for "$account" > /dev/null
    # Read before the rewrite, because after the rewrite nothing names this path any more.
    report_planted_symlink "$path"
  done

  rewritten="$(rewrite_log_paths "$vhost")"
  # A rewrite that changed nothing means the sed and the grep disagree about what a log
  # directive looks like, which is a bug in this file and not a host to migrate silently.
  if [ "$rewritten" = "$(cat "$vhost")" ]; then
    echo "81-site-logs.sh: ${vhost} names a log under /home that the rewrite did not change." >&2
    echo "  Refusing to report a migration that did not happen. Migrate it by hand." >&2
    return 1
  fi
  stage_and_swap "$vhost" "$rewritten"
  return 0
}

# step_site_logs: the step itself.
step_site_logs() {
  local vhost migrated=0 examined=0
  local -a backups=()

  assert_site_log_root
  install_logrotate_policy

  if [ ! -d "$MARAN_SITE_VHOST_DIR" ]; then
    echo "No ${MARAN_SITE_VHOST_DIR} yet: nothing to migrate on a fresh install."
    return 0
  fi

  # Every vhost's previous content, kept until the whole tree has been validated. A per-file
  # rollback is not enough here: the reload is one event for the whole tree, so a refusal must
  # put back every file this step touched and not merely the last one.
  shopt -s nullglob
  for vhost in "${MARAN_SITE_VHOST_DIR}"/*.conf; do
    examined=$((examined + 1))
    cp -p -- "$vhost" "${vhost}.maran-site-logs-previous"
    if migrate_vhost "$vhost"; then
      migrated=$((migrated + 1))
      backups+=("$vhost")
    else
      rm -f -- "${vhost}.maran-site-logs-previous"
    fi
  done
  shopt -u nullglob

  echo "Site logs: ${examined} vhost(s) examined, ${migrated} migrated to ${MARAN_SITE_LOG_ROOT}."
  # rules/testing.md: a gate states what it cannot see, in its own output, so the sentence is
  # in the install log and not only in a note nobody opens.
  echo "UNOBSERVED HERE: vhosts outside ${MARAN_SITE_VHOST_DIR} are not read or migrated." \
       "A vhost an operator hand-wrote into /etc/nginx/conf.d naming a log under /home is" \
       "still a root write into a directory a customer owns; find it with:" \
       "  nginx -T | grep -E '(access|error)_log[[:space:]]+/home/'"

  if [ "$migrated" -eq 0 ]; then
    return 0
  fi

  if ! validate_and_reload_nginx; then
    echo "81-site-logs.sh: nginx refused the migrated tree. Restoring every vhost." >&2
    for vhost in "${backups[@]}"; do
      mv -f -- "${vhost}.maran-site-logs-previous" "$vhost"
    done
    validate_and_reload_nginx || true
    echo "81-site-logs.sh: the previous vhosts are back and the site logs are STILL in" >&2
    echo "  customer-owned directories. This host remains exposed to the escalation in" >&2
    echo "  docs/superpowers/notes/2026-09-09-site-logs-threat-note.md. Aborting." >&2
    exit 1
  fi

  for vhost in "${backups[@]}"; do
    rm -f -- "${vhost}.maran-site-logs-previous"
  done
}

# validate_and_reload_nginx: ask the real nginx about the real tree, then make it reopen.
#
# `nginx -t` and not a grep of the files: only nginx knows which files it includes, and this
# whole class of defect — a check that observed a staging copy nobody serves — is what
# rules/testing.md's "a check must be able to observe what it reports on" was written about.
# The reload is what makes the master drop its descriptors on the old paths and open the new
# ones; until it happens, root is still appending into the customer's directory through a
# descriptor it already holds.
validate_and_reload_nginx() {
  nginx -t >/dev/null 2>&1 || return 1
  systemctl reload nginx >/dev/null 2>&1 || nginx -s reload >/dev/null 2>&1 || return 1
  return 0
}

# install_logrotate_policy: install /etc/logrotate.d/maran-sites.
#
# There was no rotation for site logs before this change, on any host, ever. While the logs
# were inside the home the account's disk quota was the only bound on them; outside the home
# there is none, so this policy is not housekeeping — it is the replacement for the quota.
#
# Every security-relevant choice in the shipped file is argued in a comment inside it, because
# the file is what an operator edits and this script is not.
install_logrotate_policy() {
  local source="${SCRIPT_DIR}/logrotate/maran-sites"
  if [ ! -r "$source" ]; then
    echo "81-site-logs.sh: ${source} is missing from the installer payload. Aborting." >&2
    exit 1
  fi
  install -D -o root -g root -m 0644 "$source" "$MARAN_LOGROTATE_DEST"
}
