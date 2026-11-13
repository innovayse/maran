#!/usr/bin/env bash
# the app package units (issues #93, #517): what step 70 renders and installs for them
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The subject. The agent's own sandbox (ProtectSystem=true) makes /usr read-only to everything it
# forks, so it cannot run a package manager itself. It asks PID 1 to start
# `maran-app-install@<package>.service` (or `maran-app-remove@<package>.service`) instead, a root-run
# template unit with a sandbox of its own (issue #408). Those units are the two sentences in this
# product that run a package manager outside the agent's sandbox, so what they execute is asserted
# here, on ALL THREE families, from what the installer's own render function
# produced. The assertions never read the template source for their expectation: a check that
# reads its expectation out of the file it checks agrees with that file by construction.
#
# Every refusal below carries a planted inverse control: the same checker is run on copies of the
# real render with one property broken at a time, and must refuse each. A checker that cannot be
# made to refuse is not a check.

# The unit's instance-free file name, as the installer installs it. A literal on purpose: the
# census that compares it with the agent's prefix and the uninstaller's path is `maran structure`
# check 27; this one is the name step 70 must answer.
readonly APP_INSTALL_UNIT_FILE="maran-app-install@.service"
readonly APP_REMOVE_UNIT_FILE="maran-app-remove@.service"
# The RHEL-only Node.js module-stream pair (issue #532) and the two ExecStart= lines each must render,
# in order. Spelled out here and not read from the template, so editing the template cannot also edit
# its check. The instance is the digits-only major, as %i.
readonly APP_NODEJS_INSTALL_UNIT_FILE="maran-app-nodejs-install@.service"
readonly APP_NODEJS_REMOVE_UNIT_FILE="maran-app-nodejs-remove@.service"
readonly APP_NODEJS_INSTALL_EXEC="ExecStart=/usr/bin/dnf module reset -y nodejs
ExecStart=/usr/bin/dnf module install -y -- nodejs:%i/common"
readonly APP_NODEJS_REMOVE_EXEC="ExecStart=/usr/bin/dnf module remove -y -- nodejs:%i/common
ExecStart=/usr/bin/dnf module reset -y nodejs"

# The ExecStart= line each family must render, spelled out here and not read from the template. One
# fixed argv: an absolute program, fixed options, the `--` that ends option parsing, and %i (never %I) as the only package argument.
readonly APP_INSTALL_EXEC_DEBIAN="ExecStart=/usr/bin/apt-get install -y --no-remove -- %i"
readonly APP_INSTALL_EXEC_RHEL="ExecStart=/usr/bin/dnf install -y -- %i"
# UNVERIFIED: that zypper accepts `--` before the package and that /usr/bin/zypper is its path (the
# installer's render says so too, and the agent's SUSE adapter must agree). Confirm with
# `command -v zypper` and `zypper --non-interactive install --no-recommends -- bc; echo $?` in the
# pinned Leap image. `--non-interactive` is what keeps a prompt from hanging a unit with no stdin.
readonly APP_INSTALL_EXEC_SUSE="ExecStart=/usr/bin/zypper --non-interactive install --no-recommends -- %i"
# The removal unit's argv: no purge, no autoremove, no --force-*, no --allowerasing; `--` and %i last.
readonly APP_REMOVE_EXEC_DEBIAN="ExecStart=/usr/bin/apt-get remove -y -- %i"
readonly APP_REMOVE_EXEC_RHEL="ExecStart=/usr/bin/dnf remove -y -- %i"
readonly APP_REMOVE_EXEC_SUSE="ExecStart=/usr/bin/zypper --non-interactive remove -- %i"


