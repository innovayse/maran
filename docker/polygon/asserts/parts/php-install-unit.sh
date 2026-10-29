#!/usr/bin/env bash
# the package-install template unit (issue #93): what step 70 renders and installs for it
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The subject. The agent's own sandbox (ProtectSystem=true) makes /usr read-only to everything it
# forks, so it cannot run a package manager itself. It asks PID 1 to start
# `maran-php-install@<package>.service` instead, a root-run, unsandboxed template unit. That unit is
# the one sentence in this product that runs a package manager outside the agent's sandbox, so what
# it executes is asserted here, on BOTH families, from what the installer's own render function
# produced. The assertions never read the template source for their expectation: a check that
# reads its expectation out of the file it checks agrees with that file by construction.
#
# Every refusal below carries a planted inverse control: the same checker is run on copies of the
# real render with one property broken at a time, and must refuse each. A checker that cannot be
# made to refuse is not a check.

# The unit's instance-free file name, as the installer installs it. A literal on purpose: the
# census that compares it with the agent's prefix and the uninstaller's path is `maran structure`
# check 27; this one is the name step 70 must answer.
readonly PHP_INSTALL_UNIT_FILE="maran-php-install@.service"

# The ExecStart= line each family must render, spelled out here and not read from the template. One
# fixed argv: an absolute program, fixed options, the `--` that ends option parsing, and %i (never %I) as the only package argument.
readonly PHP_INSTALL_EXEC_DEBIAN="ExecStart=/usr/bin/apt-get install -y --no-remove -- %i"
readonly PHP_INSTALL_EXEC_RHEL="ExecStart=/usr/bin/dnf install -y -- %i"


# render_php_install_unit_for: runs step 70's own render_php_install_unit for the named family in a
# child shell and leaves the rendered unit at the given path.
#
# The family is passed per call, not taken from this image's, because rendering is text: both
# families' forms can be checked in every image, so a defect in the arm this family never runs
# still stops this build.
render_php_install_unit_for() {
  local family="$1" out="$2"
  MARAN_OS_FAMILY="$family" \
    LIB_DIR="$INSTALLER_LIB" \
    bash -c 'set -euo pipefail
. "$1"
. "$2"
render_php_install_unit "$3"' _ "$COMMON_LIB" "$SERVICES_STEP" "$out"
}


