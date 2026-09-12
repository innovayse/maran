#!/usr/bin/env bash
# Step 40: create the unprivileged system user that maran-api runs as
# (rules/architecture.md: the API is never root), and the on-disk directory layout
# with correct ownership and modes. The agent runs as root and needs no dedicated user.
set -euo pipefail

# The name of that account and of its group, and the marker that says an account of that name
# is ours. All three are decided in install.sh, the one place that decides them, and are read
# here rather than re-spelled: a step driven on its own (the polygon images do that) must fail
# by name rather than expand an empty string into `install -o` or `chown root:`.
: "${MARAN_USER:?40-user.sh: MARAN_USER is unset; it is set by install.sh and must be in the environment}"
: "${MARAN_GROUP:?40-user.sh: MARAN_GROUP is unset; it is set by install.sh and must be in the environment}"
: "${MARAN_SERVICE_ACCOUNT_MARKER:?40-user.sh: MARAN_SERVICE_ACCOUNT_MARKER is unset; it is set by install.sh and must be in the environment}"

# service_account_gecos: the GECOS (comment) field of the account named as `$1`, or the empty
# string when there is no such account. `getent passwd` is the resolver's own answer, so an
# account that comes from anywhere but /etc/passwd is seen too.
service_account_gecos() {
  getent passwd "$1" | cut -d: -f5
}

# create_service_user: a system account with no login shell and no home directory of its own
# under /home (it must never be confused with a customer hosting account), stamped with the
# marker that lets a later run recognise it as ours.
#
# WHAT REPLACED WHAT, because this is the whole point of the function. It used to be idempotent
# by `id -u` alone: if an account of the name existed, it was used. On a host that already had
# an account called `panel` — another control panel, a local service, a deploy user — the
# installer printed "already exists" and then gave THAT account /etc/maran, /var/lib/maran, the
# panel's TLS key, `User=` on the api unit and an admitted uid on the root daemon's socket. The
# name changed for that reason as much as for readability, but a new name does not fix the
# mechanism: `maran` is rarer than `panel`, not impossible. So the mechanism is fixed here.
#
# Three states, and only one of them creates anything:
#
#   1. Neither the account nor its group exists — create both, stamping the marker.
#   2. The account exists and its GECOS is EXACTLY the marker — it is a previous run of this
#      installer, adopt it silently. This is what keeps the installer idempotent, and it is the
#      difference that matters: idempotence now comes from RECOGNISING our own account instead
#      of from a name collision being assumed benign.
#   3. The account (or a group of that name with no such account) exists and carries no marker —
#      refuse and stop the install, printing whose account it appears to be. Adoption is still
#      possible, but only as a deliberate act: MARAN_ADOPT_EXISTING_USER=1 in the environment,
#      which prints the identity it is adopting first.
#
# The group is checked separately because `useradd --user-group` fails late and confusingly when
# the group already exists, and because a pre-existing GROUP of this name is the same grant
# problem in miniature: everyone in it would gain read on panel.env and on the agent socket.
#
# docs/superpowers/notes/2026-09-09-service-account-rename-threat-note.md carries the argument,
# including why the marker is provenance against accident and not authentication.
create_service_user() {
  local gecos uid shell home
  if id -u "$MARAN_USER" >/dev/null 2>&1; then
    gecos="$(service_account_gecos "$MARAN_USER")"
    if [ "$gecos" = "$MARAN_SERVICE_ACCOUNT_MARKER" ]; then
      echo "System user '${MARAN_USER}' already exists and is this installer's own; adopting it."
      return 0
    fi
    uid="$(id -u "$MARAN_USER")"
    shell="$(getent passwd "$MARAN_USER" | cut -d: -f7)"
    home="$(getent passwd "$MARAN_USER" | cut -d: -f6)"
    if [ "${MARAN_ADOPT_EXISTING_USER:-}" = "1" ]; then
      echo "MARAN_ADOPT_EXISTING_USER=1: adopting the EXISTING account '${MARAN_USER}'"
      echo "  uid ${uid}, home ${home}, shell ${shell}, comment '${gecos}'."
      echo "  It will own /var/lib/maran, read /etc/maran/panel.env and the panel's TLS key, run"
      echo "  the api, and be the uid the root agent admits on its socket."
      usermod -c "$MARAN_SERVICE_ACCOUNT_MARKER" "$MARAN_USER"
      return 0
    fi
    echo "40-user.sh: an account named '${MARAN_USER}' already exists on this host and this" >&2
    echo "  installer did not create it (uid ${uid}, home ${home}, shell ${shell}, comment" >&2
    echo "  '${gecos}')." >&2
    echo "  Continuing would give it /var/lib/maran, read access to /etc/maran/panel.env and to" >&2
    echo "  the panel's TLS private key, User= on maran-api.service, and an admitted uid on the" >&2
    echo "  root agent's socket. Refusing." >&2
    echo "  Rename that account, or re-run with MARAN_ADOPT_EXISTING_USER=1 to hand it to Maran" >&2
    echo "  deliberately. Aborting." >&2
    exit 1
  fi
  if getent group "$MARAN_GROUP" >/dev/null 2>&1; then
    if [ "${MARAN_ADOPT_EXISTING_USER:-}" != "1" ]; then
      echo "40-user.sh: a group named '${MARAN_GROUP}' (gid $(getent group "$MARAN_GROUP" | cut -d: -f3))" >&2
      echo "  already exists while no account of that name does, so this installer did not create" >&2
      echo "  it. Every member of that group would gain read on /etc/maran/panel.env, on the" >&2
      echo "  panel's TLS private key and on the root agent's socket. Refusing." >&2
      echo "  Rename that group, or re-run with MARAN_ADOPT_EXISTING_USER=1. Aborting." >&2
      exit 1
    fi
    echo "MARAN_ADOPT_EXISTING_USER=1: creating '${MARAN_USER}' inside the EXISTING group '${MARAN_GROUP}'."
    useradd --system --no-create-home --shell /usr/sbin/nologin \
      --gid "$MARAN_GROUP" --comment "$MARAN_SERVICE_ACCOUNT_MARKER" "$MARAN_USER"
  else
    useradd --system --no-create-home --shell /usr/sbin/nologin --user-group \
      --comment "$MARAN_SERVICE_ACCOUNT_MARKER" "$MARAN_USER"
  fi
  echo "Created system user '${MARAN_USER}'."
}

