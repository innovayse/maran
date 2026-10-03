#!/usr/bin/env bash
# the panel socket directory and the service account's single spelling
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The split is by SUBJECT and preserves the original order exactly, because these definitions
# are order-dependent in one respect that matters — a `readonly` set twice is a fatal error, so
# each constant lives beside the only assertions that use it.


# assert_the_panel_socket_directory_is_built_and_then_looked_at: the panel's trust boundary,
# BUILT in this image by the installer's own code and then observed — not grepped for.
#
# WHY IT IS SHAPED THIS WAY. The two checks this replaced grepped the api unit for an
# `ExecStartPre=+/usr/bin/chgrp` line and for `RuntimeDirectoryMode=2710`, and their failure
# messages said nginx would not be able to open the panel's socket. Both greps passed while the
# directory came out owned by the service account and its own group on both families and nginx
# could not open the socket at
# all: systemd re-applies a unit's User=/Group= to its RuntimeDirectory= on every command
# invocation of that unit, so the chgrp was undone before ExecStart ran. A check that reports on
# something it never looked at is worse than no check, because it retires the question.
#
# WHAT THIS ONE SEES, and it is a runtime fact rather than a text one: it runs step 70's real
# `install_units` (so the snippet is rendered by the installer's renderer, from install.sh's own
# socket path and this family's own web server group), its real `build_api_socket_directory` (so
# the directory is made by THIS family's systemd-tmpfiles, against this image's real service and
# web-server groups), and its real `assert_api_socket_directory`. Then it stats the directory
# itself, so the verdict does not rest on the installer agreeing with itself. It also breaks the
# directory by hand and checks that the installer's postcondition REFUSES, and that a second
# `--create` puts it back.
#
# WHAT THIS CANNOT SEE, stated plainly because the previous version of this check did not.
# This image never boots systemd — /usr/bin/systemctl here is docker/polygon/stand-ins/systemctl-stand-in.sh
# — so nothing starts maran-api.service, and NOTHING HERE OBSERVES the web server's uid reaching
# the socket or a customer's uid being refused. Two things stand in for that: the text assertion
# below that the unit declares no RuntimeDirectory= at all, so there is no exec directory for
# systemd to re-apply ownership to; and a measurement on booted systemd on both families
# (255 and 252), recorded in docs/superpowers/notes/2026-09-03-panel-socket-threat-note.md §3.1.
# The gap is printed in this function's own output, so a reader of a green build is told what it
# did not ask.
assert_the_panel_socket_directory_is_built_and_then_looked_at() {
  local socket_path socket_dir web_group observed expected payload

  socket_path="$(installer_value MARAN_API_SOCKET_PATH "$INSTALLER_ENTRY_POINT")"
  socket_dir="${socket_path%/*}"
  web_group="$(web_server_group_for_this_family)"
  [ -n "$web_group" ] \
    || fail "install.sh's detect_web_server_identity names no web server group for MARAN_OS_FAMILY=${MARAN_OS_FAMILY:-unset};
the socket directory would be group-owned by nothing and nginx could not reach the panel."
  getent group "$web_group" >/dev/null \
    || fail "install.sh names '${web_group}' as this family's web server group, and no such group exists in this
image. Either the name is wrong for ${MARAN_OS_FAMILY:-this family}, or the Dockerfile stopped installing nginx."

  # TEXT, and only what text can know: the unit must not take the directory back. This is the
  # regression guard for the defect above — RuntimeDirectory= is the one thing that makes systemd
  # re-apply User=/Group= over the directory on every command invocation.
  if grep -q '^RuntimeDirectory=' "$API_UNIT"; then
    fail "maran-api.service declares RuntimeDirectory= again. systemd re-applies the unit's User=/Group= to a
RuntimeDirectory= on EVERY command invocation, which is what silently undid the group this directory needs;
the directory is built by ${API_TMPFILES} instead, and the unit must leave it alone.
(This is a text check. What the directory comes out as is checked below, by building it.)"
  fi
  if ! grep -q "^ReadWritePaths=.*__MARAN_API_SOCKET_DIR__" "$API_UNIT"; then
    fail "maran-api.service's ReadWritePaths= no longer names __MARAN_API_SOCKET_DIR__. ProtectSystem=strict
mounts the whole filesystem read-only, RuntimeDirectory= used to make its own exception, and without one the
panel cannot create its socket at all.
(This is a text check over ${API_UNIT}.)"
  fi
  if ! grep -q '^ExecStopPost=-/usr/bin/rm -f __MARAN_API_SOCKET__$' "$API_UNIT"; then
    fail "maran-api.service no longer removes its socket in ExecStopPost. systemd does not delete the directory
on stop any more, and the server refuses to bind over an existing socket rather than reusing it — so a killed
panel would never start again.
(This is a text check over ${API_UNIT}.)"
  fi

  # The snippet is one directive, and this checks the shape of it rather than the whole line: what
  # it renders to is applied for real below.
  payload="$(grep -v '^[[:space:]]*#' "$API_TMPFILES" | grep -v '^[[:space:]]*$' || true)"
  [ "$(printf '%s\n' "$payload" | wc -l)" -eq 1 ] \
    || fail "${API_TMPFILES} no longer carries exactly one directive; systemd-tmpfiles would build something
this check has not read. It carries:
${payload}"
  case "$payload" in
    "d __MARAN_API_SOCKET_DIR__ 2710 __MARAN_USER__ __MARAN_WEB_GROUP__ -") ;;
    *) fail "${API_TMPFILES}'s directive is '${payload}'. It must be
