#!/usr/bin/env bash
# the app store's host side (issue #517): the first-run state, one app installed and removed through
# the package units' stand-in, and the uninstaller taking the app package units back
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The subject (docs/apps/README.md). The panel installs no site runtime. nginx is the panel's own
# front and stays; PHP, Apache and Node.js are apps the panel installs on request through
# `maran-app-install@<package>.service` and takes away through `maran-app-remove@<package>.service`.
# What the text assertions of app-package-units.sh pin is the argv those units run; what is asserted
# HERE is the host's state: a fresh image has the panel front and no site runtime, and the same
# argv shapes, run through the systemctl stand-in (which runs the real package manager for these two
# unit kinds, docker/polygon/stand-ins/systemctl-stand-in.sh run_app_package_unit), really install
# and really remove a package.
#
# UNOBSERVED HERE, stated once: the stand-in runs the package manager WITHOUT the units' sandbox
# and without a booted PID 1, so a directive that breaks a real maintainer script is not seen. The
# sandbox was measured on a booted systemd (threat note 2026-11-06) and the removal unit's reuse of
# it is in the app threat note's "What the measurement could not establish".

# The app this part installs and removes: the family's Apache. Small, in the distribution's own
# repositories (no third-party source is needed to run this part), a real web server, and one of the
# apps the catalogue offers on every family.
app_package_for_this_family() {
  case "${MARAN_OS_FAMILY:-}" in
    debian | suse) printf 'apache2' ;;
    rhel) printf 'httpd' ;;
    *) fail "app_package_for_this_family: MARAN_OS_FAMILY is '${MARAN_OS_FAMILY:-}', which this part does not know" ;;
  esac
}

# package_is_installed: whether the host's package database lists the named package as installed.
package_is_installed() {
  local package="$1"
  case "${MARAN_OS_FAMILY:-}" in
    debian) dpkg-query -W -f='${Status}' "$package" 2>/dev/null | grep -q '^install ok installed$' ;;
    rhel | suse) rpm -q "$package" >/dev/null 2>&1 ;;
    *) fail "package_is_installed: MARAN_OS_FAMILY is '${MARAN_OS_FAMILY:-}', which this part does not know" ;;
  esac
}

# site_runtime_packages_installed: one line per installed package that is a site runtime (an app),
# empty when there is none. PHP-FPM, Apache and Node.js, by the names each family gives them.
site_runtime_packages_installed() {
  case "${MARAN_OS_FAMILY:-}" in
    debian)
      dpkg-query -W -f='${db:Status-Abbrev} ${Package}\n' 2>/dev/null \
        | awk '$1 == "ii" { print $2 }' | { grep -E '^(php[0-9.]*-fpm|apache2|nodejs)$' || true; } ;;
    rhel | suse)
      rpm -qa --qf '%{NAME}\n' 2>/dev/null | { grep -E '^(php[0-9]*-php-fpm|php[0-9]*-fpm|php-fpm|httpd|apache2|nodejs[0-9]*)$' || true; } ;;
    *) fail "site_runtime_packages_installed: MARAN_OS_FAMILY is '${MARAN_OS_FAMILY:-}', which this part does not know" ;;
  esac
}

# refresh_package_index: the package index, which the image build removed to keep its layers small.
refresh_package_index() {
  case "${MARAN_OS_FAMILY:-}" in
    debian) apt-get update >/dev/null || fail "apt-get update failed; the app install below would test the network and not the unit" ;;
    *) ;;
  esac
}

