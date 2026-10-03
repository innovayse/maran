#!/usr/bin/env bash
# 40-user.sh's layout: ownership, the scratch and ancestor gates, log roots, staging
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The split is by SUBJECT and preserves the original order exactly, because these definitions
# are order-dependent in one respect that matters — a `readonly` set twice is a fatal error, so
# each constant lives beside the only assertions that use it.


# --- The on-disk boundary the two privilege-escalation fixes moved ------------------------
#
# Everything from here to main() is about ONE question: is each directory the installer
# creates the thing it is documented to be — a real directory, owned by the uid that is
# supposed to own it, with exactly the mode that keeps every other uid out?
#
# It exists because two panel->root escalations were found and fixed in one day, and both
# were escapes of exactly that property:
#
#   * release staging lived inside a `panel`-owned directory, so the api's uid could swap
#     agent.tar.gz between the checksum verification and the extraction, and the extracted
#     file is what maran-agent.service runs as root (installer/lib/50-artifacts.sh, and
#     docs/superpowers/notes/2026-09-07-installer-privileged-steps-threat-note.md);
#   * the bulk scratch lived inside `panel`-owned /var/lib/maran, so the api's uid owned an
#     ancestor of root's staging area, could rename a level aside and leave a symlink at
#     that name, and root would then write a customer's plaintext database dump into a
#     panel-readable file — or truncate a root-owned one
#     (docs/superpowers/notes/2026-09-05-backups-threat-note.md section 1).
#
# Both fixes are correct and both were, until this section, guarded by nothing but the
# comments explaining them. A prose guarantee is the thing this repository has already been
# taught to distrust: it does not fail when it stops being true.
#
# Why the expected owners and modes are written out here as literals, when the Dockerfile
# comment beside `create_directory_layout` says a second copy of a mode is a copy that
# stops matching. Those are two different jobs. The Dockerfile calls the installer so the
# polygon's directories are made the way a real install makes them — there, a literal would
# be a copy of a value with no opinion of its own. Here the literals ARE the opinion: a
# check that reads its expectation out of the code it is checking agrees with that code by
# construction and can never fail, which is precisely how a mode gets loosened without
# anything going red. This table is a second, independent statement of the boundary, in the
# same spirit as check-structure.sh comparing ReadWritePaths= against agent_paths.rs.