'd __MARAN_API_SOCKET_DIR__ 2710 __MARAN_USER__ __MARAN_WEB_GROUP__ -': the type d so an existing directory is
CORRECTED and not only created, 2710 so no other uid can traverse to the socket and so the socket inherits
the group, __MARAN_USER__ because the unit's User= must be able to bind there, and the placeholders so the
path, the account and the group keep following install.sh." ;;
  esac

  # RUNTIME, from here down. Step 70's own functions, this family's own systemd-tmpfiles.
  run_services_step "$socket_path" "$web_group" 'install_units; build_api_socket_directory; assert_api_socket_directory' \
    || fail "step 70 could not build ${socket_dir} on this family. That directory is the panel's trust boundary
and the api unit does not start without it."

  # The polygon's own eyes, not the installer agreeing with itself.
  expected="2710 ${POLYGON_SERVICE_USER} ${web_group}"
  observed="$(stat -c '%a %U %G' "$socket_dir" 2>/dev/null || true)"
  [ "$observed" = "$expected" ] \
    || fail "${socket_dir} was built as '${observed:-absent}' and must be '${expected}'. At 2710 no other uid on
the machine can resolve a path inside it, so a customer's cron entry or PHP script cannot connect(2) to the
socket; the group is what lets nginx traverse it at all, and the setgid bit is what hands the socket that
group when the panel — which holds no capabilities — creates it."

  # The postcondition is not vacuous: break the directory and watch step 70 refuse.
  chgrp "$POLYGON_SERVICE_GROUP" "$socket_dir"
  if run_services_step "$socket_path" "$web_group" 'assert_api_socket_directory' >/dev/null 2>&1; then
    fail "step 70's assert_api_socket_directory accepted ${socket_dir} group-owned by ${POLYGON_SERVICE_GROUP}, which is the exact
state in which nginx cannot open the panel's socket and every API call answers 502. The postcondition that is
supposed to catch that on a real server does not catch it."
  fi

  # And a re-run puts it right, which is what makes re-running the installer a repair.
  run_services_step "$socket_path" "$web_group" 'build_api_socket_directory'
  observed="$(stat -c '%a %U %G' "$socket_dir" 2>/dev/null || true)"
  [ "$observed" = "$expected" ] \
    || fail "systemd-tmpfiles --create did not put ${socket_dir} back to '${expected}' after it was changed by
