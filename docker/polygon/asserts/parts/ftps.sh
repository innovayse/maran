#!/usr/bin/env bash
# 89-ftps.sh: the packaged daemon, the jail base, the PAM group gate, naming
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The split is by SUBJECT and preserves the original order exactly, because these definitions
# are order-dependent in one respect that matters — a `readonly` set twice is a fatal error, so
# each constant lives beside the only assertions that use it.


# assert_the_packaged_daemon_was_never_startable: the mask, and then the package, in that order.
#
# This is the one assertion in the block whose subject is a WINDOW rather than a state, so it
# runs the two functions itself instead of reading a host somebody else prepared: the package is
# required to be absent when it starts, the mask is made, the package is installed through the
# step's own list, and the mask is asked about again afterwards. Between those two calls is the
# whole security property of step 89 — on the Debian family `apt-get install vsftpd` starts the
# distribution's daemon from its postinst, which is port 21 accepting local system logins with
# the password in the clear, on a machine that is by definition reachable from the internet.
#
# Fails if `mask_packaged_vsftpd` stops running or stops producing the link, if the package name
# in `vsftpd_packages_for_family` stops being right on this family, or if anything in the install
# removes the mask again.
assert_the_packaged_daemon_was_never_startable() {
  if [ -e /usr/sbin/vsftpd ]; then
    fail "/usr/sbin/vsftpd is already on this image before the FTPS assertions ran. This assertion is about the WINDOW between masking the packaged unit and installing the package, and a package installed by something else has closed that window before it could be observed. Remove the install from the Dockerfile: the image gets vsftpd from install_vsftpd_package and from nothing else."
  fi

  mask_packaged_vsftpd

  local mask_path="/etc/systemd/system/vsftpd.service"
  [ -L "$mask_path" ] \
    || fail "${mask_path} is not a symbolic link after mask_packaged_vsftpd ran. A mask IS that link; nothing else is."
  [ "$(readlink -- "$mask_path")" = "/dev/null" ] \
    || fail "${mask_path} points at $(readlink -- "$mask_path"), not at /dev/null — the packaged unit is not masked and its postinst could start it"

  install_vsftpd_package

  [ -x /usr/sbin/vsftpd ] \
    || fail "install_vsftpd_package ran, but /usr/sbin/vsftpd is not on this host. Either vsftpd_packages_for_family names a package this family does not have, or the package stopped putting the daemon at the path installer/systemd/maran-ftps.service execs."

  # The mask must have SURVIVED the package. dpkg and rpm both write into
  # /etc/systemd/system, and a postinst that replaced the link would have reopened exactly the
  # window this ordering exists to close.
  [ -L "$mask_path" ] && [ "$(readlink -- "$mask_path")" = "/dev/null" ] \
    || fail "${mask_path} is no longer a link to /dev/null after the package was installed — the package's own scripts took the mask away"

  # The positive control on the axis that could silently go blind: a mask shadows a unit FILE,
  # so if the distribution shipped none, this whole assertion would be green while masking
  # nothing. It is the shadowing that makes the unit unstartable, and there has to be something
  # under the shadow for the sentence to mean anything.
  local packaged_unit
  packaged_unit="$(ftps_unit_shipped_by_the_distribution)"
  [ -n "$packaged_unit" ] \
    || fail "the vsftpd package on this family ships no unit file under /lib/systemd/system or /usr/lib/systemd/system, so ${mask_path} shadows nothing and this assertion has been measuring an empty statement. Find out what the package ships now and mask that."
  echo "Masked: ${mask_path} -> /dev/null, shadowing ${packaged_unit}."

  # REPORTED, never asserted — see the block comment. On the Debian family this entry EXISTS,
  # mask or no mask, and a check written the other way round would fail this build for a reason
  # that has nothing to do with whether the daemon can start.
  local packaged_wants
  packaged_wants="$(ftps_wants_entries_for vsftpd.service)"
  if [ -n "$packaged_wants" ]; then
    echo "NOT A DEFECT, and not asserted on: the package's own enablement bookkeeping left ${packaged_wants}."
    echo "  It names a unit whose file is shadowed by /dev/null, which systemd ignores. The mask above is the control; this symlink is neither the control nor a defect."
  else
    echo "NOT THE CONTROL either way: this family's package left no .wants entry for vsftpd.service. On the Debian family that is deb-systemd-helper declining to enable a unit it finds masked (measured 2026-09-09), which is a fact about a package script and not about whether the daemon can start."
  fi

  if command -v ss >/dev/null 2>&1; then
    echo "Listening sockets on port 21 after the package landed:"
    ss -lnt 2>/dev/null | awk 'NR == 1 || $4 ~ /:21$/' || true
  else
    echo "UNOBSERVED HERE: iproute2 is not in this image, so not even the empty port-21 listing below could be taken."
  fi
  echo "UNOBSERVED HERE: whether the postinst would have started the daemon. No systemd boots during an image build, and Docker's own policy-rc.d denies the start outright, so the port-21 listing above is empty whether the mask exists or not — it is decoration and is printed as such. What is observed here is the artefact: the link, and the unit it shadows. The live fact belongs on a real host."
}

# assert_the_vsftpd_package_is_refused_while_its_unit_is_startable: the inverse control for the
# ordering above.
#
# The positive assertion passes on a correct host and would pass just as happily if
# `install_vsftpd_package`'s own guard had been deleted — the mask was already there. So the
# guard is handed the one host it exists to refuse, a host whose packaged unit is startable, and
# must abort by name; and then the real host again, which it must ACCEPT, because a gate mutated
# to refuse everything passes every test that only ever gives it broken input (rules/testing.md).
#
# The second half is the other refusal in the same function, and it is a refusal to DESTROY:
# a real file at the mask path is an operator's own unit or override, and `mask_packaged_vsftpd`
# must refuse the install rather than delete it to make room for a link.
assert_the_vsftpd_package_is_refused_while_its_unit_is_startable() {
  local mask_path="/etc/systemd/system/vsftpd.service" status output

  rm -f -- "$mask_path"
  status=0
  output="$( ( install_vsftpd_package ) 2>&1 )" || status=$?
  [ "$status" -ne 0 ] \
    || fail "install_vsftpd_package accepted a host whose packaged vsftpd.service is not masked. On the Debian family that is the postinst starting port 21 with local logins and no TLS."
  case "$output" in
    *"is NOT masked"*) ;;
    *) fail "install_vsftpd_package refused, but not for the reason it should have:
${output}" ;;
  esac

  # The refusal to destroy. A real file, not a link, at the same path.
  install -d -o root -g root -m 0755 /etc/systemd/system
  printf '# an operator unit, not ours\n' > "$mask_path"
  status=0
  output="$( ( mask_packaged_vsftpd ) 2>&1 )" || status=$?
  [ "$status" -ne 0 ] \
    || fail "mask_packaged_vsftpd accepted a REAL ${mask_path}. Whatever it did to that file, the file was somebody else's unit."
  case "$output" in
    *"is a real file, not a mask"*) ;;
    *) fail "mask_packaged_vsftpd refused a real unit file, but not for the reason it should have:
${output}" ;;
  esac
  [ -f "$mask_path" ] && [ ! -L "$mask_path" ] \
    || fail "mask_packaged_vsftpd DELETED an operator's own ${mask_path} instead of refusing. The refusal message is worth nothing if the file is gone by the time it is printed."
  grep -q 'an operator unit, not ours' "$mask_path" \
    || fail "the operator's own ${mask_path} was rewritten by mask_packaged_vsftpd"
  rm -f -- "$mask_path"

  # And the acceptance, so this function cannot pass against a guard that refuses everything.
  mask_packaged_vsftpd
  install_vsftpd_package
  [ -x /usr/sbin/vsftpd ] \
    || fail "after the mask was restored, install_vsftpd_package left no /usr/sbin/vsftpd — the gate now refuses a host it must accept"
  echo "install_vsftpd_package refuses an unmasked host by name, mask_packaged_vsftpd refuses an operator's own unit without deleting it, and both accept the real host."
}