# assert_a_fresh_host_has_the_panel_front_and_no_site_runtime: the first-run state.
#
# nginx is installed (the panel's own front, installer/lib/80-nginx.sh) and no site runtime is: no
# PHP-FPM, no Apache, no Node.js. A host in this state is the one the backend's stack gate refuses
# to create a site on until an administrator has installed a site web server. The inverse control
# plants a runtime-shaped package list and shows the census reads it.
assert_a_fresh_host_has_the_panel_front_and_no_site_runtime() {
  local found
  command -v nginx >/dev/null 2>&1 \
    || fail "nginx is not installed on a fresh polygon image. It is the panel's own front on port 8443 and the installer installs it; without it the panel has no front."
  found="$(site_runtime_packages_installed)"
  [ -z "$found" ] \
    || fail "a fresh polygon image already carries site runtime package(s): $(printf '%s' "$found" | tr '\n' ' '). PHP, Apache and Node.js are apps (issue #517); a host that has one before anybody chose it has no first-run gate to observe."

  # Its inverse control: the census must see a runtime when one is there. A throwaway package-name
  # list through the same pattern, not an install, so the image is left as it was.
  case "${MARAN_OS_FAMILY:-}" in
    debian)
      [ "$(printf 'php8.3-fpm\nbash\n' | { grep -E '^(php[0-9.]*-fpm|apache2|nodejs)$' || true; })" = "php8.3-fpm" ] \
        || fail "the site runtime pattern did not match a planted php8.3-fpm, so the census above reads nothing" ;;
    *)
      [ "$(printf 'php83-php-fpm\nbash\n' | { grep -E '^(php[0-9]*-php-fpm|php[0-9]*-fpm|php-fpm|httpd|apache2|nodejs[0-9]*)$' || true; })" = "php83-php-fpm" ] \
        || fail "the site runtime pattern did not match a planted php83-php-fpm, so the census above reads nothing" ;;
  esac

  echo "A fresh image has nginx, the panel's front, and no PHP-FPM, Apache or Node.js; the census pattern matched a planted runtime."
}

# removal_dry_run: the family's dry run of removing one package, the argv the agent's adapter
# builds (agent/crates/distro/src/{debian,rhel,suse}/*_removal.rs), spelled out here so editing the
# adapter cannot also edit its check. Prints what the package manager printed; the exit status is
# the caller's to read.
removal_dry_run() {
  local package="$1"
  case "${MARAN_OS_FAMILY:-}" in
    debian) apt-get -s -q -o Debug::NoLocking=true remove -- "$package" 2>&1 ;;
    rhel) dnf remove --assumeno -- "$package" 2>&1 ;;
    suse) zypper --non-interactive remove --dry-run -- "$package" 2>&1 ;;
    *) fail "removal_dry_run: MARAN_OS_FAMILY is '${MARAN_OS_FAMILY:-}', which this part does not know" ;;
  esac
}

# assert_a_removal_dry_run_changes_nothing: the dry run the agent runs before every removal (issue
# #527) on the real package manager of this image.
#
# It must print a plan that names the package, and leave the package installed. The inverse control
# is a package that is not installed: its dry run must not print a removal plan naming it, which is
# what the agent's readers refuse (a removal that cannot be understood is not started). The
# readers themselves are Rust and are held by recorded fixtures (agent/crates/distro/tests/fixtures/
# package_manager/); what is asserted here is that this image's manager still prints a plan of the
# shape they were recorded from.
assert_a_removal_dry_run_changes_nothing() {
  local package="$1" plan absent_plan
  plan="$(removal_dry_run "$package" || true)"
  case "$plan" in
    *"$package"*) ;;
    *) fail "the dry run of removing ${package} does not name it, so the agent could not read what a removal would take: ${plan}" ;;
  esac
  package_is_installed "$package" \
    || fail "the dry run of removing ${package} REMOVED it. A dry run that changes the host is not one."

  absent_plan="$(removal_dry_run maran-polygon-no-such-package || true)"
  case "$absent_plan" in
    *"Remv maran-polygon-no-such-package"* | *"Removing:"*"maran-polygon-no-such-package"* | *"going to be REMOVED"*)
      fail "the dry run of a package that is not installed printed a removal plan naming it: ${absent_plan}" ;;
  esac
  echo "the family's removal dry run named ${package}, changed nothing, and printed no plan for a package that is not installed."
}