# create_directory_layout: every path Maran owns, with the tightest mode that still
# lets the intended process read/write it. /etc/maran holds panel.env (written with
# its own 0640 mode by 60-config.sh); the directory itself only needs to be traversable.
create_directory_layout() {
  install -d -o root  -g root         -m 0755 /usr/local/maran
  install -d -o root  -g "$MARAN_GROUP" -m 0750 /etc/maran
  install -d -o "$MARAN_USER" -g "$MARAN_GROUP" -m 0750 /var/lib/maran
  # /var/log/maran is ROOT's, with the service group able to read and traverse it — not the
  # panel's. Two root processes create and append to files directly inside it: install.sh's
  # `tee -a install.log`, which is the first thing an install does, and the nginx MASTER
  # process, which opens the panel vhost's `nginx-access.log` and `nginx-error.log`
  # (installer/nginx/maran.conf). While the panel uid owned this directory it could unlink
  # either name and leave a symbolic link there without ever being able to enter the target,
  # and both follows were measured: root's `tee -a` appended into a root-owned 0600 file, and
  # nginx appended a line whose request target the attacker chose
  # (docs/superpowers/notes/2026-09-07-installer-privileged-steps-threat-note.md, the
  # "/var/log/maran" section). `fs.protected_symlinks` does not engage at 0750: it wants a
  # world-writable sticky directory.
  #
  # Unlike the three earlier instances of this class — the release staging directory, the
  # database dump scratch and the SFTP jail base — this one is NOT fixed by relocating to a
  # root-only sibling, because operators, the installer and nginx all expect the panel's logs
  # in one place. The split is by ownership inside the tree instead: root owns the directory,
  # the panel owns one subdirectory of it.
  #
  # install.sh's harden_log_directory has already created and taken ownership of this
  # directory by the time step 40 runs (it must, since root logs into it from the first line);
  # this call sets the group, which did not exist that early on a fresh install.
  install -d -o root -g "$MARAN_GROUP" -m 0750 /var/log/maran
  assert_root_only_directory_with_mode /var/log/maran 750
  # The panel's own writable place under that tree. Nothing writes here today — the API logs
  # to stdout and journald captures it — but Serilog reads its sinks from configuration
  # (backend/src/Maran.Host/Extensions/ObservabilityExtensions.cs), so an operator can turn on
  # a file sink from /etc/maran/panel.env without a rebuild, and the design spec says logs go
  # both to journald and to this tree. maran-api.service's ReadWritePaths= names THIS path and
  # not its parent, so the panel can create entries here and nowhere root opens anything.
  #
  # Any future change that makes a ROOT process write inside this subdirectory reopens the
  # defect above one level down.
  install -d -o "$MARAN_USER" -g "$MARAN_GROUP" -m 0750 /var/log/maran/panel
  # AgentPaths::SITE_LOG_ROOT. The second root-owned subdirectory of this tree, holding one
  # directory per hosting account for the logs the ROOT nginx master writes on that account's
  # sites. root:root and not root:<service group> like its parent: the panel process has no business
  # reading a customer's access log off the disk — it asks the agent, which streams it — and a
  # group of root means the panel uid matches only `other`, which is `---` at 0750.
  #
  # These logs used to be /home/<account>/logs/<domain>.access.log, created by a child that had
  # dropped to the account, so the CUSTOMER owned the directory. The nginx master runs as root
  # and opens every access_log/error_log target O_WRONLY|O_APPEND|O_CREAT with no O_NOFOLLOW, so
  # a customer replaced the file with a symbolic link and the next reload — any tenant's site
  # action, since one nginx serves them all — had root create and append to the target. Proved
  # on a polygon image against /etc/ld.so.preload, with the loader then honouring an
  # attacker-chosen path in every process started afterwards
  # (docs/superpowers/notes/2026-09-09-site-logs-threat-note.md).
  #
  # chown root on the old directory would not have fixed it: the account owns its home and can
  # rename `logs` aside. The containment has to come from the path being outside every home,
  # which is what this directory is. The per-account directories BELOW it are created by the
  # agent as root (SiteHost::create_site_log_directory), not here, because the installer does
  # not know the accounts and an account created later must get one anyway.
  install -d -o root -g root -m 0750 /var/log/maran/sites
  assert_root_only_directory_with_mode /var/log/maran/sites 750
  # AgentPaths::BACKUP_ROOT (agent/crates/agent-core/src/agent_paths.rs), root:root 0700:
  # outside every home and document root, owned by root because the agent writes backup
  # archives here as root, never under an account's uid. Must exist before the first
  # backup: LocalBackupRoot::resolve() canonicalizes this path and returns
  # Unresolvable/BackupRootUnusable when it does not exist yet rather than creating it,
  # so without this line every backup.CreateBackup would fail on a freshly installed
  # host that never happened to get this directory another way.
  install -d -o root -g root -m 0700 /var/backups/maran
  # The restore staging root (AgentPaths::RESTORE_STAGING_ROOT). It lives under /home
  # rather than under /var/lib/maran because a restore swaps a home into place with
  # rename(2), which is atomic only within one filesystem and homes are frequently a
  # filesystem of their own.
  #
  # 0711 and not 0700: the account's own archiver extracts INTO a directory beneath
  # this one, and a process that cannot traverse a parent cannot open anything under
  # it. Not 0755 either — the entries are named <account>.<backup id>, so a listable
  # root would tell every customer which of their neighbours is being restored.
  #
  # Created the same guarded way as the scratch below and the release staging directory
  # (installer/lib/50-artifacts.sh), for the same measured reason: `install -d` exits 0 on
  # a path that already exists, FOLLOWS a symbolic link there, and checks neither owner nor
  # mode. Measured on GNU coreutils: `install -d -o root -g root -m 0711` against a symlink
  # to an existing directory leaves the symlink in place and applies the ownership and the
  # 0711 to the link's TARGET — so a planted link does not merely survive the install, it
  # gets root to widen the attacker's own directory to the mode the panel then trusts, and
  # every restore afterwards extracts a customer's home through it. (A link to a path that
  # does NOT exist fails loudly and `set -e` stops the install, so only the link-to-a-real-
  # directory shape is silently accepted.)
  #
  # The removal is CONDITIONAL where the scratch's is not, and the difference is customer
  # data. A restore whose second rename fails reports HomeParkedAt and leaves the account's
  # only home at /home/.maran-restore/<account>.previous.<id> for an operator to move back
  # by hand (agent/crates/ops/src/backup/restore_backup.rs). Step 40 runs again on every
  # upgrade, so an unconditional `rm -rf` here would delete that home during the very
  # recovery the agent's error message sends the operator into. Nothing an unprivileged uid
  # can plant is a root-owned real directory — /home is root:root 0755 on both families, and
  # chown to uid 0 needs root — so "keep it only if it is already a real directory owned by
  # root" removes every state an attacker can create and no state an operator depends on.
  remove_unless_root_only_directory /home/.maran-restore
  install -d -o root -g root -m 0711 /home/.maran-restore
  assert_root_only_directory_with_mode /home/.maran-restore 711
  # AgentPaths::BULK_SCRATCH_ROOT, root:root 0700, and deliberately NOT under
  # /var/lib/maran. The agent stages full plaintext dumps of a customer's databases
  # here while a backup or a restore runs. /var/lib/maran is created maran:maran
  # above, so while the scratch lived inside it the panel uid owned an ancestor of
  # root's staging area — enough to rename a level aside and leave a symlink at that
  # name without ever entering it, which was measured delivering a customer's dump
  # into a panel-owned file and truncating a root-owned 0600 file
  # (docs/superpowers/notes/2026-09-05-backups-threat-note.md, section 1). /var/lib
  # itself is root:root 0755 on both families, so no unprivileged uid can place an
  # entry at this name at all.
  #
  # Created the same way the release staging directory is (installer/lib/50-artifacts.sh):
  # `install -d` alone exits 0 on an existing path, follows a symlink and checks neither
  # owner nor mode, so it accepts a directory somebody else put there first. The `rm -rf`
  # removes a stale directory or a planted symlink (removing a symlink unlinks the link,
  # never its target), `install -d` makes a real one, and the gate below is what turns
  # this from a hope into a refusal. The agent re-states the same properties against the
  # inode immediately before it writes, because a mode is one chmod away from being wrong
  # and an install-time check says nothing about noon.
  rm -rf -- /var/lib/maran-scratch
  install -d -o root -g root -m 0700 /var/lib/maran-scratch
  assert_root_only_directory /var/lib/maran-scratch
  # AgentPaths::SFTP_JAIL_ROOT, root:root 0700, and — like the scratch above and the
  # release staging directory — deliberately a SIBLING of /var/lib/maran rather than a
  # child of it.
  #
  # OpenSSH refuses a chroot whose path has ANY component that is not owned by root or is
  # group/other-writable, and it says so only in the daemon's log: the client sees the
  # connection close after the password is accepted. While the jail base lived at
  # /var/lib/maran/sftp, the panel-owned /var/lib/maran above it meant every SFTP login on
  # a real server was refused — measured on both families as
  #   Accepted password for <login> ...
  #   bad ownership or modes for chroot directory component "/var/lib/maran/"
  # and fixed by the ownership of that one component and nothing else. Step 40 runs before
  # step 86, so the jail base could never have rescued an ancestor step 40 had already
  # created as the panel's.
  #
  # It is also the same escalation shape the scratch was moved for: the panel uid owning an
  # ancestor of a directory root trusts can rename a level aside and leave an entry of its
  # own at that name without ever having permission to enter it — here, every customer's
  # chroot. /var/lib is root:root 0755 on both families, so at the sibling no unprivileged
  # uid can place an entry at this name at all.
  #
  # The removal is CONDITIONAL, like the restore staging root's and unlike the scratch's,
  # and for the same reason: CUSTOMER DATA. Each jail beneath this path has the account's
  # real home BIND-MOUNTED at <account>/home, so an unconditional `rm -rf` on an upgrade
  # would recurse through a live mount and delete the customer's home. Nothing an
  # unprivileged uid can plant is a root-owned real directory, so keeping only that state
  # removes every state an attacker can create and no state a customer depends on.
  # 0700 and not the 0755 the old base carried. Only root ever traverses this directory:
  # `sshd` walks the chroot path as root before it drops privileges, `systemd` performs the
  # bind mount as root, and a logged-in customer's chroot ROOT is <account>/ one level
  # below — which is still 0755 so the login can list "/". At /var/lib/maran-sftp the mode
  # was masked by the 0750 parent; directly under a world-listable /var/lib, 0755 would
  # publish one directory name per hosting account to every local uid. 0700 keeps the base
  # exactly as private as it used to be while its ancestors become root's.
  remove_unless_root_only_directory /var/lib/maran-sftp
  install -d -o root -g root -m 0700 /var/lib/maran-sftp
  assert_root_only_directory /var/lib/maran-sftp
  # /run/maran is normally recreated on boot by the systemd unit's RuntimeDirectory=
  # (see installer/systemd/maran-agent.service); created here too so the directory
  # exists immediately for the rest of this install run, before services first start.
  install -d -o root -g "$MARAN_GROUP" -m 0750 /run/maran
}

