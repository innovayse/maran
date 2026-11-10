#!/usr/bin/env bash
# the app store's host side (issue #517): the first-run state, one app installed and removed through
# the package units' stand-in, and the uninstaller taking the two units back
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
  for unit_file in "$APP_INSTALL_UNIT_FILE" "$APP_REMOVE_UNIT_FILE"; do
    [ -f "/etc/systemd/system/${unit_file}" ] \
      || fail "/etc/systemd/system/${unit_file} is absent before the uninstaller runs, so its absence afterwards would prove nothing"
  done

  mutant="$(mktemp)"
  sed -e '\|^  rm -f /etc/systemd/system/maran-app-install@.service$|d' \
      -e '\|^  rm -f /etc/systemd/system/maran-app-remove@.service$|d' "$UNINSTALLER" > "$mutant"
  cmp -s "$UNINSTALLER" "$mutant" \
    && fail "uninstall.sh no longer carries the two 'rm -f /etc/systemd/system/maran-app-*@.service' lines in the form this assertion mutates; it would be mutating nothing"
  output="$(bash -c 'set -euo pipefail
. "$1"
remove_systemd_units' _ "$mutant" 2>&1)" \
    || fail "the mutated uninstaller's remove_systemd_units failed: ${output}"
  rm -f -- "$mutant"
  for unit_file in "$APP_INSTALL_UNIT_FILE" "$APP_REMOVE_UNIT_FILE"; do
    [ -f "/etc/systemd/system/${unit_file}" ] \
      || fail "an uninstaller WITHOUT the removal lines still removed ${unit_file}, so something else removes it and this assertion cannot attribute the removal below to those lines"
  done

  output="$(run_uninstaller 'remove_systemd_units' 2>&1)" \
    || fail "the real uninstaller's remove_systemd_units failed: ${output}"
  for unit_file in "$APP_INSTALL_UNIT_FILE" "$APP_REMOVE_UNIT_FILE"; do
    [ ! -e "/etc/systemd/system/${unit_file}" ] \
      || fail "the uninstaller left /etc/systemd/system/${unit_file} behind"
  done
  rm -f /etc/tmpfiles.d/maran-api.conf /etc/tmpfiles.d/maran-php.conf /etc/tmpfiles.d/maran-host.conf
  echo "The uninstaller removes both app package units — and a copy of it without those two lines leaves them, so this check can see a survivor."
}