# render_app_unit_for: runs step 70's own render_app_install_unit or render_app_remove_unit (the
# kind is `install` or `remove`) for the named family in a child shell and leaves the rendered unit
# at the given path.
#
# The family is passed per call, not taken from this image's, because rendering is text: all
# families' forms can be checked in every image, so a defect in the arm this family never runs
# still stops this build.
render_app_unit_for() {
  local kind="$1" family="$2" out="$3"
  MARAN_OS_FAMILY="$family" \
    LIB_DIR="$INSTALLER_LIB" \
    bash -c 'set -euo pipefail
. "$1"
. "$2"
"render_app_$3_unit" "$4"' _ "$COMMON_LIB" "$SERVICES_STEP" "$kind" "$out"
}


# app_unit_defect: prints the first reason the rendered unit at $1, for family $2 and kind $3 (install or remove), is not
# the unit the design allows, and prints nothing when it is.
#
# It reads EFFECTIVE lines only (comments and blanks removed): the shipped template's comments name
# `%I`, `--allowerasing` and `EnvironmentFile=` to explain their absence, and a check that counted
# those would refuse the unit for its own documentation.
app_unit_defect() {
  local file="$1" family="$2" kind="$3" effective expected_exec count timeout section_ok pre_lines remain_lines stdin_lines
  effective="$(grep -v '^[[:space:]]*#' "$file" | grep -v '^[[:space:]]*$' || true)"
  # eff_grep: grep over the effective lines through a here-string, never a pipe. Under pipefail a
  # `printf | grep -q` can fail with SIGPIPE when grep exits first, and a checker that flakes on
  # its own plumbing would be switched off by the first person it annoyed.
  eff_grep() { grep "$@" <<<"$effective"; }

  case "$kind:$family" in
    install:debian) expected_exec="$APP_INSTALL_EXEC_DEBIAN" ;;
    install:rhel) expected_exec="$APP_INSTALL_EXEC_RHEL" ;;
    install:suse) expected_exec="$APP_INSTALL_EXEC_SUSE" ;;
    remove:debian) expected_exec="$APP_REMOVE_EXEC_DEBIAN" ;;
    remove:rhel) expected_exec="$APP_REMOVE_EXEC_RHEL" ;;
    remove:suse) expected_exec="$APP_REMOVE_EXEC_SUSE" ;;
    *) echo "kind '${kind}' on family '${family}' is not one this check knows"; return 0 ;;
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
  if eff_grep -Eq -- '(^|[ =])(purge|autoremove|autopurge)( |$)|--purge|--auto-remove|--force-yes|--force-depends'; then
    echo "purges, auto-removes or forces: the panel's own configuration and other packages are not this unit's to delete"; return 0
  fi
  if eff_grep -q -- '--force-resolution'; then
    echo "passes --force-resolution, which lets zypper remove or downgrade other packages to satisfy the request"; return 0
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