hand (read: '${observed:-absent}'). A boundary that cannot be repaired by re-running the installer is one an
operator will widen with chmod instead."

  # Leave the image as it was found: this is the only assertion that installs a unit.
  rm -f /etc/systemd/system/maran-api.service /etc/systemd/system/maran-agent.service
  rm -f /etc/tmpfiles.d/maran-api.conf
  rm -rf "$socket_dir"

  echo "Step 70 built ${socket_dir} as ${expected} with this family's real systemd-tmpfiles, refused it when its"
  echo "group was changed by hand, and repaired it on a re-run."
  echo "UNOBSERVED HERE: this image boots no systemd, so nothing above started maran-api.service or watched"
  echo "${web_group}'s uid connect to the socket while a customer's uid was refused. That was measured on booted"
  echo "systemd on both families and is recorded in the panel socket threat note; what stands in for it here is"
  echo "the text assertion that the unit declares no RuntimeDirectory= over the same directory."
}

# assert_the_service_account_name_has_one_authority: install.sh decides the name of the account
# the panel runs as, and every other file derives it. This fails the image build when one of them
# has grown a copy that disagrees.
#
# It exists for the reason assert_panel_port_has_one_authority exists, with a worse failure: the
# port disagreeing costs a 502, whereas the NAME disagreeing means step 40 creates one account,
# step 15 renames onto another, the units name a third and the uninstaller deletes a fourth. Two
# of those are silent on a fresh host and appear only on an upgrade.
#
# What it checks, and each one is a way this has already been done wrong somewhere:
#   * install.sh assigns each of the five names exactly once — a second assignment further down
#     is what `installer_value` reads, and the first is then a comment with syntax;
#   * the step files do NOT assign them at all: they read them from the environment, so a
#     `MARAN_USER=` inside a step is a second authority that would win inside that step only;
#   * the uninstaller, which is its own entry point and sources nothing, spells the same values.
assert_the_service_account_name_has_one_authority() {
  local key count step assignment uninstall_user uninstall_legacy uninstall_marker

  for key in MARAN_USER MARAN_GROUP MARAN_LEGACY_USER MARAN_LEGACY_GROUP MARAN_SERVICE_ACCOUNT_MARKER; do
    count="$(installer_value_count "$key" "$INSTALLER_ENTRY_POINT")"
    [ "$count" -eq 1 ] \
      || fail "install.sh assigns ${key} ${count} times at the start of a line. One is the authority; a
second one further down is what every reader here resolves, and the first becomes a comment with syntax."
  done

  for step in "$USER_STEP" "$IDENTITY_STEP" "$SERVICES_STEP" "$CONFIG_STEP" "$NGINX_STEP"; do
    for key in MARAN_USER MARAN_GROUP; do
      assignment="$(installer_value_count "$key" "$step")"
      [ "$assignment" -eq 0 ] \
        || fail "$(basename "$step") assigns ${key} itself. The name of the account is install.sh's to
decide and a step's to READ: a step that assigns it wins inside itself and disagrees with every other
step, which on an upgrade means one account created, another renamed and a third named by the units."
    done
  done

  uninstall_user="$(installer_value MARAN_USER "$UNINSTALLER")"
  uninstall_legacy="$(installer_value MARAN_LEGACY_USER "$UNINSTALLER")"
  uninstall_marker="$(installer_value MARAN_SERVICE_ACCOUNT_MARKER "$UNINSTALLER")"
  uninstall_marker="${uninstall_marker%\"}"
  uninstall_marker="${uninstall_marker#\"}"
  [ "$uninstall_user" = "$POLYGON_SERVICE_USER" ] \
    || fail "uninstall.sh removes the account '${uninstall_user}' while install.sh creates
'${POLYGON_SERVICE_USER}'. The uninstaller is its own entry point and sources nothing from the installer,
which is why these two are compared here instead of hoped about."
  [ "$uninstall_legacy" = "$POLYGON_LEGACY_USER" ] \
    || fail "uninstall.sh knows the pre-rename account as '${uninstall_legacy}' while install.sh names it
'${POLYGON_LEGACY_USER}'. An uninstall on a host that was never upgraded would leave its account behind."
  [ "$uninstall_marker" = "$POLYGON_SERVICE_MARKER" ] \
    || fail "uninstall.sh looks for the marker '${uninstall_marker}' and install.sh stamps
'${POLYGON_SERVICE_MARKER}'. The uninstaller would refuse to remove this product's own account, or —
worse, if it ever loosened — remove one it did not create."

  echo "The service account is named once, in install.sh: '${POLYGON_SERVICE_USER}' (group"
  echo "'${POLYGON_SERVICE_GROUP}', pre-rename '${POLYGON_LEGACY_USER}'), and no step file, and not"
  echo "the uninstaller, carries a second spelling of it."
}