# assert_an_app_installs_and_uninstalls_through_the_package_units: one app, installed and removed.
#
# `systemctl start maran-app-install@<package>.service` through the stand-in runs the install argv and
# the package appears in the package database; `maran-app-remove@<package>.service` runs the removal
# argv and it disappears. Inverse controls, each of which must be REFUSED:
#   * an install of a package no repository has fails, and installs nothing;
#   * an instance that begins with `-` is refused before any package manager runs, so it cannot be
#     read as an option;
#   * an uninstall of a package that is not installed leaves the database as it was.
assert_an_app_installs_and_uninstalls_through_the_package_units() {
  local package unit_install unit_remove output
  package="$(app_package_for_this_family)"
  unit_install="maran-app-install@${package}.service"
  unit_remove="maran-app-remove@${package}.service"

  package_is_installed "$package" \
    && fail "${package} is installed before this assertion started, so installing it proves nothing"
  refresh_package_index

  # The refused ones first: nothing may have changed afterwards.
  if output="$(systemctl start 'maran-app-install@maran-polygon-no-such-package.service' 2>&1)"; then
    fail "installing a package no repository has was ACCEPTED by the install unit's stand-in. A route that cannot fail reports every install as a success."
  fi
  case "$output" in
    *"maran-app-install@maran-polygon-no-such-package.service failed"*) ;;
    *) fail "the install of a missing package failed, but not with the unit's failure line: ${output}" ;;
  esac
  if output="$(systemctl start 'maran-app-install@-oAPT::Get::Assume-Yes=true.service' 2>&1)"; then
    fail "an instance beginning with '-' was ACCEPTED, so it could be read by the package manager as an option"
  fi

  systemctl start "$unit_install" \
    || fail "${unit_install} failed, so the app route cannot install ${package} on this family"
  package_is_installed "$package" \
    || fail "${unit_install} returned success but ${package} is not in the package database"

  assert_a_removal_dry_run_changes_nothing "$package"

  systemctl start "$unit_remove" \
    || fail "${unit_remove} failed, so the app route cannot remove ${package} on this family"
  if package_is_installed "$package"; then
    fail "${unit_remove} returned success but ${package} is still in the package database"
  fi

  # Removing what is not installed changes nothing (the agent refuses it first, with
  # AgentAppNotInstalled; the unit is the second line, not the first).
  systemctl start "$unit_remove" >/dev/null 2>&1 || true
  package_is_installed "$package" \
    && fail "a second removal of ${package} left it installed"

  # nginx, the panel's front, is untouched by an app's install and removal.
  command -v nginx >/dev/null 2>&1 \
    || fail "installing and removing ${package} through the app units took nginx, the panel's front, with it"

  # The package lists this assertion refreshed are left in place: a later part (step 68's
  # unattended-upgrades install) reads them, and the image's final layer cleans them up.
  echo "${package} was installed by ${unit_install} and removed by ${unit_remove}; a missing package and an option-shaped instance were refused; nginx stayed."
  echo "UNOBSERVED HERE: the units' sandbox and a booted PID 1. The stand-in runs the same argv as a plain root process."
}

# nodejs_enabled_stream_lines: one line per Node.js module stream dnf lists as enabled, empty when
# none is. `dnf module list --enabled` exits non-zero and prints "No matching Modules" when there is
# none, which is the empty answer and not a failure here.
nodejs_enabled_stream_lines() {
  dnf module list --enabled nodejs 2>&1 | { grep -E '^nodejs[[:space:]].*\[e\]' || true; }
}