# assert_ftps_group_is_created: the group exists and is a SYSTEM group.
#
# Membership of it is the entire authorization to authenticate through /etc/pam.d/maran-ftps,
# so the gid range is not cosmetic: a group in the login range is one `useradd` can hand out as
# a new account's primary group by accident. Fails if ensure_ftps_group stops running or loses
# its --system flag.
assert_ftps_group_is_created() {
  ensure_ftps_group
  getent group maran-ftps >/dev/null \
    || fail "89-ftps.sh did not create the maran-ftps group, which is the entire authorization to use FTPS"
  local gid
  gid="$(getent group maran-ftps | cut -d: -f3)"
  [ "$gid" -lt 1000 ] \
    || fail "maran-ftps is not a system group (gid ${gid}); groupadd --system is what keeps it out of the range a new login can be given by accident"

  # Idempotence, which an installer needs and which this function claims: a second call must
  # converge rather than fail on its own previous work, and must not make a second group.
  ensure_ftps_group
  local group_count
  group_count="$(getent group | awk -F: '$1 == "maran-ftps"' | wc -l)"
  [ "$group_count" -eq 1 ] \
    || fail "after two calls to ensure_ftps_group there are ${group_count} maran-ftps groups"
  echo "The maran-ftps group exists, is a system group (gid ${gid}), and a second call to ensure_ftps_group changes nothing."
}

# assert_the_ftps_artifacts_are_what_they_claim: the five things step 89 leaves on disk, each
# as one uid:gid:mode triple, with a symbolic link at any of the names refused outright.
#
# The jail base's row reads 711 and not 700, and the change of that one digit is worth a
# sentence, because this row PASSED throughout the whole life of the defect it was meant to
# cover. The base was created 0700, this row required 0700, the two agreed, and every FTPS
# login on every real install failed with `500 OOPS: cannot change directory` — vsftpd chdirs
# into the login's home AFTER dropping to the account's uid, and 0700 is exactly the mode that
# stops it. A row that reads the intended value back cannot see an intended value that is
# wrong. It is kept because it observes the OTHER half — that nothing widened the base to
# 0755 — and the half it cannot see is observed live, as an unprivileged uid, by
# assert_the_jail_base_is_traversable_by_an_unprivileged_uid below.
#
# One list and one loop, exactly like step 40's directory layout, and carrying the same vacuity
# guard for the same reason: a `for` over an empty list exits 0, so a path dropped from the list
# by a bad merge removes a check with no output changing anywhere. The count is the only thing
# that observes the list's SIZE. Raise it with the list.
assert_the_ftps_artifacts_are_what_they_claim() {
  local SCRIPT_DIR="$INSTALLER_ROOT"

  ensure_ftps_directories
  install_ftps_pam_service
  install_ftps_unit
  install_ftps_logrotate

  local entry checked=0
  for entry in \
    "d:/var/lib/maran-ftps:0:0:711" \
    "d:/etc/maran/vsftpd:0:0:755" \
    "f:/etc/pam.d/maran-ftps:0:0:644" \
    "f:/etc/systemd/system/maran-ftps.service:0:0:644" \
    "f:/etc/logrotate.d/maran-ftps:0:0:644"; do
    local kind="${entry%%:*}" rest="${entry#*:}"
    case "$kind" in
      d) assert_directory_is "${rest%%:*}" "${rest#*:}" ;;
      f) assert_file_is "${rest%%:*}" "${rest#*:}" ;;
    esac
    checked=$((checked + 1))
  done
  [ "$checked" -eq 5 ] \
    || fail "the FTPS artifact list checked ${checked} paths, not the 5 step 89 creates. A path has been dropped from the list, and a check that is not in the list is not a check that failed — it is one that no longer exists."

  echo "Step 89's five artifacts verified: two directories and three files, each with its own owner and mode, none of them a link."
}