# assert_a_foreign_account_at_the_service_name_is_refused: step 40 must REFUSE an account of its
# own name that it did not create, and must ACCEPT the one it did.
#
# The defect this is the gate for: create_service_user's predecessor was idempotent by `id -u`
# alone, so an account of that name that belonged to somebody else was adopted in silence and
# handed /var/lib/maran, read on panel.env and on the TLS key, User= on the api unit and an
# admitted uid on the root agent's socket. A rarer name makes that less likely and not
# impossible, so the mechanism is what has to hold.
#
# Three legs, and the second is why this is evidence rather than decoration: a guard mutated to
# refuse everything passes any test that only ever feeds it the bad case.
#   1. the marker replaced with somebody else's comment  -> the step must refuse, by that name;
#   2. the marker put back                               -> the step must accept;
#   3. the foreign comment again, with MARAN_ADOPT_EXISTING_USER=1 -> the step must adopt it
#      deliberately, which is the operator's documented way past the refusal.
#
# The image's real account is used, and its GECOS is put back at the end and READ BACK: a
# usermod that silently did not take would leave leg 2 measuring the same state as leg 1.
assert_a_foreign_account_at_the_service_name_is_refused() {
  local original restored output

  id -u "$POLYGON_SERVICE_USER" >/dev/null 2>&1 \
    || fail "this image has no '${POLYGON_SERVICE_USER}' account for the adoption gate to be tested
against. It is created by step 40's own create_service_user earlier in this script."
  original="$(getent passwd "$POLYGON_SERVICE_USER" | cut -d: -f5)"
  [ "$original" = "$POLYGON_SERVICE_MARKER" ] \
    || fail "the '${POLYGON_SERVICE_USER}' account in this image carries the comment '${original}' and not
install.sh's marker '${POLYGON_SERVICE_MARKER}'. Step 40 stamps that marker when it creates the account, so
either it stopped stamping it or something else made this account."

  usermod -c "Some other product's service account" "$POLYGON_SERVICE_USER"
  [ "$(getent passwd "$POLYGON_SERVICE_USER" | cut -d: -f5)" = "Some other product's service account" ] \
    || fail "the adoption control could not replace the marker on '${POLYGON_SERVICE_USER}'. The plant did
not land, so the refusal below would have been measured against an account carrying our own marker."

  if output="$(run_user_step 'create_service_user' 2>&1)"; then
    usermod -c "$POLYGON_SERVICE_MARKER" "$POLYGON_SERVICE_USER"
    fail "step 40's create_service_user ACCEPTED an account named '${POLYGON_SERVICE_USER}' that this
installer did not create. That is the defect the rename was made for: the install then hands an unrelated
local principal /var/lib/maran, read on panel.env and on the panel's TLS key, User= on maran-api.service
and an admitted uid on the root agent's socket. It said:
${output}"
  fi
  case "$output" in
    *"already exists on this host and this"*) ;;
    *) usermod -c "$POLYGON_SERVICE_MARKER" "$POLYGON_SERVICE_USER"
       fail "step 40 refused the foreign account but not by name; an operator reading this cannot tell it
from any other abort. It said:
${output}" ;;
  esac

  # Leg 3, while the foreign comment is still in place: the documented way past the refusal.
  MARAN_ADOPT_EXISTING_USER=1 run_user_step 'create_service_user' >/dev/null \
    || fail "MARAN_ADOPT_EXISTING_USER=1 did not let step 40 adopt the existing '${POLYGON_SERVICE_USER}'
account. The refusal then has no way past it, and an operator whose host legitimately holds that account
cannot install this product at all."

  # Leg 2, the inverse control. Adoption at leg 3 stamps the marker, so this also proves the
  # deliberate adoption left the account recognisable to the next run.
  usermod -c "$POLYGON_SERVICE_MARKER" "$POLYGON_SERVICE_USER"
  restored="$(getent passwd "$POLYGON_SERVICE_USER" | cut -d: -f5)"
  [ "$restored" = "$POLYGON_SERVICE_MARKER" ] \
    || fail "the adoption control could not put the marker back on '${POLYGON_SERVICE_USER}' (it reads