# assert_a_nodejs_module_stream_installs_once_and_uninstalls_clean: issue #532, the alma9 half.
#
# On the RHEL family Node.js versions are module streams. Through the systemctl stand-in (which runs
# the two fixed dnf argvs of maran-app-nodejs-install@ and maran-app-nodejs-remove@, minus the sandbox):
#   * the stream list the agent parses (`dnf -C module list nodejs`) offers the supported majors with
#     the `common` profile; the inverse control is a stream nobody offers (a major absent from it);
#   * starting the install unit for 20 puts node v20 on the host;
#   * a non-digit instance is refused by the stand-in before any dnf runs (the unit itself does not
#     re-check; the agent's NodejsModuleStream type does, and its refusal of a second major with the
#     code apps.nodeMajorActive is held by the agent's tests, not by this shell);
#   * the removal unit leaves `rpm -q nodejs` absent and NO enabled stream, so any major may follow.
# Debian and SUSE hosts skip it: they install Node.js as a plain package through the generic units.
assert_a_nodejs_module_stream_installs_once_and_uninstalls_clean() {
  local listing node_version enabled
  if [ "${MARAN_OS_FAMILY:-}" != rhel ]; then
    echo "SKIPPED on the ${MARAN_OS_FAMILY:-unknown} family: Node.js module streams exist on the RHEL family only."
    return 0
  fi
  rpm -q nodejs >/dev/null 2>&1 \
    && fail "nodejs is installed before this assertion started, so installing a stream proves nothing"
  [ -z "$(nodejs_enabled_stream_lines)" ] \
    || fail "a Node.js module stream is enabled before this assertion started"

  # The cache must exist for `-C`, as it does on a panel host that has run any dnf command.
  dnf -q makecache >/dev/null 2>&1 || fail "dnf makecache failed, so the stream list below would test the network and not the parser's input"
  if ! listing="$(dnf -C module list nodejs 2>&1)"; then
    # The 10 generation ships no module streams at all (issue #542): that is an answer, not a
    # failure, and the assertion for plain per-major packages below is the one that holds there.
    case "$listing" in
      *"No matching Modules"*)
        echo "SKIPPED: this release's sources carry no Node.js module streams; Node.js is checked as per-major packages instead."
        return 0 ;;
      *) fail "dnf -C module list nodejs failed: ${listing}" ;;
    esac
  fi
  printf '%s\n' "$listing" | grep -Eq '^nodejs[[:space:]]+20[[:space:]].*common' \
    || fail "the nodejs stream list does not offer 20 with the common profile: ${listing}"
  printf '%s\n' "$listing" | grep -Eq '^nodejs[[:space:]]+999[[:space:]]' \
    && fail "the nodejs stream list offered a planted stream 999, so the census above reads nothing"

  if systemctl start 'maran-app-nodejs-install@2x.service' >/dev/null 2>&1; then
    fail "a non-digit instance was ACCEPTED by the Node.js install unit's stand-in"
  fi
  rpm -q nodejs >/dev/null 2>&1 && fail "a refused non-digit instance installed nodejs anyway"

  systemctl start 'maran-app-nodejs-install@20.service' \
    || fail "maran-app-nodejs-install@20.service failed on AlmaLinux 9"
  node_version="$(/usr/bin/node --version 2>&1)" || fail "node is not runnable after the stream install: ${node_version}"
  case "$node_version" in
    v20.*) ;;
    *) fail "the stream install of 20 put node '${node_version}' on the host, not v20.x" ;;
  esac
  [ -n "$(nodejs_enabled_stream_lines)" ] \
    || fail "no Node.js stream is enabled after the install of 20, so the host cannot say which major it has"

  systemctl start 'maran-app-nodejs-remove@20.service' \
    || fail "maran-app-nodejs-remove@20.service failed on AlmaLinux 9"
  rpm -q nodejs >/dev/null 2>&1 \
    && fail "nodejs is still installed after the stream removal"
  enabled="$(nodejs_enabled_stream_lines)"
  [ -z "$enabled" ] \
    || fail "a Node.js stream is still enabled after the removal, so a different major could not follow: ${enabled}"
  command -v nginx >/dev/null 2>&1 \
    || fail "installing and removing a Node.js stream took nginx, the panel's front, with it"

  echo "Node.js 20 was installed as a module stream (node ${node_version}) and removed with no package left and no stream enabled; a non-digit instance was refused."
  echo "UNOBSERVED HERE: the units' sandbox, a booted PID 1, and the agent's refusal of a second major (apps.nodeMajorActive), which the agent's own tests hold."
}