# php_install_unit_defect: prints the first reason the rendered unit at $1, for family $2, is not
# the unit the design allows, and prints nothing when it is.
#
# It reads EFFECTIVE lines only (comments and blanks removed): the shipped template's comments name
# `%I`, `--allowerasing` and `EnvironmentFile=` to explain their absence, and a check that counted
# those would refuse the unit for its own documentation.
php_install_unit_defect() {
  local file="$1" family="$2" effective expected_exec count timeout section_ok pre_lines remain_lines stdin_lines
  effective="$(grep -v '^[[:space:]]*#' "$file" | grep -v '^[[:space:]]*$' || true)"
  # eff_grep: grep over the effective lines through a here-string, never a pipe. Under pipefail a
  # `printf | grep -q` can fail with SIGPIPE when grep exits first, and a checker that flakes on
  # its own plumbing would be switched off by the first person it annoyed.
  eff_grep() { grep "$@" <<<"$effective"; }

  case "$family" in
    debian) expected_exec="$PHP_INSTALL_EXEC_DEBIAN" ;;
    rhel) expected_exec="$PHP_INSTALL_EXEC_RHEL" ;;
    *) echo "family '${family}' is not one this check knows"; return 0 ;;
  esac

  # One fixed argv, and it is the expected one.
  count="$(eff_grep -c '^ExecStart=' || true)"
  [ "$count" -eq 1 ] || { echo "has ${count} ExecStart= lines, not exactly one"; return 0; }
  [ "$(eff_grep '^ExecStart=')" = "$expected_exec" ] \
    || { echo "ExecStart= is not '${expected_exec}'"; return 0; }

  # Nothing that could turn the instance into more than one package argument, a path, a shell
  # string, or a removal.
  if eff_grep -q '%I'; then
    echo "uses %I, which un-escapes '-' into '/' and would let an instance spell a path"; return 0
  fi
  if eff_grep -Eq '(^|[ =/])(sh|bash|dash)( |$)|[ ]-c[ ]'; then
    echo "runs a shell"; return 0
  fi
  if eff_grep -q -- '--allowerasing'; then
    echo "passes --allowerasing, which lets dnf remove other packages"; return 0
  fi
  if eff_grep -q '^EnvironmentFile='; then
    echo "reads an EnvironmentFile=, so the agent's environment (or anyone's) reaches a root package manager"
    return 0
  fi
  pre_lines="$(eff_grep '^ExecStartPre=' || true)"
  case "$pre_lines" in
    *%*) echo "has an ExecStartPre= that interpolates a specifier"; return 0 ;;
  esac

  # A oneshot that nothing enables and that cannot be refused by the start rate limit.
  eff_grep -qx 'Type=oneshot' || { echo "is not Type=oneshot"; return 0; }
  remain_lines="$(eff_grep '^RemainAfterExit=' || true)"
  case "$remain_lines" in
    ''|RemainAfterExit=no) ;;
    *) echo "sets RemainAfterExit to something other than no"; return 0 ;;
  esac
  if eff_grep -Eq '^\[Install\]|^(WantedBy|RequiredBy|Alias|Also)='; then
    echo "has an [Install] section, so it can be enabled and started at boot"; return 0
  fi
  section_ok="$(awk '
    /^\[/ { section = $0; next }
    /^StartLimitIntervalSec=0$/ && section == "[Unit]" { found = 1 }
    END { print found ? "yes" : "no" }' <<<"$effective")"
  [ "$section_ok" = "yes" ] \
    || { echo "has no StartLimitIntervalSec=0 inside [Unit]"; return 0; }
  timeout="$(sed -n 's/^TimeoutStartSec=//p' <<<"$effective" | head -1)"
  case "$timeout" in
    ''|0|infinity) echo "has no finite TimeoutStartSec= (read: '${timeout}'), so a stuck mirror holds the agent forever"; return 0 ;;
  esac

  # The environment the package manager and its maintainer scripts really get.
  eff_grep -qx 'Environment=PATH=/usr/sbin:/usr/bin:/sbin:/bin' \
    || { echo "does not state Environment=PATH=/usr/sbin:/usr/bin:/sbin:/bin"; return 0; }
  eff_grep -qx 'UMask=0022' \
    || { echo "does not state UMask=0022"; return 0; }
  stdin_lines="$(eff_grep '^StandardInput=' || true)"
  case "$stdin_lines" in
    ''|StandardInput=null) ;;
    *) echo "gives the package manager a standard input other than null"; return 0 ;;
  esac
  if [ "$family" = "debian" ]; then
    eff_grep -qx 'Environment=DEBIAN_FRONTEND=noninteractive' \
      || { echo "does not set DEBIAN_FRONTEND=noninteractive, so a maintainer script can prompt on a closed stdin"; return 0; }
  else
    if eff_grep -q 'DEBIAN_FRONTEND'; then
      echo "sets DEBIAN_FRONTEND on a family with no debconf"; return 0
    fi
  fi
  return 0
}