# assert_the_directory_layout_is_what_it_claims: runs the installer's OWN step 40 and then
# looks at every directory it made — is it a real directory rather than a symlink to one,
# who owns it, what is its exact mode.
#
# The symlink question is asked separately from the directory question on purpose. `[ -d ]`
# follows symlinks, so a symlink pointing at a directory answers yes to it; that is the
# shape the scratch attack took, and a check that only asked `[ -d ]` would have watched it
# happen. `stat` without `-L` reports the link's own mode (0777 on Linux, always) and the
# link's owner, so neither of those would have caught it either.
#
# /run/maran's mode is put back afterwards. This image sets it 0755 on purpose for the
# php-pool suites and step 40 sets 0750; the Dockerfile re-applies 0755 after its own call
# to this function, and this restores it so the assertion cannot depend on which of the two
# ran last.
assert_the_directory_layout_is_what_it_claims() {
  local run_maran_mode_before=''
  if [ -d /run/maran ]; then
    run_maran_mode_before="$(stat -c '%a' /run/maran)"
  fi

  run_user_step 'create_service_user >/dev/null
create_directory_layout' \
    || fail "step 40's create_directory_layout failed inside the polygon"

  # path:expected uid:expected gid:expected mode, one per line. The service account is
  # resolved at check time rather than written as a number, because a system user's uid is
  # whatever useradd picked on this host.
  local panel_uid panel_gid
  panel_uid="$(id -u "$POLYGON_SERVICE_USER")"
  panel_gid="$(id -g "$POLYGON_SERVICE_USER")"

  # `/var/log/maran/sites` (AgentPaths::SITE_LOG_ROOT) is in this list and is not one more
  # directory: it is the containment for a CRITICAL escalation. Every site's nginx logs used
  # to be /home/<account>/logs/<domain>.access.log, inside a tree the CUSTOMER owns, and the
  # root nginx master opens every access_log target O_WRONLY|O_APPEND|O_CREAT with no
  # O_NOFOLLOW — so a customer replaced one with a symbolic link and the next reload, which
  # any tenant's site action triggers because one nginx serves them all, had ROOT create and
  # append to the target. Proved on this image against /etc/ld.so.preload
  # (docs/superpowers/notes/2026-09-09-site-logs-threat-note.md).
  #
  # The fix is the path, and the path's whole defence is the ownership and mode of every
  # ancestor: /var/log/maran root:<service group> 0750, /var/log/maran/sites root:root 0750. Group
  # ROOT and not the service group like its parent — the panel process has no business reading a
  # customer's access log off the disk, it asks the agent, so the panel uid must match only
  # `other`, which is `---` at 0750. If the installer ever creates this directory with the
  # parent's group, or 0755, or under a uid that is not root, the escalation returns and no
  # test in this repository outside this file would notice: the agent's own suites create
  # the tree themselves rather than reading what the installer left.
  local entry checked=0
  for entry in \
    "/usr/local/maran:0:0:755" \
    "/etc/maran:0:${panel_gid}:750" \
    "/var/lib/maran:${panel_uid}:${panel_gid}:750" \
    "/var/log/maran:0:${panel_gid}:750" \
    "/var/log/maran/panel:${panel_uid}:${panel_gid}:750" \
    "/var/log/maran/sites:0:0:750" \
    "/var/backups/maran:0:0:700" \
    "/home/.maran-restore:0:0:711" \
    "/var/lib/maran-scratch:0:0:700" \
    "/var/lib/maran-sftp:0:0:700" \
    "/run/maran:0:${panel_gid}:750"; do
    assert_directory_is "${entry%%:*}" "${entry#*:}"
    checked=$((checked + 1))
  done
  # The vacuity guard, and it is on the axis that can actually go blind here. Everything this
  # loop asserts comes from ONE list, the loop body runs zero times for an empty one, and a
  # `for` over nothing exits 0 — so a path deleted from the list (a bad merge, a rebase
  # resolving a conflict by taking the shorter side) removes a check with no output changing
  # anywhere. A count is not decoration on that axis: it is the only thing that observes the
  # list's SIZE, which is the property the assertions themselves cannot see. Raise it with the
  # list.
  [ "$checked" -eq 11 ] \
    || fail "the directory layout list checked ${checked} paths, not the 11 step 40 creates. A path has been dropped from the list, and a check that is not in the list is not a check that failed — it is one that no longer exists."

  # The scratch is a SIBLING of /var/lib/maran, never a child, and the reason is the whole
  # fix: the owner of a directory can rename an entry inside it aside and leave a symlink
  # at that name without ever having permission to enter it, so a root-only directory
  # underneath a panel-owned one is not root-only. Asserted as a statement about the PATH
  # because that is what the fix changed, and a mode check on the scratch cannot see it —
  # the scratch was root:root 0700 while it was exploitable.
  case "$(cd /var/lib/maran-scratch && pwd -P)" in
    /var/lib/maran/*) fail "/var/lib/maran-scratch resolves underneath /var/lib/maran, whose owner is the panel uid — the ancestor that made this directory reachable is back" ;;
  esac
  # And the ancestor it does have must be root's. /var/lib is root:root 0755 on both
  # families; if it ever were not, no mode on the scratch itself would save it.
  assert_ancestors_are_root_only /var/lib/maran-scratch

  # The SFTP jail base, for the same reason and with a second consequence the scratch does
  # not have. Every path component of a `ChrootDirectory` must be root-owned or OpenSSH
  # refuses the login, so a panel-owned ancestor here does not merely make the jails
  # reachable — it makes SFTP not work at all, silently, with the reason visible only in
  # the daemon's log. Measured on both families at the old base: `Accepted password ...`
  # followed by `bad ownership or modes for chroot directory component "/var/lib/maran/"`.
  case "$(cd /var/lib/maran-sftp && pwd -P)" in
    /var/lib/maran/*) fail "/var/lib/maran-sftp resolves underneath /var/lib/maran, whose owner is the panel uid — sshd refuses a chroot with a non-root path component, so every SFTP login on this host is broken" ;;
  esac
  assert_ancestors_are_root_only /var/lib/maran-sftp

  # The log directory, the fourth instance of this class and the only one not fixed by a
  # relocation. Three leaf names inside /var/log/maran are opened for APPEND by root:
  # install.sh's own install.log, and the panel vhost's nginx-access.log and nginx-error.log,
  # which the root nginx MASTER creates and reopens on every start, reload and SIGUSR1. While
  # step 40 made this directory panel:panel, the panel uid could unlink any of those names and
  # leave a symbolic link — measured getting root's `tee -a` to append into a root-owned 0600
  # file, and getting nginx to append an attacker-chosen request line into another
  # (docs/superpowers/notes/2026-09-07-installer-privileged-steps-threat-note.md).
  #
  # Asserted on the LEAVES and not on the directory, because the ancestor walk is the check
  # that can see this defect: the leaf's own mode says nothing about who may replace it, and
  # nginx recreates these files itself, as root, so nothing else stands between the plant and
  # the open. The names come from install.sh, one place, so a fourth root-written log added
  # there is covered here without anyone extending a second list.
  local trusted_log_leaf
  for trusted_log_leaf in $(installer_root_trusted_log_names); do
    assert_ancestors_are_root_only "/var/log/maran/${trusted_log_leaf}"
  done
  # And the panel's own subdirectory is NOT an ancestor of any of them — the split's whole
  # point. If a future change moved a root-written log inside it, the walk above would fail,
  # but this states the shape directly so the diagnosis names the design and not just a uid.
  case "$(cd /var/log/maran/panel && pwd -P)" in
    /var/log/maran/panel) ;;
    *) fail "/var/log/maran/panel does not resolve to itself — the panel's writable log directory is a link somewhere else, and root writes in its parent" ;;
  esac

  # The site log root's ANCESTRY, which is a different question from its own uid:gid:mode
  # asserted in the list above and is the question the F-1 fix actually turns on. Root writes
  # into this tree — the nginx master opens every site's access_log and error_log under it, as
  # root, with no O_NOFOLLOW — and who may replace a directory is decided by its PARENT, never
  # by the directory's own mode. The old location was root-writable too; what made it
  # exploitable was that /home/<account> belongs to the customer. So a mode check on this
  # directory cannot see the defect that moved it, and this walk is what can.
  assert_ancestors_are_root_only /var/log/maran/sites
  # And it must be a real directory at that name, not a link: `install -d` exits 0 on a symlink
  # to an existing directory and applies the ownership and the mode to the link's TARGET, which
  # would have root open every customer's site log wherever the link points. assert_directory_is
  # rejects a link above; this states the resolution directly so the diagnosis names the shape.
  case "$(cd /var/log/maran/sites && pwd -P)" in
    /var/log/maran/sites) ;;
    *) fail "/var/log/maran/sites does not resolve to itself — the root-owned site log root is a link somewhere else, and the root nginx master creates and appends to every site's access_log and error_log through it" ;;
  esac

  if [ -n "$run_maran_mode_before" ]; then
    chmod "$run_maran_mode_before" /run/maran
  fi

  echo "Step 40's directory layout verified: eleven directories, each a real directory with its own owner and mode, and every root-written log leaf under a root-only ancestry."
  echo "UNOBSERVED HERE: only the two directories the INSTALLER creates are asserted above — /var/log/maran and /var/log/maran/sites. The per-account level, /var/log/maran/sites/<account> root:root 0750, is created by the agent as root at site-create time (SiteHost::create_site_log_directory), never by the installer, because the installer does not know the accounts and an account created later must get one anyway. It is asserted where it is made: agent/crates/agent/tests/sites_on_a_real_host.rs, every_directory_the_site_log_tree_is_made_of_belongs_to_root. Nothing here can see it, and a reader must not take the eleven rows above as covering the whole tree."
  echo "UNOBSERVED HERE: the ancestry above is the state step 40 leaves behind, not a guarantee about later. Neither of the two root writers that made this directory a defect runs in this image — install.sh's own \`tee -a\` and the root nginx master's access_log/error_log opens — and the nginx one could not be gated even where it does run, because nginx re-creates those files itself on every start, reload and SIGUSR1, after every check the installer performs. The ownership asserted above is the whole defence, not a check in front of the write."
}

# assert_directory_is: one path against `uid:gid:mode`, with the three questions asked
# separately so the diagnosis names which one failed.
assert_directory_is() {
  local path="$1" expected="$2" found

  [ -e "$path" ] || [ -L "$path" ] \
    || fail "${path} was not created by the installer step that is supposed to create it"
  if [ -L "$path" ]; then
    fail "${path} is a SYMBOLIC LINK, not a directory. Its owner and its mode are the link's, never the target's, and root writing through it writes wherever it points."
  fi
  [ -d "$path" ] || fail "${path} exists but is not a directory"

  found="$(stat -c '%u:%g:%a' "$path")"
  [ "$found" = "$expected" ] \
    || fail "${path} is uid:gid:mode ${found}, not ${expected} ($(stat -c '%U:%G' "$path") by name)"
}

# assert_ancestors_are_root_only: every directory from `$1` up to / is owned by uid 0 and
# is not writable by group or other.
#
# This is the check the scratch defect needed and nothing had. A directory's own mode says
# who may act on its CONTENTS; who may replace the directory itself is decided by its
# parent, and by that parent's parent. Both of today's fixes are relocations — the same
# mode, moved to a path whose ancestors are root's — so this is the assertion that actually
# observes what changed.
assert_ancestors_are_root_only() {
  local path="$1" current="$1" owner mode
  while [ "$current" != "/" ]; do
    current="$(dirname "$current")"
    owner="$(stat -c '%u' "$current")"
    # The symbolic form, not the octal one: the octal is three digits on one host and four
    # on another (a setuid or sticky bit adds a leading digit and shifts every position),
    # so a pattern written against three characters silently stops matching the field it
    # was aimed at. `drwxr-xr-x` puts group-write at index 5 and other-write at index 8 on
    # every host there is.
    mode="$(stat -c '%A' "$current")"
    [ "$owner" -eq 0 ] \
      || fail "${current}, an ancestor of ${path}, is owned by uid ${owner} and not by root. The owner of a directory can rename an entry inside it aside and leave a symlink in its place, so ${path} is not root-only however it is moded."
    if [ "${mode:5:1}" = "w" ]; then
      fail "${current}, an ancestor of ${path}, is ${mode} — GROUP-writable. Any member of that group can replace ${path} with an entry of their own."
    fi
    if [ "${mode:8:1}" = "w" ]; then
      fail "${current}, an ancestor of ${path}, is ${mode} — WORLD-writable. Any uid can replace ${path} with an entry of its own."
    fi
  done
}

# assert_the_scratch_gate_refuses_a_directory_root_does_not_own: the inverse control for
# step 40's `assert_root_only_directory`.
#
# The positive assertion above passes on a correct host and would pass just as happily if
# that gate had been deleted — `install -d` had already made the directory right. So the
# gate is fed, one at a time, each of the three states it exists to refuse, and must abort
# on every one of them; then it is handed the real directory and must ACCEPT it, because a
# gate mutated to refuse everything passes every test that only ever gives it broken input
# (rules/testing.md).
#
# The gate is called directly rather than through create_directory_layout, and that is a
# limitation worth naming rather than hiding: create_directory_layout does `rm -rf` and
# then `install -d` BEFORE it calls the gate, so on a real host root always repairs the
# planted state and the gate never sees it. What the gate defends against is the window
# between those two calls, and against a host where root's own `install` did not do what
# root asked. Neither is reachable by planting a file. What is reachable is the gate's own
# behaviour, and that is what is measured here.
assert_the_scratch_gate_refuses_a_directory_root_does_not_own() {
  local scratch="/var/lib/maran-scratch" victim="/tmp/polygon-scratch-victim"

  rm -rf -- "$scratch" "$victim"
  install -d -o root -g root -m 0700 "$victim"
  ln -s "$victim" "$scratch"
  assert_scratch_gate_refuses "a symbolic link to a root-owned 0700 directory" 'not a real directory'

  rm -f -- "$scratch"
  install -d -o "$POLYGON_SERVICE_USER" -g "$POLYGON_SERVICE_GROUP" -m 0700 "$scratch"
  assert_scratch_gate_refuses "a directory owned by the panel uid" 'must be owned by root'

  rm -rf -- "$scratch"
  install -d -o root -g root -m 0750 "$scratch"
  assert_scratch_gate_refuses "a root-owned directory the panel GROUP can enter" 'must be owned by root'

  rm -rf -- "$scratch"
  install -d -o root -g root -m 0777 "$scratch"
  assert_scratch_gate_refuses "a world-writable root-owned directory" 'must be owned by root'

  # The inverse control: the state a correct install leaves behind must be ACCEPTED.
  rm -rf -- "$scratch" "$victim"
  install -d -o root -g root -m 0700 "$scratch"
  run_user_step 'assert_root_only_directory "'"$scratch"'"' \
    || fail "step 40's assert_root_only_directory REFUSED a real root:root 0700 directory. A gate that refuses everything proves nothing about the four refusals above."

  echo "Step 40's assert_root_only_directory refused a symlink, a panel-owned directory, a group-readable one and a world-writable one, and accepted the real thing."
}

# assert_scratch_gate_refuses: hands the planted /var/lib/maran-scratch to the installer's
# own gate in a child process — `exit 1` inside it is only observable across a process
# boundary, for the reason run_installer_step states at length — and requires both a
# non-zero status and a diagnosis containing `$2`.
#
# The message is checked and not only the status, because a gate that aborts for the wrong
# reason (a typo'd `stat`, an unbound variable under `set -u`) is indistinguishable from a
# gate that caught the attack if all you look at is the exit code.
assert_scratch_gate_refuses() {
  local what="$1" expected_message="$2" output status=0
  output="$(run_user_step 'assert_root_only_directory /var/lib/maran-scratch' 2>&1)" || status=$?

  [ "$status" -ne 0 ] \
    || fail "step 40's assert_root_only_directory ACCEPTED ${what} at /var/lib/maran-scratch. Root stages plaintext customer database dumps there."
  case "$output" in
    *"$expected_message"*) ;;
    *) fail "step 40's assert_root_only_directory refused ${what}, but its diagnosis does not contain '${expected_message}' — it said: ${output}" ;;
  esac
}

# assert_the_ancestor_walk_refuses_a_panel_owned_ancestor: the inverse control for
# `assert_ancestors_are_root_only` itself.
#
# The two positive calls above pass on a correct host, and they would pass exactly as
# happily if the walk's body were `return 0` — /var/lib is root:root 0755 on both families,
# so on a healthy image the loop never has anything to refuse. That is the shape this
# assertion exists to break: the walk is the ONLY check in this file that observes what the
# three relocations actually changed, and a check that has never once said no is a check
# nobody has seen work.
#
# So it is handed, in a child process because `fail` exits, the exact state the SFTP defect
# was — a root:root 0700 directory whose PARENT is owned by the panel uid, the shape the
# directory's own mode cannot see — and then a group-writable ancestor and a world-writable
# one. Each must be refused with a diagnosis naming what it found. Then the real
# /var/lib/maran-sftp, whose ancestors are root's, must be ACCEPTED, because a walk mutated
# to refuse everything passes every test that only ever feeds it broken input.
assert_the_ancestor_walk_refuses_a_panel_owned_ancestor() {
  local root="/tmp/polygon-ancestor-control"

  rm -rf -- "$root"
  install -d -o root -g root -m 0755 "$root"

  install -d -o "$POLYGON_SERVICE_USER" -g "$POLYGON_SERVICE_GROUP" -m 0750 "$root/panel-owned"
  install -d -o root -g root -m 0700 "$root/panel-owned/child"
  assert_ancestor_walk_refuses "$root/panel-owned/child" \
    "a root:root 0700 directory whose parent is owned by the panel uid" \
    'is owned by uid'

  install -d -o root -g root -m 0775 "$root/group-writable"
  install -d -o root -g root -m 0700 "$root/group-writable/child"
  assert_ancestor_walk_refuses "$root/group-writable/child" \
    "a root-owned directory under a GROUP-writable ancestor" \
    'GROUP-writable'

  # 0757 and not 0777: the walk checks group-write BEFORE other-write, so a 0777 ancestor
  # is refused with the GROUP diagnosis and this case would never reach the other-write
  # branch at all. Measured — the first version of this control used 0777 and the build
  # said so, which is the control doing its job on its own author. 0757 is world-writable
  # and not group-writable, so it exercises the branch it names.
  install -d -o root -g root -m 0757 "$root/world-writable"
  install -d -o root -g root -m 0700 "$root/world-writable/child"
  assert_ancestor_walk_refuses "$root/world-writable/child" \
    "a root-owned directory under a WORLD-writable ancestor" \
    'WORLD-writable'

  rm -rf -- "$root"

  # The inverse control: the real jail base, after the relocation, must be accepted.
  ( assert_ancestors_are_root_only /var/lib/maran-sftp ) \
    || fail "assert_ancestors_are_root_only REFUSED /var/lib/maran-sftp, whose ancestors are /var/lib and /. A walk that refuses everything proves nothing about the three refusals above."

  echo "The ancestor walk refused a panel-owned, a group-writable and a world-writable ancestor — the SFTP defect's own shape — and accepted the relocated jail base."
}

# assert_ancestor_walk_refuses: runs the walk against a planted path in a SUBSHELL, because
# `fail` exits the process, and requires both a non-zero status and a diagnosis containing
# `$3`.
#
# The message and not only the status, for the reason assert_scratch_gate_refuses gives: a
# walk that aborts for the wrong reason — an unbound variable under `set -u`, a `stat` that
# could not read the path — is indistinguishable from one that caught the attack if all you
# look at is an exit code.
assert_ancestor_walk_refuses() {
  local path="$1" what="$2" expected_message="$3" output status=0

  output="$( assert_ancestors_are_root_only "$path" 2>&1 )" || status=$?

  [ "$status" -ne 0 ] \
    || fail "assert_ancestors_are_root_only ACCEPTED ${what}. That is the exact shape of the SFTP chroot defect, and this walk is the only check in this file that can see it."
  case "$output" in
    *"$expected_message"*) ;;
    *) fail "assert_ancestors_are_root_only refused ${what}, but its diagnosis does not contain '${expected_message}' — it said: ${output}" ;;
  esac
}

# assert_the_log_directory_gate_sees_the_defect_it_was_written_for: the inverse control for the
# log-leaf ancestor assertions in assert_the_directory_layout_is_what_it_claims.
#
# Those assertions pass on a correct host and would pass just as happily if the walk could not
# see this defect at all, so the defect is PUT BACK — /var/log/maran is chowned to the panel uid,
# which is exactly what installer/lib/40-user.sh:29 used to do — and every root-written leaf must
# then be refused, by name, with the ownership diagnosis. It is restored afterwards and the same
# leaves must be ACCEPTED, because a walk that refuses everything proves nothing.
#
# The plant is on the REAL path and not on a stand-in tree: the generic walk control below covers
# the walk's behaviour on synthetic ancestors, and what is unproven without this is that the walk
# is pointed at the paths the defect actually lives on. A check aimed one directory to the side
# would pass that control and see nothing here.
#
# The directory's contents are untouched: `chown` on the directory alone, both ways.
#
# The plant is VERIFIED TO HAVE LANDED before anything is scored, and the restore afterwards.
# A `chown` that silently did not take — the directory already gone, a stand-in `chown` earlier
# on PATH, a read-only layer — leaves the walk looking at a correct host, and every leaf is then
# refused by nothing and this function reports the control as held while it measured a healthy
# tree. Three mutations in this repository were scored BLIND that way in one day, so the plant is
# read back with `stat` and disagreement is a failure, not a warning.
assert_the_log_directory_gate_sees_the_defect_it_was_written_for() {
  local leaf checked=0 panel_uid planted restored

  panel_uid="$(id -u "$POLYGON_SERVICE_USER")"
  chown "${POLYGON_SERVICE_USER}:${POLYGON_SERVICE_GROUP}" /var/log/maran
  planted="$(stat -c '%u' /var/log/maran)"
  [ "$planted" = "$panel_uid" ] \
    || fail "the log-leaf control tried to put the defect back by chowning /var/log/maran to the panel uid (${panel_uid}) and stat still reports uid ${planted}. The plant did not land, so every refusal below would have been measured against a HEALTHY directory and this control would report itself held while checking nothing."

  for leaf in $(installer_root_trusted_log_names); do
    assert_ancestor_walk_refuses "/var/log/maran/${leaf}" \
      "/var/log/maran/${leaf} under a PANEL-OWNED /var/log/maran — the defect exactly as step 40 used to create it" \
      'is owned by uid'
    checked=$((checked + 1))
  done
  chown "root:${POLYGON_SERVICE_GROUP}" /var/log/maran
  restored="$(stat -c '%u' /var/log/maran)"
  [ "$restored" = "0" ] \
    || fail "the log-leaf control could not restore /var/log/maran to root (stat reports uid ${restored}). The image is being left carrying the defect this control plants, and the acceptance pass below would be measured against it."

  [ "$checked" -eq 3 ] \
    || fail "the log-leaf control planted the defect and then checked ${checked} leaves, not the three root writes install.sh, and nginx's master, actually make. A control that checks nothing passes."

  for leaf in $(installer_root_trusted_log_names); do
    ( assert_ancestors_are_root_only "/var/log/maran/${leaf}" ) \
      || fail "assert_ancestors_are_root_only REFUSED /var/log/maran/${leaf} on a correctly installed host, whose ancestors are /var/log/maran (root:<service group> 0750), /var/log and /. The refusals above prove nothing if the walk refuses the real thing too."
  done

  echo "The log-leaf ancestor check was shown the original defect — /var/log/maran owned by the panel — refused all three root-written leaves by name, and accepted them again once the directory was root's. The plant and the restore were both read back with stat, so a chown that did not take fails here instead of scoring a healthy directory as a caught defect."
  # What this control does NOT observe, in its own output rather than only in a comment above it,
  # because a check that reads like coverage it does not have is the defect this file exists for.
  echo "UNOBSERVED HERE: the nginx half of this defect cannot be gated by anything, here or in the installer. The root nginx MASTER creates and re-opens nginx-access.log and nginx-error.log itself, at the fixed paths installer/nginx/maran.conf names, on every start, every reload and every SIGUSR1 — always AFTER install.sh's harden_log_directory and after step 40's assertion have finished. So what is proved above is a statement about the directory's ownership at install time, which is the ENTIRE defence; there is no moment at which a check could stand between a planted symlink and nginx's open. Nothing in this image, and nothing in this repository, watches those two opens. A reviewer wanting that evidence must take it from a booted host: nginx running, its master's /proc/<pid>/fd entries resolving to regular files inside /var/log/maran, and \`ls -l\` on the two names showing root-owned regular files rather than links."
}

# assert_the_site_log_root_check_can_see_each_property_break: the inverse control for the
# `/var/log/maran/sites:0:0:750` row of the directory layout list.
#
# That row passes on a correctly installed image and would pass exactly as happily if
# `assert_directory_is` had gone blind on any one of the three properties it claims to check
# — a `stat` format string losing a field, an expectation string that no longer matches what
# the installer writes, a comparison that became a prefix test. The row is the ONLY thing in
# CI that observes the containment the F-1 escalation fix depends on, so "it is green" has to
# mean something stronger than "nobody has broken it yet".
#
# So each of the three properties is broken ON THE REAL PATH, one at a time, and the check
# must refuse with a diagnosis naming the triple it found:
#
#   owner  the panel uid instead of root. The owner of a directory may rename any entry inside
#          it aside and leave a symbolic link at that name, so a panel-owned site log root
#          gives the panel uid the root nginx master's O_APPEND back — the whole escalation,
#          one level up from where it used to live.
#   group  root:<service group>, the parent's group, which is the realistic regression: somebody makes
#          this directory look like /var/log/maran. At 0750 that hands the panel uid read on
#          every hosting customer's access log, which the panel is deliberately not allowed to
#          take off the disk — it asks the agent, which streams it.
#   mode   0755. Defence in depth rather than the front line, since /var/log/maran above it is
#          0750 root:<service group> and `other` cannot traverse it today — but this directory's mode is
#          what stops a customer walking into a NEIGHBOUR's log directory if that parent ever
#          widens, and a check that cannot see the mode would not report it when it does.
#
# Then the directory is put back and the same check must ACCEPT it, because a check mutated to
# refuse everything passes every test that only ever hands it broken input (rules/testing.md).
#
# Every plant and the restore are read back with `stat` before anything is scored. A `chown`
# or `chmod` that silently did not take leaves the check looking at a healthy directory, and
# this function would then report itself held while measuring nothing — the exact shape three
# mutations in this repository were scored blind by in one day.
assert_the_site_log_root_check_can_see_each_property_break() {
  local sites="/var/log/maran/sites" panel_uid panel_gid found refusals=0

  panel_uid="$(id -u "$POLYGON_SERVICE_USER")"
  panel_gid="$(id -g "$POLYGON_SERVICE_USER")"

  chown "$panel_uid":root "$sites"
  found="$(stat -c '%u:%g:%a' "$sites")"
  [ "$found" = "${panel_uid}:0:750" ] \
    || fail "the site log root control tried to break the OWNER by chowning ${sites} to uid ${panel_uid} and stat reports ${found}. The plant did not land, so the refusal below would have been measured against a healthy directory."
  assert_directory_check_refuses "$sites" "0:0:750" \
    "${sites} owned by the panel uid instead of root — the owner of a directory can rename an entry inside it aside and leave a symlink, which is the escalation this path exists to contain" \
    "is uid:gid:mode ${found}, not 0:0:750"
  refusals=$((refusals + 1))

  chown root:"$panel_gid" "$sites"
  found="$(stat -c '%u:%g:%a' "$sites")"
  [ "$found" = "0:${panel_gid}:750" ] \
    || fail "the site log root control tried to break the GROUP by chowning ${sites} to gid ${panel_gid} and stat reports ${found}. The plant did not land."
  assert_directory_check_refuses "$sites" "0:0:750" \
    "${sites} carrying the panel group like its parent, which at 0750 gives the panel uid read on every customer's access log" \
    "is uid:gid:mode ${found}, not 0:0:750"
  refusals=$((refusals + 1))

  chown root:root "$sites"
  chmod 0755 "$sites"
  found="$(stat -c '%u:%g:%a' "$sites")"
  [ "$found" = "0:0:755" ] \
    || fail "the site log root control tried to break the MODE by chmodding ${sites} to 0755 and stat reports ${found}. The plant did not land."
  assert_directory_check_refuses "$sites" "0:0:750" \
    "${sites} at 0755, world-traversable — the bit that stops a customer walking into a neighbour's log directory if the tree above it ever widens" \
    "is uid:gid:mode ${found}, not 0:0:750"
  refusals=$((refusals + 1))

  chown root:root "$sites"
  chmod 0750 "$sites"
  found="$(stat -c '%u:%g:%a' "$sites")"
  [ "$found" = "0:0:750" ] \
    || fail "the site log root control could not restore ${sites} to root:root 0750 (stat reports ${found}). The image would ship carrying the defect this control plants, and every site the polygon suites create would write its logs into it."

  [ "$refusals" -eq 3 ] \
    || fail "the site log root control scored ${refusals} refusals, not the three properties — owner, group and mode — it exists to prove are observable. A control that checks fewer than it says passes."

  # The inverse control: the state a correct install leaves behind must be ACCEPTED.
  ( assert_directory_is "$sites" "0:0:750" ) \
    || fail "assert_directory_is REFUSED the real ${sites} at root:root 0750, which is what installer/lib/40-user.sh creates. A check that refuses everything proves nothing about the three refusals above."

  echo "The site log root's row was shown each of its three properties broken in turn on the real path — owner to the panel uid, group to the panel group, mode to 0755 — refused each by name with the triple it found, and accepted root:root 0750 again. Every plant and the restore were read back with stat, so a chown that did not take fails here instead of being scored as a caught defect."
  echo "UNOBSERVED HERE: this proves the CHECK can see a break, on the path the installer writes, at install time. It is not a statement about later. The root nginx master creates and re-opens every site's access_log and error_log under this directory itself, on every start, reload and SIGUSR1, always after this script has finished — so the ownership and mode asserted here are the ENTIRE defence, not a gate standing in front of the open. What the agent does one level down, at /var/log/maran/sites/<account>, is asserted only in agent/crates/agent/tests/sites_on_a_real_host.rs."
}

# assert_directory_check_refuses: hands a planted path to `assert_directory_is` in a SUBSHELL,
# because `fail` exits the process, and requires both a non-zero status and a diagnosis
# containing `$4`.
#
# The message and not only the status, for the reason assert_scratch_gate_refuses gives at
# length: a check that aborts for the wrong reason — an unbound variable under `set -u`, a
# `stat` that could not read the path — is indistinguishable from one that caught the defect
# if all you look at is an exit code. Here the expected message carries the TRIPLE the plant
# produced, so a check that noticed some other difference and reported some other value does
# not score as a catch either.
assert_directory_check_refuses() {
  local path="$1" expected="$2" what="$3" expected_message="$4" output status=0

  output="$( assert_directory_is "$path" "$expected" 2>&1 )" || status=$?

  [ "$status" -ne 0 ] \
    || fail "assert_directory_is ACCEPTED ${what}. That row is the only thing in CI that observes the containment the site-log escalation fix depends on."
  case "$output" in
    *"$expected_message"*) ;;
    *) fail "assert_directory_is refused ${what}, but its diagnosis does not contain '${expected_message}' — it said: ${output}" ;;
  esac
}

# assert_the_release_staging_directory_is_root_only: step 50's staging directory, the one
# an artifact is downloaded into, checksummed in and extracted from.
#
# It is the highest-value directory in the whole install: the file that comes out of it is
# /usr/local/maran/agent/maran-agent, which maran-agent.service starts as root. While it
# lived under /var/lib/maran the api's uid owned it, and because every archive used to be
# checksummed before ANY of them was extracted, the api could replace agent.tar.gz in that
# window and install a root backdoor.
#
# Step 50 is not RUN here — it downloads over HTTPS from the release server and this build
# has no network trust to spend on that — so `prepare_staging_dir` is called on its own.
# That is the whole of the directory half of the fix, and it is the half a polygon can see.
assert_the_release_staging_directory_is_root_only() {
  # The path comes from the step's own constant, and is then held against the literal
  # below. Two different jobs again: asking the step where it stages means a move of the
  # directory is diagnosed as a MOVE — "step 50 now stages in X" — instead of arriving as
  # "the directory nobody created is missing", which is what a hardcoded path alone says
  # and is the least useful sentence a security check can produce. The literal is what
  # makes it a check rather than a description: without it the ancestor rule below would
  # be the only thing standing between the staging directory and a quiet relocation to
  # somewhere that happens to satisfy it.
  local staging
  staging="$(run_staging_step 'printf "%s" "$MARAN_ARTIFACT_TMP"')" \
    || fail "step 50 does not define MARAN_ARTIFACT_TMP; this check cannot see where release artifacts are staged"
  [ "$staging" = "/var/lib/maran-artifact-staging" ] \
    || fail "step 50 stages release artifacts in ${staging}, not /var/lib/maran-artifact-staging. That directory is where an archive is checksummed and extracted, and the file that comes out of it is the binary maran-agent.service runs as root — a move of it is a security change, not a tidy-up."

  run_staging_step 'prepare_staging_dir' \
    || fail "step 50's prepare_staging_dir failed inside the polygon"

  assert_directory_is "$staging" "0:0:700"
  assert_ancestors_are_root_only "$staging"

  # It must not be under /var/lib/maran, for the reason the scratch must not be.
  case "$(cd "$staging" && pwd -P)" in
    /var/lib/maran/*) fail "${staging} resolves underneath /var/lib/maran, which the panel uid owns — root would be extracting a root-run binary out of a directory the api can rearrange" ;;
  esac

  # And no unit may hand it to an unprivileged process. This is a text assertion about the
  # units and says so: nothing here boots systemd.
  local unit
  for unit in "$AGENT_UNIT" "$API_UNIT"; do
    if grep '^ReadWritePaths=' "$unit" | grep -q 'maran-artifact-staging'; then
      fail "$(basename "$unit") lists /var/lib/maran-artifact-staging in ReadWritePaths=. The release staging directory is the installer's alone; a running service that can write it can replace the agent binary before it is extracted."
    fi
  done

  echo "Step 50's prepare_staging_dir built /var/lib/maran-artifact-staging root:root 0700 under root-only ancestors, and no unit makes it writable."
}

# run_staging_step: step 50's code in a child, the way install.sh runs it.
#
# SCRIPT_DIR is exported because 50-artifacts.sh resolves the release signing key against
# it in a top-level `readonly`, and under `set -u` an unset variable would kill the source
# before a single function was defined — a failure that looks nothing like the one this
# check is about. It points at the installer tree this image carries; nothing below reads
# the key, and no check here depends on it existing.
run_staging_step() {
  local snippet="$1" path_prefix="${2:-}"
  local search_path="$PATH"
  if [ -n "$path_prefix" ]; then
    search_path="${path_prefix}:${PATH}"
  fi
  SCRIPT_DIR="$INSTALLER_ROOT" PATH="$search_path" bash -c 'set -euo pipefail
. "$1"
. "$2"
eval "$3"' _ "$COMMON_LIB" "$ARTIFACTS_STEP" "$snippet"
}

# assert_the_staging_gate_refuses_what_install_left_wrong: the inverse control for the gate
# inside prepare_staging_dir.
#
# It cannot be driven by planting a directory, and the reason is worth writing down because
# it is the same reason the scratch gate needed its own entry point. prepare_staging_dir
# does `rm -rf` and then `install -d -o root -g root -m 0700` before it looks at anything,
# so root repairs whatever was planted and the gate is handed a correct directory every
# time. Feeding it a violation means the CREATION, not the plant, has to go wrong — which
# is exactly the case the gate is written for: `install -d` exits 0 on a path it did not
# make the way it was asked.
#
# So an `install` stand-in goes first on the step's PATH, using the same mechanism
# run_nginx_step already uses to put a hostile binary in front of a step. The stand-in is
# a fixture, not a mutation of the installer: 50-artifacts.sh is byte-for-byte the shipped
# file in every run below, and the thing being measured is whether its gate looks at the
# result of a command instead of trusting it.
#
# Two states, because they fail different lines: a directory with the wrong mode, and a
# symlink — which `install -d` follows and `[ -d ]` would call a directory.
assert_the_staging_gate_refuses_what_install_left_wrong() {
  local shim_dir="/tmp/polygon-staging-shim" victim="/tmp/polygon-staging-victim"
  rm -rf -- "$shim_dir" "$victim"
  install -d -m 0755 "$shim_dir"

  # A stand-in that ignores -o/-g/-m and leaves the directory world-writable.
  cat > "${shim_dir}/install" <<'SHIM'
#!/usr/bin/env bash
# Polygon fixture: an `install` that creates the directory it was asked for and then
# ignores the ownership and mode it was asked for. It stands in for the host on which
# root's own tools did not do what root asked, which is the only state the staging gate
# can ever meet on a real machine.
set -euo pipefail
: > /tmp/polygon-staging-shim.ran
target="${@: -1}"
/usr/bin/install -d -m 0777 "$target"
SHIM
  chmod 755 "${shim_dir}/install"
  assert_staging_gate_refuses "$shim_dir" "a world-writable staging directory" 'must be owned by root with mode 0700'

  # A stand-in that leaves a symbolic link where the directory should be.
  install -d -o root -g root -m 0700 "$victim"
  cat > "${shim_dir}/install" <<'SHIM'
#!/usr/bin/env bash
# Polygon fixture: an `install` that leaves a symbolic link at the staging path instead
# of a directory. `[ -d ]` alone answers yes to this, which is why the gate asks `[ -L ]`
# first; this is the fixture that makes that ordering matter.
set -euo pipefail
: > /tmp/polygon-staging-shim.ran
target="${@: -1}"
ln -s /tmp/polygon-staging-victim "$target"
SHIM
  chmod 755 "${shim_dir}/install"
  assert_staging_gate_refuses "$shim_dir" "a symbolic link at the staging path" 'is not a real directory'

  rm -rf -- "$shim_dir" "$victim" /tmp/polygon-staging-shim.ran
  # The inverse control: with the real `install` back on PATH the step must SUCCEED.
  run_staging_step 'prepare_staging_dir' \
    || fail "prepare_staging_dir refused the directory its own real install(1) had just built. A gate that refuses everything proves nothing about the two refusals above."
  assert_directory_is /var/lib/maran-artifact-staging "0:0:700"

  echo "Step 50's staging gate refused a world-writable directory and a symbolic link, and accepted the one the real install builds."
}

# assert_staging_gate_refuses: runs prepare_staging_dir with `$1` first on PATH and
# requires a non-zero status and a diagnosis containing `$2`.
#
# The stand-in is checked to have actually been reached. A PATH prefix that does not take
# effect — a stand-in that is not executable, a step that calls install by absolute path —
# turns this into a run of the ordinary code that passes, and a negative test that passes
# for the wrong reason is the exact failure mode this file's header is about.
assert_staging_gate_refuses() {
  local shim_dir="$1" what="$2" expected_message="$3" output status=0

  rm -rf -- /var/lib/maran-artifact-staging /tmp/polygon-staging-shim.ran
  output="$(run_staging_step 'prepare_staging_dir' "$shim_dir" 2>&1)" || status=$?

  # The vacuity guard, on the axis that can go blind: whether the stand-in was reached at
  # all. A PATH prefix that does not take effect turns this into an ordinary, passing run
  # of the step, and the negative test then reports a refusal that never happened.
  [ -f /tmp/polygon-staging-shim.ran ] \
    || fail "the install(1) stand-in in ${shim_dir} was never executed, so prepare_staging_dir met no violation at all and the refusal below would have been about nothing"

  [ "$status" -ne 0 ] \
    || fail "step 50's prepare_staging_dir ACCEPTED ${what}. The archive extracted from that directory becomes /usr/local/maran/agent/maran-agent, which systemd runs as root."
  case "$output" in
    *"$expected_message"*) ;;
    *) fail "prepare_staging_dir refused ${what}, but its diagnosis does not contain '${expected_message}' — it said: ${output}" ;;
  esac
}