# assert_the_app_package_units_are_the_fixed_oneshots_the_design_allows: U1, U2 and U3 in one
# place, because they share the renders and the planted controls.
#
# Both units are rendered for all three families by step 70's own functions and each render must
# come out clean. Then the checker is shown to refuse: copies with ONE property broken must each be
# refused, so that a refusal is known to be caused by the content and not by a checker that refuses
# everything, and an acceptance is known to mean something.
assert_the_app_package_units_are_the_fixed_oneshots_the_design_allows() {
  local work family kind defect name control planted_count unit_file
  work="$(mktemp -d)"

  for kind in install remove; do
    unit_file="maran-app-${kind}@.service"
    for family in debian rhel suse; do
      render_app_unit_for "$kind" "$family" "${work}/${kind}-${family}.service" \
        || fail "step 70's render_app_${kind}_unit refused the ${family} family, which the installer supports"
      [ -s "${work}/${kind}-${family}.service" ] \
        || fail "step 70's render_app_${kind}_unit rendered an empty ${family} unit"
      defect="$(app_unit_defect "${work}/${kind}-${family}.service" "$family" "$kind")"
      [ -z "$defect" ] \
        || fail "the ${family} render of ${unit_file} ${defect}. This unit runs a package manager as root
outside the agent's sandbox; it must stay one fixed argv taking the instance only as %i, a oneshot nothing can
enable, with a stated environment. (This is the installer's own render, not the template read directly.)"
    done
  done

  # The inverse controls. Each is the real render of one family with one thing broken, and each
  # must be refused. The names say what was broken; the checker says why it refused.
  planted_count=0
  while IFS='|' read -r name kind family control; do
    [ -n "$name" ] || continue
    sed "$control" "${work}/${kind}-${family}.service" > "${work}/planted.service"
    cmp -s "${work}/${kind}-${family}.service" "${work}/planted.service" \
      && fail "the planted control '${name}' changed nothing, so it proves nothing about the checker"
    defect="$(app_unit_defect "${work}/planted.service" "$family" "$kind")"
    [ -n "$defect" ] \
      || fail "the unit checker ACCEPTED the planted defect '${name}' (${kind} unit, ${family} render). A checker that accepts a unit
whose ExecStart= or lifecycle was broken guards nothing about the root package route."
    planted_count=$((planted_count + 1))
  done <<'CONTROLS'
specifier %i spelled %I|install|debian|s/%i/%I/
--no-remove dropped|install|debian|s/ --no-remove//
the -- terminator dropped|install|debian|s/ -- %i/ %i/
the -- terminator dropped|install|rhel|s/ -- %i/ %i/
a shell wrapped around the package manager|install|debian|s#^ExecStart=.*#ExecStart=/bin/sh -c "apt-get install -y %i"#
--allowerasing added|install|rhel|s/dnf install -y /dnf install -y --allowerasing /
the -- terminator dropped|install|suse|s/ -- %i/ %i/
--non-interactive dropped, so a prompt could hang the unit|install|suse|s/ --non-interactive//
--no-recommends dropped, so suggested packages are installed|install|suse|s/ --no-recommends//
--force-resolution added|install|suse|s/ install / install --force-resolution /
a shell wrapped around the package manager|install|suse|s#^ExecStart=.*#ExecStart=/bin/sh -c "zypper install %i"#
specifier %i spelled %I|install|suse|s/%i/%I/
an EnvironmentFile= added|install|debian|s#^UMask=0022#UMask=0022\nEnvironmentFile=/etc/maran/agent.env#
an [Install] section added|install|debian|s#^Type=oneshot#Type=oneshot\n[Install]\nWantedBy=multi-user.target#
TimeoutStartSec removed|install|debian|/^TimeoutStartSec=/d
TimeoutStartSec infinity|install|debian|s/^TimeoutStartSec=.*/TimeoutStartSec=infinity/
StartLimitIntervalSec moved out of [Unit]|install|debian|s/^StartLimitIntervalSec=0$//;s/^Type=oneshot/Type=oneshot\nStartLimitIntervalSec=0/
RemainAfterExit=yes|install|debian|s/^RemainAfterExit=no/RemainAfterExit=yes/
Type changed from oneshot|install|debian|s/^Type=oneshot/Type=simple/
PATH line removed|install|debian|/^Environment=PATH=/d
UMask 0027|install|debian|s/^UMask=0022/UMask=0027/
DEBIAN_FRONTEND removed|install|debian|/^Environment=DEBIAN_FRONTEND=/d
DEBIAN_FRONTEND on the RHEL family|install|rhel|s/^# No debconf.*/Environment=DEBIAN_FRONTEND=noninteractive/
DEBIAN_FRONTEND on the SUSE family|install|suse|s/^# No debconf.*/Environment=DEBIAN_FRONTEND=noninteractive/
the removal unit purges the configuration|remove|debian|s/ remove / purge /
the removal unit auto-removes dependencies|remove|debian|s/ remove -y / remove -y --auto-remove /
the removal unit forces|remove|debian|s/ remove -y / remove -y --force-yes /
the -- terminator dropped from the removal|remove|debian|s/ -- %i/ %i/
the -- terminator dropped from the removal|remove|rhel|s/ -- %i/ %i/
the removal unit lets dnf erase more|remove|rhel|s/dnf remove -y /dnf remove -y --allowerasing /
the removal unit auto-removes (dnf)|remove|rhel|s/dnf remove -y /dnf autoremove -y /
the -- terminator dropped from the removal|remove|suse|s/ -- %i/ %i/
--non-interactive dropped from the removal|remove|suse|s/ --non-interactive//
the removal unit forces a resolution (zypper)|remove|suse|s/ remove / remove --force-resolution /
a shell wrapped around the removal|remove|debian|s#^ExecStart=.*#ExecStart=/bin/sh -c "apt-get remove -y %i"#
specifier %i spelled %I in the removal|remove|rhel|s/%i/%I/
the removal unit installs instead|remove|debian|s/ remove -y / install -y /
an [Install] section added to the removal|remove|debian|s#^Type=oneshot#Type=oneshot\n[Install]\nWantedBy=multi-user.target#
TimeoutStartSec removed from the removal|remove|debian|/^TimeoutStartSec=/d
CONTROLS
  [ "$planted_count" -eq 39 ] \
    || fail "the app package unit checker ran ${planted_count} planted controls, not the 39 it carries. A control dropped from the list is a check that no longer exists."

  rm -rf "$work"
  echo "The package-install and package-removal units render for all three families as one fixed argv taking %i alone, oneshots with no [Install],"
  echo "a finite timeout, the rate limit off, and a stated PATH, UMask and frontend; the removal never purges or auto-removes; the checker refused all ${planted_count} planted defects."
}


# assert_step_70_installs_the_app_package_units_root_owned_and_never_enabled: U4. Step 70's own
# install_units is RUN, then the files it left are looked at.
#
# The mode is 0644 root:root, because a unit file another uid can write is a root command line
# another uid can choose. Nothing may enable either unit: no .wants or .requires entry anywhere under
# the unit directory names it, because only the agent starts them. Where this image has
# systemd-analyze, each unit must verify clean; where it does not, that is said rather than skipped
# silently.
assert_step_70_installs_the_app_package_units_root_owned_and_never_enabled() {
  local socket_path web_group installed observed link unit_file
  socket_path="$(installer_value MARAN_API_SOCKET_PATH "$INSTALLER_ENTRY_POINT")"
  web_group="$(web_server_group_for_this_family)"

  for unit_file in "$APP_INSTALL_UNIT_FILE" "$APP_REMOVE_UNIT_FILE" "$APP_NODEJS_INSTALL_UNIT_FILE" "$APP_NODEJS_REMOVE_UNIT_FILE"; do
    rm -f "/etc/systemd/system/${unit_file}"
  done
  run_services_step "$socket_path" "$web_group" 'install_units' \
    || fail "step 70's install_units failed on this family, so the panel could not be installed at all"

  for unit_file in "$APP_INSTALL_UNIT_FILE" "$APP_REMOVE_UNIT_FILE"; do
    installed="/etc/systemd/system/${unit_file}"
    [ -f "$installed" ] \
      || fail "step 70's install_units did not install ${installed}, so the agent's app route would find no unit"

    observed="$(stat -c '%a %U %G' "$installed")"
    [ "$observed" = "644 root root" ] \
      || fail "${installed} was installed as '${observed}' and must be '644 root root'. A unit file another uid can write
is a root command line that uid can choose."

    link="$(find /etc/systemd/system -mindepth 2 -maxdepth 2 -name "${unit_file%@.service}@*" 2>/dev/null | head -1 || true)"
    [ -z "$link" ] \
      || fail "${link} enables an app package unit. It has no [Install] section and only the agent may start it."

    if command -v systemd-analyze >/dev/null 2>&1; then
      local verdict
      if ! verdict="$(systemd-analyze verify "$installed" 2>&1)"; then
        fail "systemd-analyze verify refused ${installed}:
${verdict}"
      fi
      echo "systemd-analyze verify accepted the installed ${unit_file}."
    else
      echo "UNOBSERVED HERE: no systemd-analyze in this image, so the installed ${unit_file} was not parsed by systemd."
    fi
  done

  # Leave the image as it was found: the other assertions that install units remove theirs.
  rm -f "/etc/systemd/system/${APP_INSTALL_UNIT_FILE}" "/etc/systemd/system/${APP_REMOVE_UNIT_FILE}" \
    "/etc/systemd/system/${APP_NODEJS_INSTALL_UNIT_FILE}" "/etc/systemd/system/${APP_NODEJS_REMOVE_UNIT_FILE}" \
    /etc/systemd/system/maran-api.service /etc/systemd/system/maran-agent.service \
    /etc/tmpfiles.d/maran-api.conf /etc/tmpfiles.d/maran-php.conf
  echo "Step 70 installed ${APP_INSTALL_UNIT_FILE} and ${APP_REMOVE_UNIT_FILE} as 644 root:root and enabled nothing."
}


# render_nodejs_unit_for: runs step 70's own render_nodejs_module_unit for the named family in a child
# shell and leaves the rendered unit at the given path. Returns the child's status, because the
# refusal on a family without module streams is the thing under test. The child's standard error is kept
# beside the output (<out>.err) so a failure is explained by what the renderer said and never lost.
#
# The optional fourth argument is the release (os-release VERSION_ID) the render is for; the default
# is 9, a release with module streams. A release is passed rather than read from this image because
# rendering is text: the RHEL 10 refusal (issue #542) is checked on every image, alma9 included.
render_nodejs_unit_for() {
  local template="$1" family="$2" out="$3" release="${4:-9}"
  MARAN_OS_FAMILY="$family" \
    MARAN_OS_VERSION_ID="$release" \
    LIB_DIR="$INSTALLER_LIB" \
    bash -c 'set -euo pipefail
. "$1"
. "$2"
render_nodejs_module_unit "$3" "$4"' _ "$COMMON_LIB" "$SERVICES_STEP" "$template" "$out" 2>"${out}.err"
}

# nodejs_unit_defect: prints the first reason the rendered Node.js module unit at $1 (kind $2, install
# or remove) is not the unit the design allows, and prints nothing when it is. Effective lines only:
# the template's comments name `%I`, `--allowerasing` and `[Install]` to explain their absence.
nodejs_unit_defect() {
  local file="$1" kind="$2" effective expected_exec exec_lines timeout
  effective="$(grep -v '^[[:space:]]*#' "$file" | grep -v '^[[:space:]]*$' || true)"
  eff_grep() { grep "$@" <<<"$effective"; }
  case "$kind" in
    install) expected_exec="$APP_NODEJS_INSTALL_EXEC" ;;
    remove) expected_exec="$APP_NODEJS_REMOVE_EXEC" ;;
    *) echo "kind '${kind}' is not one this check knows"; return 0 ;;
  esac
  exec_lines="$(eff_grep '^ExecStart=' || true)"
  [ "$exec_lines" = "$expected_exec" ] \
    || { echo "ExecStart= lines are not exactly, in order: ${expected_exec//$'\n'/ ; }"; return 0; }
  if eff_grep -q '%I'; then echo "uses %I, which un-escapes '-' into '/'"; return 0; fi
  if eff_grep -Eq '(^|[ =/])(sh|bash|dash)( |$)|[ ]-c[ ]'; then echo "runs a shell"; return 0; fi
  if eff_grep -q -- '--allowerasing'; then echo "passes --allowerasing, which lets dnf remove other packages"; return 0; fi
  if eff_grep -Eq -- '(^|[ =])(autoremove|autopurge)( |$)|--setopt|--nogpgcheck'; then
    echo "auto-removes or weakens dnf's checks"; return 0
  fi
  if eff_grep -q '^EnvironmentFile='; then echo "reads an EnvironmentFile="; return 0; fi
  eff_grep -qx 'Type=oneshot' || { echo "is not Type=oneshot"; return 0; }
  if eff_grep -Eq '^\[Install\]|^(WantedBy|RequiredBy|Alias|Also)='; then
    echo "has an [Install] section, so it can be enabled and started at boot"; return 0
  fi
  eff_grep -qx 'RemainAfterExit=no' || { echo "does not state RemainAfterExit=no"; return 0; }
  [ "$(awk '/^\[/ { section = $0; next } /^StartLimitIntervalSec=0$/ && section == "[Unit]" { found = 1 } END { print found ? "yes" : "no" }' <<<"$effective")" = "yes" ] \
    || { echo "has no StartLimitIntervalSec=0 inside [Unit]"; return 0; }
  timeout="$(sed -n 's/^TimeoutStartSec=//p' <<<"$effective" | head -1)"
  case "$timeout" in
    ''|0|infinity) echo "has no finite TimeoutStartSec= (read: '${timeout}')"; return 0 ;;
  esac
  eff_grep -qx 'Environment=PATH=/usr/sbin:/usr/bin:/sbin:/bin' \
    || { echo "does not state Environment=PATH=/usr/sbin:/usr/bin:/sbin:/bin"; return 0; }
  eff_grep -qx 'UMask=0022' || { echo "does not state UMask=0022"; return 0; }
  eff_grep -qx 'ProtectKernelTunables=yes' \
    || { echo "lost the install unit's sandbox (ProtectKernelTunables=yes is absent)"; return 0; }
  if eff_grep -q 'DEBIAN_FRONTEND'; then echo "sets DEBIAN_FRONTEND on a family with no debconf"; return 0; fi
  return 0
}