# assert_node_majors_are_plain_packages_that_coexist_where_the_release_has_no_modules: issue #542, the
# alma10 half.
#
# The 10 generation of the RHEL family has no module streams; Node.js is one package per major
# (`nodejs` is the default major 22, `nodejs24` is 24, each with its own `-npm` package), and the
# majors install beside each other with a versioned interpreter each (`/usr/bin/node-22`,
# `/usr/bin/node-24`). Through the generic package units' stand-in (the argv the agent starts for
# these packages):
#   * the availability query the agent runs (`dnf -q -C list --available -- <names>`) lists the four
#     package names; the inverse control is `nodejs20`, a name that looks like the pattern and that
#     the sources do not carry, which must NOT be listed;
#   * installing 24 and then the default major leaves BOTH interpreters running (v24 and v22);
#   * the dry run of removing 24 names its own packages and not the default major's;
#   * removing the default major (npm first, as the agent does) leaves 24 running and no
#     `/usr/bin/node` at all, and removing 24 leaves no Node.js package.
# A release with module streams skips it (the module-stream assertion above holds there), as do the
# Debian and SUSE families.
assert_node_majors_are_plain_packages_that_coexist_where_the_release_has_no_modules() {
  local listing available package version plan
  if [ "${MARAN_OS_FAMILY:-}" != rhel ]; then
    echo "SKIPPED on the ${MARAN_OS_FAMILY:-unknown} family: this assertion is for the RHEL family's release without module streams."
    return 0
  fi
  for package in nodejs nodejs-npm nodejs24 nodejs24-npm; do
    package_is_installed "$package" \
      && fail "${package} is installed before this assertion started, so installing it proves nothing"
  done
  dnf -q makecache >/dev/null 2>&1 || fail "dnf makecache failed, so the availability query below would test the network and not the parser's input"
  if listing="$(dnf -C module list nodejs 2>&1)"; then
    echo "SKIPPED: this release carries Node.js module streams; assert_a_nodejs_module_stream_installs_once_and_uninstalls_clean holds them."
    return 0
  fi
  case "$listing" in
    *"No matching Modules"*) ;;
    *) fail "dnf -C module list nodejs failed, but not with 'No matching Modules', so this is not a release without modules: ${listing}" ;;
  esac

  available="$(dnf -q -C list --available -- nodejs nodejs-npm nodejs20 nodejs24 nodejs24-npm 2>&1)" \
    || fail "the availability query failed: ${available}"
  for package in nodejs nodejs-npm nodejs24 nodejs24-npm; do
    printf '%s\n' "$available" | grep -Eq "^${package}\.[A-Za-z0-9_]+[[:space:]]" \
      || fail "the sources do not list ${package}, so the per-major package names are not what this release carries: ${available}"
  done
  printf '%s\n' "$available" | grep -Eq '^nodejs20\.' \
    && fail "the availability query listed nodejs20, a name this release does not carry, so the check above reads nothing"

  systemctl start 'maran-app-install@nodejs24.service' \
    || fail "maran-app-install@nodejs24.service failed on a release without module streams"
  systemctl start 'maran-app-install@nodejs24-npm.service' \
    || fail "maran-app-install@nodejs24-npm.service failed on a release without module streams"
  version="$(/usr/bin/node-24 --version 2>&1)" || fail "node-24 is not runnable after the install: ${version}"
  case "$version" in v24.*) ;; *) fail "nodejs24 put node '${version}' at /usr/bin/node-24, not v24.x" ;; esac
  /usr/bin/npm-24 --version >/dev/null 2>&1 || fail "/usr/bin/npm-24 does not run after nodejs24-npm was installed"
  [ ! -e /usr/bin/node ] \
    || fail "/usr/bin/node exists with only nodejs24 installed, so the versioned file is not the only one a major can be asked for"

  systemctl start 'maran-app-install@nodejs.service' \
    || fail "maran-app-install@nodejs.service failed on a release without module streams"
  systemctl start 'maran-app-install@nodejs-npm.service' \
    || fail "maran-app-install@nodejs-npm.service failed on a release without module streams"
  version="$(/usr/bin/node-22 --version 2>&1)" || fail "node-22 is not runnable after the install: ${version}"
  case "$version" in v22.*) ;; *) fail "nodejs put node '${version}' at /usr/bin/node-22, not v22.x" ;; esac
  version="$(/usr/bin/node-24 --version 2>&1)" || fail "installing the default major broke node-24: ${version}"
  case "$version" in v24.*) ;; *) fail "node-24 reports '${version}' after the default major was installed beside it" ;; esac

  assert_a_removal_dry_run_changes_nothing nodejs24
  # Read from a variable: the dry run exits 1 after printing its plan, which a pipeline under
  # `pipefail` would report as the pipeline's own failure.
  plan="$(removal_dry_run nodejs24 || true)"
  if printf '%s\n' "$plan" | grep -Eq '^ nodejs(-npm)? '; then
    fail "the dry run of removing nodejs24 would take the default major's packages with it: ${plan}"
  fi
  printf '%s\n' "$plan" | grep -Eq '^ nodejs24-npm ' \
    || fail "the dry run of removing nodejs24 does not name its npm package, so the check above reads nothing: ${plan}"

  # The agent removes the npm package first and the interpreter after it.
  systemctl start 'maran-app-remove@nodejs-npm.service' || fail "maran-app-remove@nodejs-npm.service failed"
  systemctl start 'maran-app-remove@nodejs.service' || fail "maran-app-remove@nodejs.service failed"
  package_is_installed nodejs && fail "nodejs is still installed after its removal"
  package_is_installed nodejs-npm && fail "nodejs-npm is still installed after its removal"
  version="$(/usr/bin/node-24 --version 2>&1)" || fail "removing the default major took node-24 with it: ${version}"
  [ ! -e /usr/bin/node-22 ] || fail "/usr/bin/node-22 is still there after nodejs was removed"
  [ ! -e /usr/bin/node ] || fail "/usr/bin/node is still there after nodejs was removed"

  systemctl start 'maran-app-remove@nodejs24-npm.service' || fail "maran-app-remove@nodejs24-npm.service failed"
  systemctl start 'maran-app-remove@nodejs24.service' || fail "maran-app-remove@nodejs24.service failed"
  for package in nodejs nodejs-npm nodejs24 nodejs24-npm; do
    package_is_installed "$package" && fail "${package} is still installed after every removal"
  done
  command -v nginx >/dev/null 2>&1 \
    || fail "installing and removing Node.js packages took nginx, the panel's front, with it"

  echo "Node.js 24 and 22 were installed as plain packages beside each other (node-24 ${version}, node-22 v22.x), each run from its own interpreter, and removed leaving no package; a name the sources do not carry was not offered."
  echo "UNOBSERVED HERE: the units' sandbox and a booted PID 1, which the stand-in does not have."
}