# assert_ftps_jail_base_is_root_owned_outside_the_panel_state_root: where the jail base IS, which
# is a different question from its own mode and is the question that has already cost this
# repository a working feature.
#
# Step 40 creates /var/lib/maran owned by the service account, 0750, and the owner of a directory can rename a
# level aside and leave an entry of its own at the name every customer's jail hangs under — so a
# root-only directory underneath a panel-owned one is not root-only however it is moded. While
# the SFTP base was /var/lib/maran/sftp, sshd refused every login on every real install with
# `bad ownership or modes for chroot directory component "/var/lib/maran/"`. vsftpd's rule is not
# the same rule and is not weaker: it refuses to run at all with a chroot root the logged-in user
# can write to. A mode check on the base cannot see this defect; the path and the ancestor walk
# can.
assert_ftps_jail_base_is_root_owned_outside_the_panel_state_root() {
  ensure_ftps_directories

  # The step's OWN constant, not the literal, and this is the correction that makes the
  # assertion able to fail. Written against the literal /var/lib/maran-ftps it was BLIND to the
  # one mutation it exists to catch: moving MARAN_FTPS_JAIL_ROOT to /var/lib/maran/ftps left the
  # literal path uncreated, `cd` into it failed inside a command substitution — which `set -e`
  # does not see — the `case` matched an empty string, and the ancestor walk walked the parents
  # of a path that was not there and found them all root's. Measured as a SURVIVED mutant before
  # this line existed. The literal is asserted separately, in the artifact list above, where a
  # moved path fails as "was not created"; here the question is about the path the step actually
  # uses, so the step is what names it.
  local base="$MARAN_FTPS_JAIL_ROOT"
  [ -d "$base" ] \
    || fail "${base}, the jail base 89-ftps.sh names in MARAN_FTPS_JAIL_ROOT, is not a directory after ensure_ftps_directories ran"
  local resolved
  resolved="$(cd "$base" && pwd -P)" \
    || fail "${base} could not be resolved; every check below it would have been asked about nothing"
  case "$resolved" in
    /var/lib/maran/*) fail "the FTPS jail base resolves to ${resolved}, underneath /var/lib/maran, whose owner is the panel uid — an unprivileged uid can then replace a component of every account's chroot path, and vsftpd refuses a writable chroot root outright" ;;
  esac
  assert_ancestors_are_root_only "$resolved"
  echo "The FTPS jail base the step names (${base}) resolves to ${resolved}, a sibling of the panel state root and not a child of it, and every ancestor of it belongs to root and is writable by nobody else."
}

# assert_the_jail_base_is_traversable_by_an_unprivileged_uid: the property a mode row cannot
# see, asked the way the daemon asks it.
#
# It drives the step's OWN gate, `assert_jail_base_is_traversable_and_not_listable`, rather
# than re-implementing the question here — a polygon assertion that re-derives the answer is
# scoring its own copy of the logic and not the shipped one. What this function adds is the
# pair of controls that gate owes, because a refusing gate that has only ever been handed
# correct input is indistinguishable from one mutated to accept everything:
#
#   POSITIVE  at 0711 the gate must ACCEPT (the inverse control: it accepts something).
#   INVERSE A at 0700 it must REFUSE, naming the traversal — the shape of the live defect.
#   INVERSE B at 0755 it must REFUSE, naming the listing — the over-correction, which would
#             publish one directory name per hosting account to every local uid.
#
# Each call runs in a subshell because the gate exits on refusal, which is what it should do
# inside the installer and is not what a harness wants. UNOBSERVED HERE: an actual FTPS login.
# No daemon boots in an image build. What IS observed is the same syscall the login's chdir
# makes, performed by the same class of uid, which is the axis the defect lived on.
assert_the_jail_base_is_traversable_by_an_unprivileged_uid() {
  ensure_ftps_directories

  local mode outcome checked=0
  for mode in 0711 0700 0755; do
    chmod "$mode" /var/lib/maran-ftps
    outcome="$( (assert_jail_base_is_traversable_and_not_listable) 2>&1 )" && outcome="ACCEPTED
${outcome}" || outcome="REFUSED
${outcome}"
    case "$mode" in
      0711)
        case "$outcome" in
          ACCEPTED*) : ;;
          *) fail "the step's jail-base gate REFUSED the correct mode 0711. A gate that refuses everything passes every test that only hands it broken input, and this is the control that says it is not one. It said: $(echo "$outcome" | tail -n +2)" ;;
        esac
        ;;
      0700)
        case "$outcome" in
          REFUSED*"cannot traverse"*) : ;;
          *) fail "the step's jail-base gate did not refuse mode 0700 by naming the traversal. 0700 is the mode under which EVERY FTPS login fails with '500 OOPS: cannot change directory', and it is the mode this gate exists for. It said: ${outcome}" ;;
        esac
        ;;
      0755)
        case "$outcome" in
          REFUSED*"can LIST"*) : ;;
          *) fail "the step's jail-base gate did not refuse mode 0755 by naming the listing. The entries under this base are hosting account names; at 0755 every local uid can read them. It said: ${outcome}" ;;
        esac
        ;;
    esac
    checked=$((checked + 1))
  done
  [ "$checked" -eq 3 ] \
    || fail "the jail-base traversal controls ran ${checked} of the 3 modes. A control that is not in the loop is not a control that failed — it is one that no longer exists."

  # And the axis the three controls above CANNOT see, which is the axis the live defect lived
  # on: the mode the step ITSELF chooses. The controls hand the gate modes this function sets,
  # so they say the gate works and say nothing about what `ensure_ftps_directories` produces.
  # Measured as a SURVIVOR before this line existed: a mutant that made the step create 0700
  # again AND moved every mode literal in this file to agree with it — which is precisely the
  # shape of the shipped defect, an intended value that was wrong with every assertion
  # agreeing — left all three assertions green. This line asks the gate about the step's own
  # output and states no mode at all, so there is no literal for such a mutant to move.
  ensure_ftps_directories
  assert_jail_base_is_traversable_and_not_listable
  echo "The jail base is traversable and not listable by an unprivileged uid, asked as that uid: the step's own gate accepts 0711, refuses 0700 by naming the traversal, refuses 0755 by naming the listing, and accepts what ensure_ftps_directories actually creates."
  echo "UNOBSERVED HERE: an actual FTPS login — no daemon boots in an image build. The syscall the login's chdir makes is what was performed, by the same class of uid."
}

# assert_ftps_pam_stack_requires_group_membership: the CONTENT of the file that decides who may
# authenticate. What that content does is a separate assertion, and the one below this one.
#
# The name of this function used to be the whole defect. It says "requires", and it observes
# presence: every check in it is a search for a line, and a PAM stack is its control flow, not its
# lines. Prepending `auth sufficient pam_permit.so` to the shipped stack leaves both `required
# pam_succeed_if` lines byte-identical, passes every check here, and turns the daemon into one that
# accepts every account on the host with no password at all. So this function keeps the two
# properties a content check really can hold — the group test is present in both phases, and the
# host's aggregate stack is absent — and it no longer claims the property it cannot see;
# assert_the_pam_stack_answers_the_three_logins_the_group_gate_exists_for asks the library instead. The second is not tidiness — password-auth and common-auth pull in whatever
# authentication policy an operator has configured (pam_sss, a directory service), and including
# them would make this daemon's answer depend on configuration nobody wrote for it.
assert_ftps_pam_stack_requires_group_membership() {
  local SCRIPT_DIR="$INSTALLER_ROOT"
  install_ftps_pam_service

  local phase found=0
  for phase in auth account; do
    grep -Eq "^${phase}[[:space:]]+required[[:space:]]+pam_succeed_if\.so user ingroup maran-ftps" \
      /etc/pam.d/maran-ftps \
      || fail "/etc/pam.d/maran-ftps has no ${phase}-phase pam_succeed_if test for maran-ftps membership. Without the auth line every local shadow entry can authenticate to FTPS; without the account line the gate is passed once and never re-asked, and the account phase is the one that answers whether this user may use this service."
    found=$((found + 1))
  done
  [ "$found" -eq 2 ] \
    || fail "the PAM phase list checked ${found} phases, not the 2 this stack must carry the group test in"

  # The DIRECTIVES, never the whole file: the shipped stack argues in a comment why it uses
  # pam_unix.so "and not password-auth/common-auth", so a grep over the raw text matches the
  # explanation and refuses a correct file. Measured, as a red run of this very assertion.
  if ftps_pam_directives /etc/pam.d/maran-ftps | grep -Eq 'password-auth|common-auth|system-auth'; then
    fail "/etc/pam.d/maran-ftps pulls in the host's aggregate stack ($(ftps_pam_directives /etc/pam.d/maran-ftps | grep -E 'password-auth|common-auth|system-auth')). An FTPS login is a local shadow entry and nothing else; including the aggregate makes this daemon's answer depend on an operator's directory service, pam_faillock and whatever else is in there."
  fi

  # The positive control on the grep itself. Both patterns above are searches, and a search that
  # cannot match — a renamed file, a `grep` reading the wrong path — reports the same green as
  # one that matched. So the probe is pointed at a planted copy carrying exactly what it hunts
  # for and must FIND it, and then at a copy with the line removed and must NOT.
  local planted="/tmp/polygon-pam-probe"
  render_ftps_pam_service > "$planted"
  grep -Eq '^auth[[:space:]]+required[[:space:]]+pam_succeed_if\.so user ingroup maran-ftps' "$planted" \
    || fail "the probe cannot find the group test in a freshly rendered stack — it has been searching for something render_ftps_pam_service does not write, and its green above meant nothing"
  grep -v 'pam_succeed_if' "$planted" > "${planted}.stripped"
  if grep -Eq '^auth[[:space:]]+required[[:space:]]+pam_succeed_if\.so user ingroup maran-ftps' "${planted}.stripped"; then
    fail "the probe matched a stack with every pam_succeed_if line removed — it cannot tell the presence of the group test from its absence"
  fi
  # And the same for the aggregate-stack probe, which is a REFUSING check and therefore the one
  # that would silently stop refusing: an aggregate is planted as a real directive and the probe
  # must see it. Without this the comment-stripping above could go wrong in the other direction
  # and strip the whole file, and the check would pass on every host forever.
  printf '@include common-auth\n' >> "${planted}.stripped"
  ftps_pam_directives "${planted}.stripped" | grep -Eq 'password-auth|common-auth|system-auth' \
    || fail "the aggregate-stack probe cannot see an @include common-auth planted as a real directive — it has stopped being able to refuse the thing it exists to refuse"
  rm -f -- "$planted" "${planted}.stripped"

  echo "The PAM stack CONTAINS the maran-ftps membership test in both the auth and the account phase, and pulls in no aggregate stack; the probe was shown to find that line when it is there and to miss it when it is not."
  echo "UNOBSERVED HERE: whether that content makes the stack REQUIRE membership. Presence is not control flow — a 'sufficient' module in front of these lines leaves every one of them byte-identical and admits everybody, and this assertion cannot tell the two files apart. What the stack ACTUALLY answers is measured next, through the real libpam, by assert_the_pam_stack_answers_the_three_logins_the_group_gate_exists_for."
}

# compile_pam_witness: build docker/polygon/asserts/pam-witness.c against THIS family's own libpam.
#
# Compiled here rather than shipped as a binary for the reason the Dockerfiles compile the agent
# in each image: the two families ship different glibc and different PAM releases, and a binary
# built anywhere else is a binary that either refuses to start or links a library nobody runs.
#
# A missing compiler or a missing header is a named failure and never a skipped assertion: an
# authorization check that quietly did not run is the exact shape rules/testing.md forbids.
compile_pam_witness() {
  local compiler
  compiler="$(command -v cc || command -v gcc || true)"
  [ -n "$compiler" ] \
    || fail "no C compiler in this image, so the PAM witness cannot be built and the FTPS stack's actual answer cannot be observed. The Dockerfile must install one before this script runs."
  [ -f /usr/include/security/pam_appl.h ] \
    || fail "the PAM development headers are not in this image (/usr/include/security/pam_appl.h), so the PAM witness cannot be built. The Dockerfile must install them before this script runs: libpam0g-dev on the Debian family, pam-devel on the RHEL family."
  install -d -o root -g root -m 0755 "$(dirname -- "$PAM_WITNESS_BINARY")"
  "$compiler" -O0 -Wall -Wextra -Werror -o "$PAM_WITNESS_BINARY" "$PAM_WITNESS_SOURCE" -lpam \
    || fail "docker/polygon/asserts/pam-witness.c did not compile in this image. Nothing below it has measured anything, which is why this is a failure and not a warning."
  [ -x "$PAM_WITNESS_BINARY" ] \
    || fail "${PAM_WITNESS_BINARY} is not executable after the compiler reported success"
}

# pam_witness_says: what the named PAM service answers for one login and one password.
#
# One word plus the two codes, so a caller compares an ANSWER and a reader sees why. `BROKEN` is
# deliberately a third answer and not folded into `REFUSED`: pam_start failing is a harness fault,
# and reading it as a refusal is how a check that can no longer observe anything reports the
# strictest possible result and passes.
pam_witness_says() {
  local service="$1" user="$2" password="$3" status=0 output
  output="$("$PAM_WITNESS_BINARY" "$service" "$user" "$password" 2>&1)" || status=$?
  output="$(printf '%s' "$output" | tr '\n' ' ')"
  case "$status" in
    0) printf 'ACCEPTED %s' "$output" ;;
    1) printf 'REFUSED %s' "$output" ;;
    *) printf 'BROKEN exit %s: %s' "$status" "$output" ;;
  esac
}

# ftps_witness_cases: how many logins the assertion below actually put to the library.
#
# A module list PAM cannot load, a service file at a name nothing reads, a witness that exits
# before its first transaction — every one of those produces an assertion that runs no case and
# prints its success line. The count is the axis that goes blind, so the count is what is guarded.
ftps_witness_cases=0

# assert_pam_answer: one login through one service, against the answer it must get.
assert_pam_answer() {
  local expected="$1" service="$2" user="$3" password="$4" why="$5" answer
  answer="$(pam_witness_says "$service" "$user" "$password")"
  ftps_witness_cases=$((ftps_witness_cases + 1))
  case "$answer" in
    "${expected} "*) return 0 ;;
  esac
  fail "PAM service ${service} answered ${answer} for ${user}, and it must be ${expected}. ${why}"
}

# assert_the_pam_stack_answers_the_three_logins_the_group_gate_exists_for: what the real
# pam_authenticate does with the stack step 89 installed, asked through the real libpam.
#
# This is the assertion the one above it cannot be. `assert_ftps_pam_stack_requires_group_membership`
# reads the file, and a PAM stack is not its lines — it is their control flow. Measured on
# 2026-09-09 on both families: prepend `auth sufficient pam_permit.so` to the shipped stack, leave
# both `required pam_succeed_if` lines byte-identical, and every content check stays green while an
# account in no group, typing the WRONG password, is authenticated. That is a total authentication
# bypass under a green gate, and only the library can see it.
#
# The four cases against the shipped stack are the four answers the arrangement is made of:
#
#   member,  correct password  -> ACCEPTED   the daemon has to let its own customers in
#   outsider, correct password -> REFUSED    this is what the group gate IS. The outsider holds a
#                                            valid shadow entry; membership is the whole difference
#   outsider, wrong password   -> REFUSED    the case that catches the bypass mutant
#   member,   wrong password   -> REFUSED    pam_unix is really consulted; the group test is an
#                                            authorization gate and never a substitute for a password
#
# The fifth case is the inverse control, and it is the reviewer's mutant itself: the same
# directives with `sufficient pam_permit.so` prepended, planted under a service name of its own,
# where the outsider with a wrong password MUST be accepted. Without it the three refusals above
# would also be produced by a witness that can no longer authenticate anybody — a broken libpam, a
# throwaway account whose password never got set — and a gate that refuses everything passes every
# test that only ever hands it something it must refuse (rules/testing.md).
assert_the_pam_stack_answers_the_three_logins_the_group_gate_exists_for() {
  local SCRIPT_DIR="$INSTALLER_ROOT"
  ensure_ftps_group
  install_ftps_pam_service
  compile_pam_witness

  # Made here and removed at the end: these accounts exist for five PAM transactions and must not
  # be in the image any suite later runs against. `-M` so no home directory is created and `-N` so
  # neither gets a group of its own, which keeps the ONLY group difference between them the one
  # under test.
  userdel -f "$FTPS_WITNESS_MEMBER" >/dev/null 2>&1 || true
  userdel -f "$FTPS_WITNESS_OUTSIDER" >/dev/null 2>&1 || true
  useradd -M -N "$FTPS_WITNESS_MEMBER" \
    || fail "could not create the throwaway FTPS member account ${FTPS_WITNESS_MEMBER}"
  useradd -M -N "$FTPS_WITNESS_OUTSIDER" \
    || fail "could not create the throwaway FTPS outsider account ${FTPS_WITNESS_OUTSIDER}"
  usermod -aG "$MARAN_FTPS_GROUP" "$FTPS_WITNESS_MEMBER" \
    || fail "could not add ${FTPS_WITNESS_MEMBER} to ${MARAN_FTPS_GROUP}"
  printf '%s:%s\n' "$FTPS_WITNESS_MEMBER" "$FTPS_WITNESS_PASSWORD" | chpasswd \
    || fail "could not set the throwaway password on ${FTPS_WITNESS_MEMBER}"
  printf '%s:%s\n' "$FTPS_WITNESS_OUTSIDER" "$FTPS_WITNESS_PASSWORD" | chpasswd \
    || fail "could not set the throwaway password on ${FTPS_WITNESS_OUTSIDER}"

  # The premise of every case below, checked rather than assumed: the two accounts differ by that
  # membership and by nothing else the stack asks about. A refusal for the outsider means nothing
  # if the outsider was never given a password, and an acceptance for the member means nothing if
  # the member is not actually in the group.
  id -nG "$FTPS_WITNESS_MEMBER" | tr ' ' '\n' | grep -qx "$MARAN_FTPS_GROUP" \
    || fail "${FTPS_WITNESS_MEMBER} is not in ${MARAN_FTPS_GROUP} after usermod -aG, so the accepted case below would prove nothing about membership"
  if id -nG "$FTPS_WITNESS_OUTSIDER" | tr ' ' '\n' | grep -qx "$MARAN_FTPS_GROUP"; then
    fail "${FTPS_WITNESS_OUTSIDER} IS in ${MARAN_FTPS_GROUP}, so the refusals below would be refusals of a member and would say nothing about the group gate"
  fi

  # The inverse control's stack: the shipped directives, untouched, with a `sufficient` short
  # circuit in front of them. This is the file the content check above cannot tell from the real
  # one.
  local mutant_path="/etc/pam.d/${FTPS_MUTANT_PAM_SERVICE}"
  {
    printf 'auth sufficient pam_permit.so\n'
    printf 'account sufficient pam_permit.so\n'
    cat "$MARAN_FTPS_PAM_SERVICE"
  } > "$mutant_path"
  chmod 0644 "$mutant_path"

  local service
  service="$(basename -- "$MARAN_FTPS_PAM_SERVICE")"

  assert_pam_answer ACCEPTED "$service" "$FTPS_WITNESS_MEMBER" "$FTPS_WITNESS_PASSWORD" \
    "A member of ${MARAN_FTPS_GROUP} holding the right password is the login this daemon exists to serve; a stack that refuses it refuses every customer."
  assert_pam_answer REFUSED "$service" "$FTPS_WITNESS_OUTSIDER" "$FTPS_WITNESS_PASSWORD" \
    "This is the whole reason the group gate exists: an account that holds a VALID local password and is not in ${MARAN_FTPS_GROUP} must not reach this daemon. Every other login on the host — root, the panel uid, every system account — is refused by the same line."
  assert_pam_answer REFUSED "$service" "$FTPS_WITNESS_OUTSIDER" "$FTPS_WITNESS_WRONG_PASSWORD" \
    "A non-member with a wrong password being accepted is the shape of a total authentication bypass: it is what a 'sufficient' module in front of the required ones produces, with every line of this file still present and every content check still green."
  assert_pam_answer REFUSED "$service" "$FTPS_WITNESS_MEMBER" "$FTPS_WITNESS_WRONG_PASSWORD" \
    "Membership authorizes; it does not authenticate. If a member gets in with the wrong password, pam_unix.so is no longer being consulted and the group test has become the only thing between the network and the account."
  assert_pam_answer ACCEPTED "$FTPS_MUTANT_PAM_SERVICE" "$FTPS_WITNESS_OUTSIDER" "$FTPS_WITNESS_WRONG_PASSWORD" \
    "This is the inverse control. A stack with 'sufficient pam_permit.so' in front of the shipped directives MUST let a non-member in with a wrong password; a witness that refuses even that one is refusing everything, and the three refusals above were measuring nothing."

  [ "$ftps_witness_cases" -eq 5 ] \
    || fail "the PAM witness put ${ftps_witness_cases} logins to the library, not the 5 this assertion is made of. A case that is not run is not a case that passed — it is one that no longer exists."

  rm -f -- "$mutant_path" "$PAM_WITNESS_BINARY"
  userdel -f "$FTPS_WITNESS_MEMBER" >/dev/null 2>&1 || true
  userdel -f "$FTPS_WITNESS_OUTSIDER" >/dev/null 2>&1 || true

  # The mail spool, deleted by name, and it is not tidiness. The RHEL family's `useradd` creates
  # /var/spool/mail/<login> even under `-M`, the Debian family's does not, and `userdel -f` leaves
  # it behind on both. Measured: the alma9 image shipped for a while with two 0-byte spool files
  # owned by the raw uids 1000 and 1001 — the numbers these two accounts held — after the accounts
  # themselves were long gone. A later suite's account that lands on a recycled uid then finds a
  # file it OWNS at a path it never created, which is exactly the kind of state a polygon image is
  # supposed not to carry between suites. `userdel -r` is not the fix: these accounts are made with
  # `-M`, so `-r` would spend its time complaining about a home directory that was never there.
  local spool
  for spool in "$FTPS_WITNESS_MEMBER" "$FTPS_WITNESS_OUTSIDER"; do
    rm -f -- "/var/spool/mail/${spool}" "/var/mail/${spool}"
  done

  local leftover
  for leftover in "$FTPS_WITNESS_MEMBER" "$FTPS_WITNESS_OUTSIDER"; do
    if getent passwd "$leftover" >/dev/null; then
      fail "the throwaway account ${leftover} outlived the assertion that made it and would be in this image, where every later suite would meet it"
    fi
    # The guard is widened to the axis that actually went blind. `getent passwd` answered
    # correctly all along while two files named after these accounts sat in the image, so a
    # deletion check that only asks the passwd database is one that agrees with the defect.
    if [ -e "/var/spool/mail/${leftover}" ] || [ -e "/var/mail/${leftover}" ]; then
      fail "the throwaway account ${leftover} is gone from /etc/passwd but its mail spool is still in this image, owned by a uid that is now free to be handed to a later suite's account"
    fi
  done

  echo "The real pam_authenticate/pam_acct_mgmt, against the stack step 89 installed at ${MARAN_FTPS_PAM_SERVICE}, in 5 transactions: a ${MARAN_FTPS_GROUP} member with the right password is ACCEPTED; a non-member with the RIGHT password is REFUSED; a non-member with a wrong password is REFUSED; a member with a wrong password is REFUSED; and the same directives with 'sufficient pam_permit.so' prepended ACCEPT the non-member with the wrong password, which is what proves the four answers above were the stack's and not the witness's."
  echo "UNOBSERVED HERE: everything the DAEMON does around those two calls — the TLS handshake, the chroot into the jail, the data channel, and vsftpd's own pam_service_name reaching this file. No daemon boots in an image build; ftps_on_a_real_host.rs is where the login itself is proven."
}

# agent_string_after: the first string literal following a marker in a Rust source, read with the
# file flattened to one line.
#
# Flattened on purpose: a declaration rustfmt wrapped across lines is invisible to a line-oriented
# regex, and a constant that silently retires from a drift check is worse than one that was never
# in it. The same defect, and the same fix, as the `ReadWritePaths=` comparison in
# scripts/lib/check-structure.sh.
agent_string_after() {
  local marker="$1" file="$2"
  # `|| true` and not a bare grep: this script runs under `set -o pipefail`, so a grep that
  # matches nothing would make the ASSIGNMENT in the caller fail and kill the script with no
  # message at all. Measured, on the mutant that renames the function this hunts for: the run
  # exited 1 having printed nothing, which is the one outcome worse than a false green — a check
  # that failed for a reason nobody can read. An empty answer is returned instead, and every
  # caller turns it into a named failure.
  tr '\n' ' ' < "$file" | { grep -o "${marker}[^\"]*\"[^\"]*\"" || true; } | head -1 \
    | sed 's/.*"\(.*\)"$/\1/'
}

# assert_the_installer_and_the_agent_spell_the_ftps_names_the_same: the seam between the two halves
# of FTPS, which until this existed was three literals each asserted only against itself.
#
# The arrangement needs both halves to agree on three names. The installer creates the group and
# writes /etc/pam.d/<service>; the agent adds each login to that group, renders `pam_service_name`
# into the vsftpd configuration and builds every jail under the same root. If either side's spelling
# moves, the installer creates group A, the agent adds logins to group B, the PAM stack tests group
# A — and EVERY FTPS login on EVERY install fails while every gate in this repository stays green.
# Nothing observed that, and installer/lib/89-ftps.sh claimed that these images did.
#
# What it compares: the agent's SOURCE TEXT against the installer's own constants, read from the
# step this script has already sourced. What it therefore cannot see is stated in its output — a
# value the agent computes rather than declares, and anything the compiler would do to these
# literals. The agent's own unit tests pin them from the other side
# (agent/crates/distro/src/tests/*/\*_adapter_tests.rs, agent/crates/agent-core/src/tests/agent_paths_tests.rs),
# which is what makes this a comparison of two independent statements rather than of one restated.
assert_the_installer_and_the_agent_spell_the_ftps_names_the_same() {
  local compared=0 value planted

  # The positive control first, and on the axis that can go blind: the extractor. If it returned
  # the empty string for every input — a marker that no longer matches, a moved file — every
  # comparison below would be `"" = ""` against a value read the same way, or would fail for a
  # reason that reads like drift. So it is pointed at a copy carrying a value that is deliberately
  # NOT the shipped one, and it must report that value back.
  #
  # The sentinel is derived from the file itself and NOT from the installer's constant, which was
  # the first version of this control and was wrong: with the installer's own spelling mutated, the
  # planted `sed` matched nothing and the control failed — a red run, but one that blamed the
  # extractor for the drift the check exists to name. A control must fail for its own reason only.
  planted="/tmp/polygon-agent-name-probe.rs"
  value="$(agent_string_after 'fn ftps_group()' "$AGENT_DEBIAN_SERVICES")"
  [ -n "$value" ] \
    || fail "no string literal at all could be read out of ftps_group() in ${AGENT_DEBIAN_SERVICES}. The extractor is not reading this file, so every agreement reported below would be an agreement between two empty strings (rules/testing.md)."
  sed "s/\"${value}\"/\"polygon-planted-name\"/" "$AGENT_DEBIAN_SERVICES" > "$planted"
  [ "$(agent_string_after 'fn ftps_group()' "$planted")" = "polygon-planted-name" ] \
    || fail "the Rust literal extractor read '${value}' out of debian_services.rs but does not read 'polygon-planted-name' out of a copy carrying exactly that instead — it is not reading the file's content, and its agreements mean nothing."
  rm -f -- "$planted"

  # 1 and 2: the group, on each family's adapter. Both, and not one of them, because the two
  # families answer it independently and a drift on either is a family whose logins are added to a
  # group the PAM stack does not test.
  local services_file
  for services_file in "$AGENT_DEBIAN_SERVICES" "$AGENT_RHEL_SERVICES"; do
    value="$(agent_string_after 'fn ftps_group()' "$services_file")"
    [ -n "$value" ] \
      || fail "no string literal could be read out of ftps_group() in ${services_file} — this comparison had nothing to compare, which is a failure to observe and not an agreement (rules/testing.md)"
    [ "$value" = "$MARAN_FTPS_GROUP" ] \
      || fail "the FTPS group has drifted: installer/lib/89-ftps.sh creates '${MARAN_FTPS_GROUP}' and ${services_file} adds logins to '${value}'. Every FTPS login on this family would be refused by a PAM stack testing a group nobody is in."
    compared=$((compared + 1))
  done

  # 3: the jail root. The agent builds /<root>/<account> and bind-mounts the home beneath it; the
  # installer creates the root, with the mode that decides whether the daemon can traverse it.
  value="$(agent_string_after 'FTPS_JAIL_ROOT' "$AGENT_PATHS")"
  [ -n "$value" ] \
    || fail "no string literal could be read for FTPS_JAIL_ROOT in ${AGENT_PATHS} — this comparison had nothing to compare (rules/testing.md)"
  [ "$value" = "$MARAN_FTPS_JAIL_ROOT" ] \
    || fail "the FTPS jail root has drifted: installer/lib/89-ftps.sh creates '${MARAN_FTPS_JAIL_ROOT}' and AgentPaths::FTPS_JAIL_ROOT is '${value}'. The agent would mount every account's jail under a directory the installer never made, root-owned and traversable."
  compared=$((compared + 1))

  # 4: the PAM service. `pam_service_name` in the rendered vsftpd configuration is a NAME that
  # libpam resolves under /etc/pam.d; the installer writes the file. They must be the same name.
  # `|| true` for the same pipefail reason as the extractor above: a template that has lost the
  # line must produce the named failure below, never a script that dies without printing one.
  value="$( { grep -E '^pam_service_name=' "$AGENT_VSFTPD_TEMPLATE" || true; } | tail -1 | cut -d= -f2-)"
  [ -n "$value" ] \
    || fail "no pam_service_name= line in ${AGENT_VSFTPD_TEMPLATE} — the daemon's configuration no longer names a PAM service where this check can read it, so nothing compares it against the file installer/lib/89-ftps.sh writes (rules/testing.md)"
  case "$value" in
    *'{'*) fail "pam_service_name in ${AGENT_VSFTPD_TEMPLATE} is rendered from a template expression ('${value}'), so its value is not readable here. Whatever decides it must be compared against installer/lib/89-ftps.sh's MARAN_FTPS_PAM_SERVICE somewhere that can see it — this check cannot, and must not pretend to." ;;
  esac
  [ "$value" = "$(basename -- "$MARAN_FTPS_PAM_SERVICE")" ] \
    || fail "the FTPS PAM service name has drifted: the agent renders pam_service_name=${value} and installer/lib/89-ftps.sh writes ${MARAN_FTPS_PAM_SERVICE}. vsftpd would resolve /etc/pam.d/${value}, which is the distribution's file or no file at all, and the group gate would not be in the path of a single login."
  compared=$((compared + 1))

  # 5: and the directory that name is resolved under. A service NAME is only half of a path.
  [ "$(dirname -- "$MARAN_FTPS_PAM_SERVICE")" = "/etc/pam.d" ] \
    || fail "installer/lib/89-ftps.sh writes its PAM stack to ${MARAN_FTPS_PAM_SERVICE}, outside /etc/pam.d, where libpam resolves a pam_service_name. The stack would not be read by anything."
  compared=$((compared + 1))

  # 6: the transfer log path, read from the agent first. Its own planted control, on its own
  # marker: `agent_string_after` is only as good as the marker it is given, and a marker that has
  # stopped matching returns the empty string, which every comparison below would then be
  # comparing against. The sentinel is derived from the file's own value for the same reason the
  # first control is -- a control must fail for its own reason only.
  local expected_log
  expected_log="$(agent_string_after 'FTPS_LOG_PATH' "$AGENT_ENABLE_FTPS")"
  [ -n "$expected_log" ] \
    || fail "no string literal could be read for FTPS_LOG_PATH in ${AGENT_ENABLE_FTPS} — the path the agent renders into vsftpd_log_file is not where this check can read it, so nothing below compared anything (rules/testing.md)"
  case "$expected_log" in
    /*.log) ;;
    *) fail "FTPS_LOG_PATH in ${AGENT_ENABLE_FTPS} reads '${expected_log}', which is not an absolute path ending in .log. Either the constant has become something this extractor cannot read, or the daemon is being pointed somewhere unexpected; either way the comparisons below would be comparing the wrong string." ;;
  esac
  planted="/tmp/polygon-agent-log-probe.rs"
  sed "s|\"${expected_log}\"|\"/var/log/polygon-planted/probe.log\"|" "$AGENT_ENABLE_FTPS" > "$planted"
  [ "$(agent_string_after 'FTPS_LOG_PATH' "$planted")" = "/var/log/polygon-planted/probe.log" ] \
    || fail "the Rust literal extractor read '${expected_log}' out of enable_ftps.rs but does not read '/var/log/polygon-planted/probe.log' out of a copy carrying exactly that instead — it is not reading this file's content, and its agreements mean nothing."
  rm -f -- "$planted"

  # 7: the rotation policy rotates THAT file. One stanza, and its header is the agent's path.
  #
  # The stanza count is part of the comparison and not pedantry: a second stanza would make
  # `head -1` pick whichever came first, so a policy that had grown a second block could agree
  # here while rotating something else. `|| true` for the pipefail reason the extractor documents.
  local stanza_lines stanza_count stanza_path
  stanza_lines="$( { grep -E '^[[:space:]]*/[^[:space:]]+[[:space:]]*\{[[:space:]]*$' "$FTPS_LOGROTATE_SOURCE" || true; } )"
  stanza_count="$(printf '%s' "$stanza_lines" | grep -c . || true)"
  [ "$stanza_count" -eq 1 ] \
    || fail "installer/logrotate/maran-ftps has ${stanza_count} rotation stanzas, not the 1 this comparison is written for. With none, nothing rotates the FTPS transfer log at all; with more, the file this check reads is not necessarily the one that rotates it (rules/testing.md)."
  stanza_path="$(printf '%s\n' "$stanza_lines" | sed 's/[[:space:]]*{[[:space:]]*$//' | tr -d '[:space:]')"
  [ "$stanza_path" = "$expected_log" ] \
    || fail "the FTPS transfer log path has drifted: the agent renders vsftpd_log_file=${expected_log} and installer/logrotate/maran-ftps rotates ${stanza_path}. vsftpd would write one line per login and per transfer, from every customer, into a file nothing bounds — fastest on exactly the host where FTPS is used most — until the operator's partition fills and takes the panel and every site on it down. Nothing else reports this."
  compared=$((compared + 1))

  # 8, 9 and 10: the census. Not "the two copies agree" but "every copy agrees", derived from the
  # files rather than listed here, so a THIRD site added tomorrow is caught by name instead of by
  # luck. Every log path under /var/log written anywhere in the shipped rotation policy, in the
  # installer step that installs it, or in the agent file that declares the constant -- comments
  # included, because a comment that names the wrong path is what the next person edits from.
  local census_file census_here mention
  for census_file in "$FTPS_LOGROTATE_SOURCE" "$FTPS_STEP" "$AGENT_ENABLE_FTPS"; do
    census_here=0
    while IFS= read -r mention; do
      [ -n "$mention" ] || continue
      census_here=$((census_here + 1))
      [ "$mention" = "$expected_log" ] \
        || fail "${census_file} names the FTPS transfer log as '${mention}', and the agent renders '${expected_log}'. Two spellings of the one file this product writes for FTPS: whichever of them the rotation policy does not name grows without bound."
    done < <( { grep -oE '/var/log/[A-Za-z0-9_./-]*\.log' "$census_file" || true; } )
    # The vacuity guard, per file, on the axis that can go blind: a file that names the path
    # nowhere agrees with everything. Each of these three names it at least once today, and a
    # file that has stopped naming it has either lost the line or spelled it in a way this
    # census cannot see -- both are failures to observe (rules/testing.md).
    [ "$census_here" -gt 0 ] \
      || fail "no log path under /var/log appears anywhere in ${census_file}, so its agreement with the agent's '${expected_log}' was an agreement with nothing (rules/testing.md)"
    compared=$((compared + 1))
  done

  # 11: the FIFTH name, and the one nothing read until this block existed: the path of the
  # vsftpd.conf this panel's daemon is started against. It is spelled three times, independently —
  # the agent DECLARES it (`AgentPaths::VSFTPD_CONFIG_PATH`) and both writes and reads it back
  # there; installer/systemd/maran-ftps.service hands it to vsftpd on its ExecStart line; and
  # installer/lib/89-ftps.sh creates the DIRECTORY it lands in. Before this, the only thing in the
  # repository that mentioned all three together was a comment, and
  # docker/polygon/asserts/assert-installer-steps.sh's own artifact table restated the directory literal
  # against itself, which is not a comparison.
  #
  # Why the drift is silent, which is why it is worth a check rather than a comment. If the AGENT's
  # path moves and the unit's does not, the agent renders a config the daemon never reads and the
  # daemon starts against whatever is still at the old path — on any host where FTPS was enabled
  # once, that is a PREVIOUS render, so the panel reports the settings it just wrote while the
  # running daemon serves the old ones. The old ones may have `force_local_logins_ssl=NO`.
  # Every unit test on both sides still passes: each half is consistent with itself.
  local expected_config config_dir
  expected_config="$(agent_string_after 'VSFTPD_CONFIG_PATH' "$AGENT_PATHS")"
  [ -n "$expected_config" ] \
    || fail "no string literal could be read for VSFTPD_CONFIG_PATH in ${AGENT_PATHS} — the path the agent writes the daemon's configuration to is not where this check can read it, so nothing below compared anything (rules/testing.md)"
  case "$expected_config" in
    /*.conf) ;;
    *) fail "VSFTPD_CONFIG_PATH in ${AGENT_PATHS} reads '${expected_config}', which is not an absolute path ending in .conf. Either the constant has become something this extractor cannot read, or the daemon is being pointed somewhere unexpected; either way the comparisons below would be comparing the wrong string." ;;
  esac
  # Its own planted control, on its own marker, for the reason the two above carry one: an
  # extractor that has stopped matching returns the empty string, and the guard above would then
  # be the only thing between that and four agreements between nothing.
  planted="/tmp/polygon-agent-config-probe.rs"
  sed "s|\"${expected_config}\"|\"/etc/polygon-planted/probe.conf\"|" "$AGENT_PATHS" > "$planted"
  [ "$(agent_string_after 'VSFTPD_CONFIG_PATH' "$planted")" = "/etc/polygon-planted/probe.conf" ] \
    || fail "the Rust literal extractor read '${expected_config}' out of agent_paths.rs but does not read '/etc/polygon-planted/probe.conf' out of a copy carrying exactly that instead — it is not reading this file's content, and its agreements mean nothing."
  rm -f -- "$planted"
  config_dir="$(dirname -- "$expected_config")"
  [ "$config_dir" = "$MARAN_FTPS_CONFIG_DIR" ] \
    || fail "the FTPS configuration directory has drifted: the agent writes '${expected_config}' and installer/lib/89-ftps.sh creates '${MARAN_FTPS_CONFIG_DIR}'. The agent's safe_write would be renaming into a directory the installer never made, so enabling FTPS fails on every install — or, worse, succeeds into a directory somebody else owns."
  compared=$((compared + 1))

  # 12: the unit hands the daemon THAT file. Matched as a whole ARGUMENT and never as a substring:
  # a substring test passes a deliberately corrupted path ('/etc/maran/vsftpd/vsftpd.conf.bak'
  # contains the shipped spelling), which is precisely the trap that let a corrupted path through a
  # sibling check in this file. The ExecStart line is split on whitespace and one of its words must
  # equal the agent's constant exactly.
  local exec_line word exec_names_config=0
  exec_line="$( { grep -E '^ExecStart=' "$FTPS_UNIT_SOURCE" || true; } | tail -1)"
  [ -n "$exec_line" ] \
    || fail "no ExecStart= line in ${FTPS_UNIT_SOURCE} — the unit this installer installs starts nothing, and nothing below compared the configuration path it would have handed the daemon (rules/testing.md)"
  for word in ${exec_line#ExecStart=}; do
    [ "$word" = "$expected_config" ] && exec_names_config=1
  done
  [ "$exec_names_config" -eq 1 ] \
    || fail "the FTPS configuration path has drifted: the agent writes '${expected_config}' and installer/systemd/maran-ftps.service execs '${exec_line}'. The daemon would be started against a file the agent does not write — on a host where FTPS has been enabled before, a PREVIOUS render — so the panel would report the configuration it just saved while vsftpd served the old one, forced TLS included."
  compared=$((compared + 1))

  # 13, 14 and 15: the same census shape as the log path, on this path's own prefix. Every mention
  # of anything under the agent's configuration directory, in the installer step that creates it,
  # in the unit that reads it and in the agent file that declares it — comments included, because a
  # comment naming the wrong path is what the next person edits from. A fourth site added tomorrow
  # is caught by name rather than by luck. The prefix is DERIVED from the agent's constant and not
  # written here, so a wholesale move of the directory empties the census in the other two files
  # and trips the vacuity guard by name instead of quietly matching nothing.
  for census_file in "$FTPS_STEP" "$FTPS_UNIT_SOURCE" "$AGENT_PATHS"; do
    census_here=0
    while IFS= read -r mention; do
      [ -n "$mention" ] || continue
      census_here=$((census_here + 1))
      case "$mention" in
        "$expected_config"|"$config_dir") ;;
        *) fail "${census_file} names '${mention}' under the agent's FTPS configuration directory, and the agent declares '${expected_config}'. Two spellings of the one file this daemon is started against: whichever of them the daemon reads is not necessarily the one the agent writes." ;;
      esac
    # The trailing `sed` strips sentence punctuation, and it is not cosmetic: two of these three
    # files name the path inside English prose, so `... does not write /etc/maran/vsftpd/vsftpd.conf.`
    # yields a mention one byte longer than the constant and the census fails on a full stop. What it
    # costs is the ability to see a drift to a path that genuinely ENDS in punctuation, which the
    # dirname and ExecStart comparisons above would refuse anyway.
    done < <( { grep -oE "${config_dir}[A-Za-z0-9_./-]*" "$census_file" || true; } | sed 's/[.,;:)]\+$//' )
    [ "$census_here" -gt 0 ] \
      || fail "nothing under '${config_dir}' appears anywhere in ${census_file}, so its agreement with the agent's '${expected_config}' was an agreement with nothing (rules/testing.md)"
    compared=$((compared + 1))
  done

  [ "$compared" -eq 14 ] \
    || fail "the name-agreement check made ${compared} comparisons, not the 14 it is made of. A comparison that is not made is not one that agreed."

  echo "The installer and the agent spell the five FTPS names identically, compared and not assumed: the group '${MARAN_FTPS_GROUP}' against ftps_group() on BOTH family adapters, the jail root '${MARAN_FTPS_JAIL_ROOT}' against AgentPaths::FTPS_JAIL_ROOT, the PAM service '$(basename -- "$MARAN_FTPS_PAM_SERVICE")' against the pam_service_name the agent renders — under /etc/pam.d, where libpam resolves it — and the transfer log '${expected_log}', against the one stanza installer/logrotate/maran-ftps rotates and against EVERY mention of a log path in that file, in installer/lib/89-ftps.sh and in the agent file that declares it — and the daemon's configuration path '${expected_config}', against the directory installer/lib/89-ftps.sh creates, against the whole ExecStart ARGUMENT installer/systemd/maran-ftps.service hands vsftpd, and against EVERY mention of that directory in those two files and in agent_paths.rs."
  echo "UNOBSERVED HERE: this reads the agent's SOURCE TEXT, not the compiled agent. A name a future adapter computes instead of declaring, or a copy of any of these strings outside the files read here, is invisible to it; the agent's own unit tests pin these literals from the other side. For the log path specifically, two more things it cannot see: a path both halves spell identically and that the daemon still cannot write (a missing directory, a mode, an SELinux label) — ftps_on_a_real_host.rs is where the daemon's own answer is observed — and WHAT logrotate does with the file, since this compares the name in the stanza header and judges no directive inside it."
}

# assert_a_planted_symlink_at_the_jail_base_is_replaced_not_followed: the inverse control for
# ensure_ftps_directories.
#
# `install -d` alone exits 0 on a symbolic link to an existing directory and applies the
# ownership and the mode to the link's TARGET — so a step that only ever met a clean host would
# pass every check while every account's jail hung under a path somebody else chose. Each half
# below asserts on an axis that can actually go red: the link is gone, what stands at the name is
# a real root-owned 0711 directory, and the link's target was never written into — following the
# link would have chmod'd or populated /tmp/planted, so its emptiness and its unchanged mode are
# the observation that nothing went through it.
assert_a_planted_symlink_at_the_jail_base_is_replaced_not_followed() {
  rm -rf -- /var/lib/maran-ftps /tmp/planted
  install -d -o root -g root -m 0755 /tmp/planted
  ln -s /tmp/planted /var/lib/maran-ftps

  ensure_ftps_directories

  if [ -L /var/lib/maran-ftps ]; then
    fail "ensure_ftps_directories left the planted symlink at /var/lib/maran-ftps in place — every account jail would be created wherever it points"
  fi
  [ -d /var/lib/maran-ftps ] \
    || fail "/var/lib/maran-ftps is not a real directory after the planted link was replaced"
  [ "$(stat -c '%U:%G %a' /var/lib/maran-ftps)" = "root:root 711" ] \
    || fail "the replaced jail base is $(stat -c '%U:%G %a' /var/lib/maran-ftps), not root:root 711"
  [ -z "$(ls -A /tmp/planted)" ] \
    || fail "ensure_ftps_directories wrote through the symlink: /tmp/planted now holds $(ls -A /tmp/planted)"
  [ "$(stat -c '%a' /tmp/planted)" = "755" ] \
    || fail "ensure_ftps_directories chmod'd through the symlink: /tmp/planted is now mode $(stat -c '%a' /tmp/planted), not the 755 it was planted with"
  rm -rf -- /tmp/planted
  echo "A symbolic link planted at the jail base is replaced by a real root:root 0711 directory, and nothing was written or chmod'd through it."
}

# assert_the_ftps_step_says_it_is_off_and_can_see_when_it_is_not: report_ftps_is_installed_and_off
# run against the real host, and then against the two hosts it exists to refuse.
#
# The step's own report is a gate, and a gate that has only ever seen a good host is one that
# passes even when it refuses nothing. Its two answerable questions are both symlink facts — the
# mask, and any .wants entry naming maran-ftps.service — so both can be broken here and both must
# produce a named refusal. Its third line, the port, is decoration in an image build and the
# report says so itself; that is quoted rather than counted.
assert_the_ftps_step_says_it_is_off_and_can_see_when_it_is_not() {
  local status output wants_directory="/etc/systemd/system/multi-user.target.wants"

  report_ftps_is_installed_and_off \
    || fail "report_ftps_is_installed_and_off refused the host step 89 itself had just built"

  # Enabled: a .wants entry naming our unit is what "somebody turned FTPS on" looks like on
  # disk, and this step promises to leave it off.
  install -d -o root -g root -m 0755 "$wants_directory"
  ln -sfn /etc/systemd/system/maran-ftps.service "${wants_directory}/maran-ftps.service"
  status=0
  output="$( ( report_ftps_is_installed_and_off ) 2>&1 )" || status=$?
  rm -f -- "${wants_directory}/maran-ftps.service"
  [ "$status" -ne 0 ] \
    || fail "report_ftps_is_installed_and_off accepted a host where maran-ftps.service is ENABLED. FTPS ships switched off; enabling it is an administrator action taken in the panel, with its firewall consequences shown first."
  case "$output" in
    *"is ENABLED"*) ;;
    *) fail "report_ftps_is_installed_and_off refused an enabled unit, but not for the reason it should have:
${output}" ;;
  esac

  # Unmasked: the other half, and the more dangerous one.
  local mask_path="/etc/systemd/system/vsftpd.service"
  rm -f -- "$mask_path"
  status=0
  output="$( ( report_ftps_is_installed_and_off ) 2>&1 )" || status=$?
  [ "$status" -ne 0 ] \
    || fail "report_ftps_is_installed_and_off accepted a host whose packaged vsftpd.service is not masked"
  case "$output" in
    *"is not masked"*) ;;
    *) fail "report_ftps_is_installed_and_off refused an unmasked host, but not for the reason it should have:
${output}" ;;
  esac
  mask_packaged_vsftpd

  report_ftps_is_installed_and_off \
    || fail "after both plants were removed, report_ftps_is_installed_and_off refuses a host it must accept"
  echo "The step's own off-report accepts the host it built, and refuses by name both an enabled maran-ftps.service and an unmasked packaged unit."
}

# ---------------------------------------------------------------------------------------------
# The process-signalling census. See
# assert_every_supported_family_installs_the_process_signalling_tool.
# ---------------------------------------------------------------------------------------------

# The uid the behavioural half hands `pkill`, and the reason it is this one.
#
# It must match NOTHING: this runs inside a build layer where the assertions above have started a
# database and an sshd, and a cull that matched a real uid here would kill them. 4294967294 is
# (uint32)-2 — the `nobody` uid on no supported family, allocated to no account by any of them, and
# the value `pkill` itself refuses to treat as a wildcard. What is asserted is therefore the
# "nothing matched" answer, which is the answer `ops::logins::end_account_sessions` treats as a
# SUCCESS (its `NOTHING_MATCHED: i32 = 1`), and the flags it is asked for are that operation's own
# argv in that operation's own order.
readonly SIGNALLING_PROBE_UID="4294967294"

# dependency_step_family_arms: the `case "$MARAN_OS_FAMILY"` arm labels of one function in
# installer/lib/20-dependencies.sh, one per line, with the `*)` catch-all left out.
#
# The file is a PARAMETER with the shipped step as its default, so the planted control below can
# point the same reader at a copy. `$DEPENDENCIES_STEP` is readonly, which is right — the census
# must not be able to move the step out from under itself — and a command-prefix assignment to a
# readonly name is a shell error, not an override.
#
# This is the enumerator the census is built on, and the reason the check is a census rather than
# "both families name a package". A list of families written HERE would agree with the installer
# the day it was typed and be silent on the third family, which is the same defect one release
# later — so the families are read out of the installer's own arms, and a family added there
# tomorrow is caught BY NAME by the comparison below.
#
# It reads the arms between the function's opening line and the first line that is a bare `}` in
# column one, which is how every function in that file ends, and it accepts only the lowercase
# labels the file uses. A label spelled some other way — a glob, two labels joined by `|`, a
# variable — is invisible to it, and the assertion says so in its output.
dependency_step_family_arms() {
  local function_name="$1" file="${2:-$DEPENDENCIES_STEP}"
  awk -v fn="${function_name}() {" '
    index($0, fn) == 1 { inside = 1; next }
    inside && $0 == "}" { inside = 0 }
    inside { print }
  ' "$file" \
    | { grep -oE '^[[:space:]]+[a-z][a-z0-9_]*\)' || true; } \
    | sed 's/[[:space:])]//g' \
    | sort -u
}