# assert_the_php_install_unit_is_the_one_fixed_oneshot_the_design_allows: U1, U2 and U3 in one
# place, because they share the renders and the planted controls.
#
# Both families are rendered by step 70's own function and each render must come out clean. Then the
# checker is shown to refuse: eleven copies, each with ONE property broken, must each be refused,
# so that a refusal is known to be caused by the content and not by a checker that refuses
# everything, and an acceptance is known to mean something.
assert_the_php_install_unit_is_the_one_fixed_oneshot_the_design_allows() {
  local work family defect name control planted_count
  work="$(mktemp -d)"

  for family in debian rhel; do
    render_php_install_unit_for "$family" "${work}/${family}.service" \
      || fail "step 70's render_php_install_unit refused the ${family} family, which the installer supports"
    [ -s "${work}/${family}.service" ] \
      || fail "step 70's render_php_install_unit rendered an empty ${family} unit"
    defect="$(php_install_unit_defect "${work}/${family}.service" "$family")"
    [ -z "$defect" ] \
      || fail "the ${family} render of ${PHP_INSTALL_UNIT_FILE} ${defect}. This unit runs a package manager as root
outside the agent's sandbox; it must stay one fixed argv taking the instance only as %i, a oneshot nothing can
enable, with a stated environment. (This is the installer's own render, not the template read directly.)"
  done

  # The inverse controls. Each is the real render of one family with one thing broken, and each
  # must be refused. The names say what was broken; the checker says why it refused.
  planted_count=0
  while IFS='|' read -r name family control; do
    [ -n "$name" ] || continue
    sed "$control" "${work}/${family}.service" > "${work}/planted.service"
    cmp -s "${work}/${family}.service" "${work}/planted.service" \
      && fail "the planted control '${name}' changed nothing, so it proves nothing about the checker"
    defect="$(php_install_unit_defect "${work}/planted.service" "$family")"
    [ -n "$defect" ] \
      || fail "the unit checker ACCEPTED the planted defect '${name}' (${family} render). A checker that accepts a unit
whose ExecStart= or lifecycle was broken guards nothing about the root package install."
    planted_count=$((planted_count + 1))
  done <<'CONTROLS'
specifier %i spelled %I|debian|s/%i/%I/
--no-remove dropped|debian|s/ --no-remove//
the -- terminator dropped|debian|s/ -- %i/ %i/
the -- terminator dropped|rhel|s/ -- %i/ %i/
a shell wrapped around the package manager|debian|s#^ExecStart=.*#ExecStart=/bin/sh -c "apt-get install -y %i"#
--allowerasing added|rhel|s/dnf install -y /dnf install -y --allowerasing /
an EnvironmentFile= added|debian|s#^UMask=0022#UMask=0022\nEnvironmentFile=/etc/maran/agent.env#
an [Install] section added|debian|s#^Type=oneshot#Type=oneshot\n[Install]\nWantedBy=multi-user.target#
TimeoutStartSec removed|debian|/^TimeoutStartSec=/d
TimeoutStartSec infinity|debian|s/^TimeoutStartSec=.*/TimeoutStartSec=infinity/
StartLimitIntervalSec moved out of [Unit]|debian|s/^StartLimitIntervalSec=0$//;s/^Type=oneshot/Type=oneshot\nStartLimitIntervalSec=0/
RemainAfterExit=yes|debian|s/^RemainAfterExit=no/RemainAfterExit=yes/
Type changed from oneshot|debian|s/^Type=oneshot/Type=simple/
PATH line removed|debian|/^Environment=PATH=/d
UMask 0027|debian|s/^UMask=0022/UMask=0027/
DEBIAN_FRONTEND removed|debian|/^Environment=DEBIAN_FRONTEND=/d
DEBIAN_FRONTEND on the RHEL family|rhel|s/^# No debconf.*/Environment=DEBIAN_FRONTEND=noninteractive/
CONTROLS
  [ "$planted_count" -eq 17 ] \
    || fail "the php install unit checker ran ${planted_count} planted controls, not the 17 it carries. A control dropped from the list is a check that no longer exists."

  rm -rf "$work"
  echo "The package-install unit renders for both families as one fixed argv taking %i alone, a oneshot with no [Install],"
  echo "a finite timeout, the rate limit off, and a stated PATH, UMask and frontend; the checker refused all ${planted_count} planted defects."
}


# assert_step_70_installs_the_php_install_unit_root_owned_and_never_enabled: U4. Step 70's own
# install_units is RUN, then the file it left is looked at.
#
# The mode is 0644 root:root, because a unit file another uid can write is a root command line
# another uid can choose. Nothing may enable it: no .wants or .requires entry anywhere under the
# unit directory names it, because only the agent starts it. Where this image has systemd-analyze,
# the unit must verify clean; where it does not, that is said rather than skipped silently.
assert_step_70_installs_the_php_install_unit_root_owned_and_never_enabled() {
  local socket_path web_group installed observed link
  socket_path="$(installer_value MARAN_API_SOCKET_PATH "$INSTALLER_ENTRY_POINT")"
  web_group="$(web_server_group_for_this_family)"
  installed="/etc/systemd/system/${PHP_INSTALL_UNIT_FILE}"

  rm -f "$installed"
  run_services_step "$socket_path" "$web_group" 'install_units' \
    || fail "step 70's install_units failed on this family, so the panel could not be installed at all"
  [ -f "$installed" ] \
    || fail "step 70's install_units did not install ${installed}, so the agent's PHP install would find no unit"

  observed="$(stat -c '%a %U %G' "$installed")"
  [ "$observed" = "644 root root" ] \
    || fail "${installed} was installed as '${observed}' and must be '644 root root'. A unit file another uid can write
is a root command line that uid can choose."

  link="$(find /etc/systemd/system -mindepth 2 -maxdepth 2 -name 'maran-php-install@*' 2>/dev/null | head -1 || true)"
  [ -z "$link" ] \
    || fail "${link} enables the package-install unit. It has no [Install] section and only the agent may start it."

  if command -v systemd-analyze >/dev/null 2>&1; then
    local verdict
    if ! verdict="$(systemd-analyze verify "$installed" 2>&1)"; then
      fail "systemd-analyze verify refused ${installed}:
${verdict}"
    fi
    echo "systemd-analyze verify accepted the installed package-install unit."
  else
    echo "UNOBSERVED HERE: no systemd-analyze in this image, so the installed unit was not parsed by systemd."
  fi

  # Leave the image as it was found: the other assertions that install units remove theirs.
  rm -f "$installed" /etc/systemd/system/maran-api.service /etc/systemd/system/maran-agent.service \
    /etc/tmpfiles.d/maran-api.conf /etc/tmpfiles.d/maran-php.conf
  echo "Step 70 installed ${PHP_INSTALL_UNIT_FILE} as 644 root:root and enabled nothing."
}