# remove_unless_root_only_directory: deletes whatever occupies the path unless it is
# already a real directory (not a symbolic link to one) owned by uid 0, so that the
# `install -d` which follows creates the directory rather than reaching through somebody
# else's link into somebody else's tree.
#
# Removing a symbolic link unlinks the LINK; the target it pointed at is untouched, which
# is what makes this safe to run against a path an attacker aimed somewhere valuable.
#
# The exception for a root-owned real directory is not a weakening: the states this
# defends against — a symlink, or a directory another uid owns — all require the ability
# to create an entry at the path, and a root-owned directory there is one only root can
# have made. What it buys is idempotence for a path that legitimately holds data between
# runs, which is why the mode is checked by the gate below and not here: a wrong mode on a
# real root-owned directory is repaired by `install -d`, whereas deleting the directory
# would destroy its contents.
remove_unless_root_only_directory() {
  local path="$1"
  if [ ! -L "$path" ] && [ -d "$path" ] && [ "$(stat -c '%u' "$path")" -eq 0 ]; then
    return 0
  fi
  rm -rf -- "$path"
}

# assert_root_only_directory_with_mode: refuses to continue unless the path is a real
# directory (not a symlink to one), owned by uid 0, with exactly the octal mode given as
# the second argument. Aborting the install is the right answer: the alternative is a panel
# that starts and stages customer data in a directory somebody else can reach.
#
# The mode is a parameter because the root-only directories this step creates do not
# share one. The scratch and the SFTP jail base are 0700 — nothing but root ever opens a
# path beneath them from the OUTSIDE, and the jail base's own children are the modes an
# SFTP login sees, not this one. The
# restore staging root is 0711, because the account's own archiver extracts into a
# directory below it and cannot traverse a parent it has no execute bit on. /var/log/maran is
# 0750 with the panel group: root is the only uid that may create an entry there, which is the
# whole protection, while the panel and an operator in that group still read the install log
# and the vhost logs. Note what this gate does NOT check for that path — the group. A gate
# that also demanded root:root would refuse the one directory here that has a deliberate
# non-root group, and the group has no write bit at 0750. Reading the
# expected mode out of the caller keeps the gate exact for both instead of loosening it to
# whatever both would pass, which is how a gate stops observing what it reports on.
assert_root_only_directory_with_mode() {
  local path="$1" expected_mode="$2" owner mode
  if [ -L "$path" ] || [ ! -d "$path" ]; then
    echo "40-user.sh: ${path} is not a real directory. Aborting." >&2
    exit 1
  fi
  owner="$(stat -c '%u' "$path")"
  mode="$(stat -c '%a' "$path")"
  if [ "$owner" -ne 0 ] || [ "$mode" != "$expected_mode" ]; then
    echo "40-user.sh: ${path} must be owned by root with mode 0${expected_mode}" >&2
    echo "  (found owner uid ${owner}, mode ${mode}). Refusing to stage customer data in a" >&2
    echo "  directory another uid can reach. Aborting." >&2
    exit 1
  fi
}

# assert_root_only_directory: the 0700 case of assert_root_only_directory_with_mode, kept
# as a name of its own because that is the mode of every directory only root ever enters,
# and a call site that spells out `700` invites the next reader to wonder which modes are
# also allowed.
assert_root_only_directory() {
  assert_root_only_directory_with_mode "$1" 700
}

step_user() {
  echo "Creating '${MARAN_USER}' system user and directory layout..."
  create_service_user
  create_directory_layout
  echo "User and directories ready."
}