# assert_the_uninstaller_removes_the_app_package_units: both templates go with the panel.
#
# The units are root command lines that survive a panel which no longer exists; a template left behind
# would let anyone able to start a system unit run a package manager by a name nobody remembers. The
# uninstaller's remove_systemd_units is RUN against both installed units, and a copy of it without
# the two `rm` lines is shown to leave them.
assert_the_uninstaller_removes_the_app_package_units() {
  local socket_path web_group mutant output unit_file
  socket_path="$(installer_value MARAN_API_SOCKET_PATH "$INSTALLER_ENTRY_POINT")"
  web_group="$(web_server_group_for_this_family)"

  run_services_step "$socket_path" "$web_group" 'install_units' \
    || fail "step 70's install_units failed, so there is no unit for the uninstaller to remove"
  # The RHEL-only Node.js pair is part of the set only where step 70 laid it down: the RHEL family.
  local all_units="$APP_INSTALL_UNIT_FILE $APP_REMOVE_UNIT_FILE"
  if [ "${MARAN_OS_FAMILY:-}" = rhel ]; then
    all_units="$all_units $APP_NODEJS_INSTALL_UNIT_FILE $APP_NODEJS_REMOVE_UNIT_FILE"
  fi
  for unit_file in $all_units; do
    [ -f "/etc/systemd/system/${unit_file}" ] \
      || fail "/etc/systemd/system/${unit_file} is absent before the uninstaller runs, so its absence afterwards would prove nothing"
  done

  mutant="$(mktemp)"
  sed -e '\|^  rm -f /etc/systemd/system/maran-app-install@.service$|d' \
      -e '\|^  rm -f /etc/systemd/system/maran-app-remove@.service$|d' \
      -e '\|^  rm -f /etc/systemd/system/maran-app-nodejs-install@.service$|d' \
      -e '\|^  rm -f /etc/systemd/system/maran-app-nodejs-remove@.service$|d' "$UNINSTALLER" > "$mutant"
  cmp -s "$UNINSTALLER" "$mutant" \
    && fail "uninstall.sh no longer carries the two 'rm -f /etc/systemd/system/maran-app-*@.service' lines in the form this assertion mutates; it would be mutating nothing"
  output="$(bash -c 'set -euo pipefail
. "$1"
remove_systemd_units' _ "$mutant" 2>&1)" \
    || fail "the mutated uninstaller's remove_systemd_units failed: ${output}"
  rm -f -- "$mutant"
  for unit_file in $all_units; do
    [ -f "/etc/systemd/system/${unit_file}" ] \
      || fail "an uninstaller WITHOUT the removal lines still removed ${unit_file}, so something else removes it and this assertion cannot attribute the removal below to those lines"
  done

  output="$(run_uninstaller 'remove_systemd_units' 2>&1)" \
    || fail "the real uninstaller's remove_systemd_units failed: ${output}"
  for unit_file in $all_units; do
    [ ! -e "/etc/systemd/system/${unit_file}" ] \
      || fail "the uninstaller left /etc/systemd/system/${unit_file} behind"
  done
  rm -f /etc/tmpfiles.d/maran-api.conf /etc/tmpfiles.d/maran-php.conf /etc/tmpfiles.d/maran-host.conf
  echo "The uninstaller removes both app package units — and a copy of it without those two lines leaves them, so this check can see a survivor."
}