# assert_the_nodejs_module_units_are_fixed_oneshots_where_the_release_has_module_streams: issue #532.
#
# Step 70's render_nodejs_module_unit renders both units for the RHEL family and each render must come
# out clean; for Debian and SUSE it must REFUSE and write nothing (module streams are a dnf feature).
# The RHEL 10 generation refuses too (issue #542). Then install_units is run on THIS image's family: on alma9 both
# files are installed 644 root:root and enabled nowhere (and not at all when the release is simulated as 10), on the
# other images neither exists. Planted copies with one property broken must
# each be refused, so a refusal is known to come from the content.
assert_the_nodejs_module_units_are_fixed_oneshots_where_the_release_has_module_streams() {
  local work kind family release image_streams defect name control planted_count unit_file installed observed socket_path web_group link
  work="$(mktemp -d)"

  for kind in install remove; do
    unit_file="maran-app-nodejs-${kind}@.service"
    render_nodejs_unit_for "$unit_file" rhel "${work}/${kind}.service" \
      || fail "step 70's render_nodejs_module_unit refused the RHEL family for ${unit_file}: $(cat "${work}/${kind}.service.err")"
    defect="$(nodejs_unit_defect "${work}/${kind}.service" "$kind")"
    [ -z "$defect" ] \
      || fail "the RHEL render of ${unit_file} ${defect}. This unit runs dnf as root outside the agent's sandbox; it must stay two
fixed argvs taking the instance only as %i, a oneshot nothing can enable."
    for family in debian suse; do
      if render_nodejs_unit_for "$unit_file" "$family" "${work}/${kind}-${family}.service"; then
        fail "step 70's render_nodejs_module_unit ACCEPTED the ${family} family for ${unit_file}; module streams exist on the RHEL family only"
      fi
      [ ! -e "${work}/${kind}-${family}.service" ] \
        || fail "a refused ${family} render of ${unit_file} still left a file"
      [ -s "${work}/${kind}-${family}.service.err" ] \
        || fail "step 70's render_nodejs_module_unit refused the ${family} family for ${unit_file} without saying why"
    done
    # The RHEL 10 generation has no module streams (issue #542): refused, and nothing written. 8.10
    # (modules) and a release that is not a number (read as the older generation) are the inverse
    # controls, so the refusal is known to come from the major and not from the family alone.
    for release in 10 10.2 11.0; do
      if render_nodejs_unit_for "$unit_file" rhel "${work}/${kind}-el${release}.service" "$release"; then
        fail "step 70's render_nodejs_module_unit ACCEPTED RHEL ${release} for ${unit_file}; that generation ships no module streams"
      fi
      [ ! -e "${work}/${kind}-el${release}.service" ] \
        || fail "a refused RHEL ${release} render of ${unit_file} still left a file"
      [ -s "${work}/${kind}-el${release}.service.err" ] \
        || fail "step 70's render_nodejs_module_unit refused RHEL ${release} for ${unit_file} without saying why"
    done
    for release in 8.10 9.4 unknown; do
      render_nodejs_unit_for "$unit_file" rhel "${work}/${kind}-el${release}.service" "$release" \
        || fail "step 70's render_nodejs_module_unit refused RHEL ${release} for ${unit_file}, a release with module streams: $(cat "${work}/${kind}-el${release}.service.err")"
    done
  done

  planted_count=0
  while IFS='|' read -r name kind control; do
    [ -n "$name" ] || continue
    sed "$control" "${work}/${kind}.service" > "${work}/planted.service"
    cmp -s "${work}/${kind}.service" "${work}/planted.service" \
      && fail "the planted control '${name}' changed nothing, so it proves nothing about the checker"
    defect="$(nodejs_unit_defect "${work}/planted.service" "$kind")"
    [ -n "$defect" ] \
      || fail "the Node.js unit checker ACCEPTED the planted defect '${name}' (${kind} unit)"
    planted_count=$((planted_count + 1))
  done <<'CONTROLS'
specifier %i spelled %I|install|s/%i/%I/
the -- terminator dropped|install|s/ -- nodejs:/ nodejs:/
the stream reset dropped|install|/^ExecStart=.*module reset/d
the install runs before the reset|install|s/^ExecStart=\/usr\/bin\/dnf module reset -y nodejs$/ExecStart=\/usr\/bin\/dnf module list -y nodejs/
--allowerasing added|install|s/module install -y/module install -y --allowerasing/
a shell wrapped around dnf|install|s#^ExecStart=/usr/bin/dnf module install.*#ExecStart=/bin/sh -c "dnf module install -y nodejs:%i/common"#
an [Install] section added|install|s#^Type=oneshot#Type=oneshot\n[Install]\nWantedBy=multi-user.target#
an EnvironmentFile= added|install|s#^UMask=0022#UMask=0022\nEnvironmentFile=/etc/maran/agent.env#
TimeoutStartSec infinity|install|s/^TimeoutStartSec=.*/TimeoutStartSec=infinity/
the sandbox dropped|install|/^ProtectKernelTunables=/d
the stream is not reset after the removal|remove|/^ExecStart=.*module reset/d
the -- terminator dropped from the removal|remove|s/ -- nodejs:/ nodejs:/
the removal lets dnf erase more|remove|s/module remove -y/module remove -y --allowerasing/
the removal auto-removes|remove|s/module remove -y/autoremove -y/
the removal installs instead|remove|s/module remove -y/module install -y/
an [Install] section added to the removal|remove|s#^Type=oneshot#Type=oneshot\n[Install]\nWantedBy=multi-user.target#
StartLimitIntervalSec removed from the removal|remove|/^StartLimitIntervalSec=0$/d
CONTROLS
  [ "$planted_count" -eq 17 ] \
    || fail "the Node.js unit checker ran ${planted_count} planted controls, not the 17 it carries. A dropped control is a check that no longer exists."

  # install_units on THIS image's family and release: an EL8 or EL9 image has the pair, every other image
  # (EL10 included) none. The expectation is read from the image's own os-release, not from the step's
  # predicate, so the step is not asked to agree with itself.
  image_streams=no
  if [ "${MARAN_OS_FAMILY:-}" = rhel ]; then
    case "$(. /etc/os-release && printf '%s' "${VERSION_ID%%.*}")" in
      8 | 9) image_streams=yes ;;
    esac
  fi
  socket_path="$(installer_value MARAN_API_SOCKET_PATH "$INSTALLER_ENTRY_POINT")"
  web_group="$(web_server_group_for_this_family)"
  for unit_file in "$APP_NODEJS_INSTALL_UNIT_FILE" "$APP_NODEJS_REMOVE_UNIT_FILE"; do
    rm -f "/etc/systemd/system/${unit_file}"
  done
  run_services_step "$socket_path" "$web_group" 'install_units' \
    || fail "step 70's install_units failed on this family"
  for unit_file in "$APP_NODEJS_INSTALL_UNIT_FILE" "$APP_NODEJS_REMOVE_UNIT_FILE"; do
    installed="/etc/systemd/system/${unit_file}"
    if [ "$image_streams" = yes ]; then
      [ -f "$installed" ] || fail "step 70's install_units did not install ${installed} on the RHEL family before 10"
      observed="$(stat -c '%a %U %G' "$installed")"
      [ "$observed" = "644 root root" ] \
        || fail "${installed} was installed as '${observed}' and must be '644 root root'"
      link="$(find /etc/systemd/system -mindepth 2 -maxdepth 2 -name "${unit_file%@.service}@*" 2>/dev/null | head -1 || true)"
      [ -z "$link" ] || fail "${link} enables a Node.js module unit; only the agent may start it"
    else
      [ ! -e "$installed" ] \
        || fail "step 70's install_units installed ${installed} on a ${MARAN_OS_FAMILY:-unknown} release that has no module streams"
    fi
  done

  # The same install_units for the RHEL 10 generation, simulated through the release the step reads
  # (MARAN_OS_VERSION_ID) because no alma10 image carries this suite: neither unit may be laid down,
  # while the generic pair still is (the inverse: the step ran and installed units).
  if [ "${MARAN_OS_FAMILY:-}" = rhel ]; then
    for unit_file in "$APP_NODEJS_INSTALL_UNIT_FILE" "$APP_NODEJS_REMOVE_UNIT_FILE" "$APP_INSTALL_UNIT_FILE" "$APP_REMOVE_UNIT_FILE"; do
      rm -f "/etc/systemd/system/${unit_file}"
    done
    MARAN_OS_VERSION_ID=10.2 run_services_step "$socket_path" "$web_group" 'install_units' \
      || fail "step 70's install_units failed on a simulated RHEL 10 release"
    for unit_file in "$APP_NODEJS_INSTALL_UNIT_FILE" "$APP_NODEJS_REMOVE_UNIT_FILE"; do
      [ ! -e "/etc/systemd/system/${unit_file}" ] \
        || fail "step 70's install_units installed ${unit_file} on a simulated RHEL 10 release, which has no module streams"
    done
    for unit_file in "$APP_INSTALL_UNIT_FILE" "$APP_REMOVE_UNIT_FILE"; do
      [ -f "/etc/systemd/system/${unit_file}" ] \
        || fail "step 70's install_units did not install ${unit_file} on a simulated RHEL 10 release"
    done
  fi
  rm -f "/etc/systemd/system/${APP_NODEJS_INSTALL_UNIT_FILE}" "/etc/systemd/system/${APP_NODEJS_REMOVE_UNIT_FILE}" \
    /etc/systemd/system/maran-api.service /etc/systemd/system/maran-agent.service \
    "/etc/systemd/system/${APP_INSTALL_UNIT_FILE}" "/etc/systemd/system/${APP_REMOVE_UNIT_FILE}" \
    /etc/tmpfiles.d/maran-api.conf /etc/tmpfiles.d/maran-php.conf /etc/tmpfiles.d/maran-host.conf

  rm -rf "$work"
  echo "The Node.js module units render for the RHEL family as two fixed dnf argvs taking %i alone, oneshots with no [Install]; Debian, SUSE and RHEL 10 refuse them;"
  echo "install_units lays them down 644 root:root on the RHEL releases with module streams only and enables nothing; the checker refused all ${planted_count} planted defects."
}