'${restored}'). The image is left in the planted state, and every assertion after this one runs against it."
  run_user_step 'create_service_user' >/dev/null \
    || fail "step 40's create_service_user REFUSED the account it created itself, the one carrying its own
marker. Re-running the installer over its own previous run is what idempotence means here, and it is now
broken: an upgrade of an installed host would abort at step 40."

  echo "Step 40 refused an account named '${POLYGON_SERVICE_USER}' carrying somebody else's comment, adopted"
  echo "it when MARAN_ADOPT_EXISTING_USER=1 said so, and accepted the one carrying its own marker."
}

# assert_the_legacy_service_account_is_migrated_in_place: step 15's rename keeps the uid and the
# gid, which is the property every file the pre-rename account owned depends on.
#
# WHY THIS IS THE PROPERTY. /etc/maran, panel.env, the TLS key, /var/lib/maran with the
# DataProtection keys in it, /var/log/maran and its panel subdirectory, /run/maran and the api's
# socket directory are all owned by that uid on an installed host. A migration that created a new
# account and chowned a list of paths would have to get the list right and would break whatever
# it missed; a rename cannot miss anything, because nothing about the files changes at all.
#
# Driven against THROWAWAY accounts rather than this image's real one: the real account owns
# every directory the assertions around this one measure, and taking it apart to rebuild it would
# make this assertion's failure mode "the next twelve assertions fail for a reason of my making".
# The names are passed to the step the same way install.sh passes them.
#
# UNOBSERVED HERE: this image never runs install.sh end to end, so the ORDER of the whole
# migration — stop the api, rename, rewrite, and step 70's restart at the far end — is not
# exercised. What is exercised is each thing the step does to the host, one at a time.
assert_the_legacy_service_account_is_migrated_in_place() {
  local from="maranpolygonfrom" to="maranpolygonto"
  local uid_before gid_before uid_after gid_after owner planted config found

  userdel "$from" >/dev/null 2>&1 || true
  userdel "$to" >/dev/null 2>&1 || true
  groupdel "$from" >/dev/null 2>&1 || true
  groupdel "$to" >/dev/null 2>&1 || true
  rm -rf -- /tmp/polygon-legacy-state
  useradd --system --no-create-home --shell /usr/sbin/nologin --user-group "$from"
  uid_before="$(id -u "$from")"
  gid_before="$(id -g "$from")"
  install -d -o "$from" -g "$from" -m 0750 /tmp/polygon-legacy-state
  : > /tmp/polygon-legacy-state/keys
  chown "${from}:${from}" /tmp/polygon-legacy-state/keys

  MARAN_IDENTITY_FROM="$from" MARAN_IDENTITY_TO="$to" \
    run_identity_step 'rename_the_group; rename_the_user' >/dev/null \
    || { userdel "$from" >/dev/null 2>&1 || true
         fail "step 15's rename of '${from}' to '${to}' failed inside the polygon. A pre-rename host cannot
be upgraded at all, and the panel is left stopped by the step that stops it first."; }

  id -u "$to" >/dev/null 2>&1 \
    || fail "step 15 reported success and there is no '${to}' account. The migration is the only thing that
renames a pre-rename host onto the name every other step now uses."
  uid_after="$(id -u "$to")"
  gid_after="$(id -g "$to")"
  [ "$uid_after" = "$uid_before" ] && [ "$gid_after" = "$gid_before" ] \
    || fail "step 15 changed the uid/gid while renaming: ${uid_before}/${gid_before} became
${uid_after}/${gid_after}. EVERY file the pre-rename account owned — /var/lib/maran and the DataProtection
keys in it, /etc/maran/panel.env, the panel's TLS private key, /var/log/maran/panel — is owned by that
NUMBER, so the panel would come up unable to read its own configuration and unable to write its own state."
  owner="$(stat -c '%U:%G' /tmp/polygon-legacy-state)"
  [ "$owner" = "${to}:${to}" ] \
    || fail "a directory owned by the pre-rename account reads '${owner}' after the migration and must read
'${to}:${to}'. Files follow a rename because the uid does not change; if they do not, this migration is a
chown of a list of paths and the list is not in it."
  [ "$(getent passwd "$to" | cut -d: -f5)" = "$POLYGON_SERVICE_MARKER" ] \
    || fail "step 15 renamed the account without stamping install.sh's marker on it, so step 40 — which
runs 25 steps later in the same install — would then refuse the very account this step just produced, and
every upgrade of a pre-rename host would abort halfway through the migration it just performed."

  userdel "$to" >/dev/null 2>&1 || true
  groupdel "$to" >/dev/null 2>&1 || true
  rm -rf -- /tmp/polygon-legacy-state

  # The predicate that decides whether a `panel` on this host is OURS at all, both ways round. A
  # foreign account of that name on a host with no Maran state must NOT be migrated, and the
  # account that owns /var/lib/maran must be.
  useradd --system --no-create-home --shell /usr/sbin/nologin --user-group "$from"
  if MARAN_IDENTITY_FROM="$from" MARAN_IDENTITY_TO="$to" \
      run_identity_step 'host_is_a_legacy_installation' >/dev/null 2>&1; then
    userdel "$from" >/dev/null 2>&1 || true
    groupdel "$from" >/dev/null 2>&1 || true
    fail "step 15 treats an account that owns nothing of this product's as a pre-rename installation of it.
It would then rename somebody else's account — and on a host that merely happens to have one, an install
would take it over instead of refusing it, which is the whole defect this change exists to close."
  fi
  userdel "$from" >/dev/null 2>&1 || true
  groupdel "$from" >/dev/null 2>&1 || true

  planted="$(stat -c '%U' /var/lib/maran)"
  [ "$planted" = "$POLYGON_SERVICE_USER" ] \
    || fail "/var/lib/maran is owned by '${planted}' and not by '${POLYGON_SERVICE_USER}', so the inverse
control below would be measuring the same 'no' as the case above and would prove nothing."
  MARAN_IDENTITY_FROM="$POLYGON_SERVICE_USER" MARAN_IDENTITY_TO="$to" \
    run_identity_step 'host_is_a_legacy_installation' >/dev/null 2>&1 \
    || fail "step 15's predicate answers NO for the account that owns /var/lib/maran. A pre-rename host
would then be left alone by the migration and taken over by step 40 instead, which is the state where the
api runs as one account and every file belongs to another."

  # The one name a rename does not follow: the role in the panel's own configuration file.
  config="/etc/maran/panel.env"
  rm -f -- "$config"
  install -d -o root -g root -m 0750 /etc/maran
  printf 'Database__Host=/var/run/postgresql\nDatabase__Username=%s\nSecurity__EncryptionKey=x\n' \
    "$POLYGON_LEGACY_USER" > "$config"
  run_identity_step 'rewrite_the_configured_role' >/dev/null \
    || { rm -f -- "$config"; fail "step 15 could not rewrite Database__Username in ${config}."; }
  grep -q "^Database__Username=${POLYGON_SERVICE_USER}\$" "$config" \
    || { rm -f -- "$config"
         fail "step 15 left ${config} naming the pre-rename role. PostgreSQL authenticates the panel by
matching its OS user name to the role name (peer auth), so the api would come up and fail every connection
with an authentication error against a role that no longer exists."; }
  found="$(stat -c '%U:%G:%a' "$config")"
  [ "$found" = "root:${POLYGON_SERVICE_GROUP}:640" ] \
    || { rm -f -- "$config"
         fail "step 15 rewrote ${config} and left it ${found} instead of
root:${POLYGON_SERVICE_GROUP}:640. That file holds the encryption key and the setup token; a rewrite that
widens it is a worse defect than the name it was fixing."; }
  rm -f -- "$config"

  echo "Step 15 renamed an account and its group in place with the uid and gid unchanged, files following,"
  echo "and install.sh's marker stamped; refused a same-named account that owns nothing of ours; recognised"
  echo "the one that owns /var/lib/maran; and rewrote Database__Username without widening panel.env."
  echo "UNOBSERVED HERE: this image runs no install end to end, so the migration's ORDER — the api stopped"
  echo "first and restarted by step 70 last — is not exercised, and neither is the PostgreSQL role rename,"
  echo "which needs a server holding this product's own database."
}
