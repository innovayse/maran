#!/usr/bin/env bash
# 89-dns.sh: the packaged daemon's masks, and the names the installer and the agent both spell
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# What this part CANNOT see, stated here and in its output rather than implied: it never starts a
# named, so it says nothing about AppArmor or SELinux confinement of the daemon, about whether the
# packages' file lists match the binary paths the step names, or about whether a socket is bound
# before the administrator turns DNS on. Those need a real-host run with a named
# fixture that no polygon suite provides yet (dns_zone_files_on_a_real_host.rs runs only the zone checker), and are UNOBSERVED here.

readonly DNS_UNIT_SOURCE="${INSTALLER_ROOT}/systemd/maran-dns.service"

# dns_image_family: the family of THIS image, in the spelling step 89-dns.sh keys on.
dns_image_family() {
  if [ -f /etc/debian_version ]; then echo debian; else echo rhel; fi
}

# dns_remove_unit_link <unit>: take a unit file or mask link out of the unit directory. The target
# is built with `:?` so an unset variable stops the script instead of widening the removal.
dns_remove_unit_link() {
  rm -f -- "${MARAN_DNS_UNIT_DIR:?}/${1:?}"
}

# assert_the_dns_package_is_refused_while_a_packaged_unit_is_startable: the ordering of step_dns made
# mechanical. With no mask in place `install_dns_packages` must exit non-zero BEFORE it reaches the
# package manager. The inverse control follows: once the masks exist it must get past the mask
# check and reach `pkg_install` (stubbed to exit 42), so the refusal above is shown to fire for the
# missing mask and for nothing else.
assert_the_dns_package_is_refused_while_a_packaged_unit_is_startable() {
  local family unit status=0
  family="$(dns_image_family)"

  for unit in $(MARAN_OS_FAMILY="$family" dns_packaged_units_for_family); do
    dns_remove_unit_link "$unit"
  done

  ( MARAN_OS_FAMILY="$family"; pkg_install() { exit 42; }; install_dns_packages ) >/dev/null 2>&1 || status=$?
  [ "$status" -ne 0 ] \
    || fail "install_dns_packages returned success with the packaged BIND unit(s) not masked. On the Debian family the package's postinst starts the distribution's own resolver, so the ordering the step exists for was not enforced."
  [ "$status" -ne 42 ] \
    || fail "install_dns_packages reached the package manager with the packaged BIND unit(s) not masked."

  status=0
  ( MARAN_OS_FAMILY="$family"; mask_packaged_dns >/dev/null 2>&1; pkg_install() { exit 42; }; install_dns_packages ) >/dev/null 2>&1 || status=$?
  [ "$status" -eq 42 ] \
    || fail "with the masks in place install_dns_packages exited ${status} instead of reaching pkg_install (42). The refusal above fired for a reason other than the missing mask, so it proved nothing (rules/testing.md: a refusing gate needs an inverse control)."

  for unit in $(MARAN_OS_FAMILY="$family" dns_packaged_units_for_family); do
    dns_remove_unit_link "$unit"
  done
  echo "install_dns_packages refuses while a packaged unit is startable and reaches the package manager once every one is masked (${family}). UNOBSERVED HERE: that the postinst would have started the daemon — this image boots no systemd."
}

# assert_the_dns_mask_covers_every_packaged_unit: the mask is a link to /dev/null for each unit name
# the family's package can start the daemon under, and a REAL file at one of those paths is refused
# and left alone rather than deleted.
assert_the_dns_mask_covers_every_packaged_unit() {
  local family unit units first
  family="$(dns_image_family)"
  units="$(MARAN_OS_FAMILY="$family" dns_packaged_units_for_family)"
  [ -n "$units" ] || fail "dns_packaged_units_for_family answered nothing for '${family}', so no mask was checked (rules/testing.md)"

  ( MARAN_OS_FAMILY="$family"; mask_packaged_dns ) >/dev/null 2>&1 \
    || fail "mask_packaged_dns failed on a host with no packaged BIND unit at all"
  for unit in $units; do
    [ "$(readlink -- "${MARAN_DNS_UNIT_DIR}/${unit}" 2>/dev/null || true)" = "/dev/null" ] \
      || fail "after mask_packaged_dns, ${MARAN_DNS_UNIT_DIR}/${unit} is not a link to /dev/null, so that name could still start the distribution's daemon"
    dns_remove_unit_link "$unit"
  done

  first="${units%% *}"
  printf '[Service]\nExecStart=/bin/true\n' > "${MARAN_DNS_UNIT_DIR}/${first}"
  if ( MARAN_OS_FAMILY="$family"; mask_packaged_dns ) >/dev/null 2>&1; then
    dns_remove_unit_link "$first"
    fail "mask_packaged_dns replaced an operator's real ${first}. It must refuse and leave it in place."
  fi
  if [ ! -f "${MARAN_DNS_UNIT_DIR}/${first}" ] || [ -L "${MARAN_DNS_UNIT_DIR}/${first}" ]; then
    dns_remove_unit_link "$first"
    fail "mask_packaged_dns refused a real ${first} but did not leave it where it was"
  fi
  dns_remove_unit_link "$first"
  echo "Every packaged BIND unit name (${units}) is masked, and an operator's own file at one of them is refused and kept."
}

# assert_the_installer_and_the_agent_spell_the_dns_names_the_same: the seam between the two halves
# of DNS. The installer creates the directories, the key and the environment file; the agent
# renders a configuration that names them, runs the binaries by absolute path and tells systemd a
# unit name. A drift is silent by construction: the installer builds a directory the daemon cannot
# write, or the agent runs a checker at a path the package never had, with every gate green.
#
# It compares the agent's SOURCE TEXT with the installer's own functions, which this script has
# sourced. What it therefore cannot see is a value the agent computes instead of declares.
assert_the_installer_and_the_agent_spell_the_dns_names_the_same() {
  local compared=0 value planted family services_file installer_value unit_text config_path
  local function_name binary

  # The extractor's positive control, on the axis that can go blind.
  planted="/tmp/polygon-agent-dns-probe.rs"
  value="$(agent_string_after 'fn dns_user()' "$AGENT_DEBIAN_SERVICES")"
  [ -n "$value" ] \
    || fail "no string literal could be read out of dns_user() in ${AGENT_DEBIAN_SERVICES}; every agreement below would be between two empty strings (rules/testing.md)"
  sed "s/\"${value}\"/\"polygon-planted-name\"/" "$AGENT_DEBIAN_SERVICES" > "$planted"
  [ "$(agent_string_after 'fn dns_user()' "$planted")" = "polygon-planted-name" ] \
    || fail "the Rust literal extractor does not read a planted value out of a copy of debian_services.rs, so its agreements mean nothing."
  rm -f -- "${planted:?}"

  for family in debian rhel; do
    if [ "$family" = debian ]; then services_file="$AGENT_DEBIAN_SERVICES"; else services_file="$AGENT_RHEL_SERVICES"; fi

    value="$(agent_string_after 'fn dns_user()' "$services_file")"
    installer_value="$(MARAN_OS_FAMILY="$family" dns_user_for_family)"
    [ -n "$value" ] && [ "$value" = "$installer_value" ] \
      || fail "the DNS user has drifted on ${family}: 89-dns.sh gives its directories to '${installer_value}' and ${services_file} answers '${value}'."
    compared=$((compared + 1))

    value="$(agent_string_after 'fn dns_group()' "$services_file")"
    [ -n "$value" ] && [ "$value" = "$installer_value" ] \
      || fail "the DNS group has drifted on ${family}: 89-dns.sh uses '${installer_value}' and ${services_file} answers '${value}'."
    compared=$((compared + 1))

    value="$(agent_string_after 'fn dns_state_directory()' "$services_file")"
    installer_value="$(MARAN_OS_FAMILY="$family" dns_state_directory_for_family)"
    [ -n "$value" ] && [ "$value" = "$installer_value" ] \
      || fail "the DNS state directory has drifted on ${family}: 89-dns.sh creates '${installer_value}' and ${services_file} renders '${value}' into the daemon's configuration."
    compared=$((compared + 1))

    value="$(agent_string_after 'fn dns_zone_directory()' "$services_file")"
    [ "$value" = "${installer_value}/zones" ] \
      || fail "the DNS zone directory has drifted on ${family}: 89-dns.sh creates '${installer_value}/zones' and ${services_file} answers '${value}'."
    compared=$((compared + 1))

    for function_name in dns_checkconf_binary dns_checkzone_binary dns_control_binary; do
      binary="$(agent_string_after "fn ${function_name}()" "$services_file")"
      [ -n "$binary" ] \
        || fail "no string literal could be read out of ${function_name}() in ${services_file}"
      case " $(MARAN_OS_FAMILY="$family" dns_binaries_for_family) " in
        *" ${binary} "*) ;;
        *) fail "${function_name}() answers '${binary}' on ${family} and 89-dns.sh does not require that path to exist after the install (dns_binaries_for_family)." ;;
      esac
      compared=$((compared + 1))
    done
  done

  value="$(agent_string_after 'const DNS_CONFIG_DIRECTORY' "$AGENT_PATHS")"
  [ "$value" = "$MARAN_DNS_CONFIG_DIR" ] \
    || fail "the DNS configuration directory has drifted: 89-dns.sh creates '${MARAN_DNS_CONFIG_DIR}' and AgentPaths::DNS_CONFIG_DIRECTORY is '${value}'."
  compared=$((compared + 1))

  config_path="$(agent_string_after 'const DNS_CONFIG_PATH' "$AGENT_PATHS")"
  [ -n "$config_path" ] && [ "$(dirname -- "$config_path")" = "$MARAN_DNS_CONFIG_DIR" ] \
    || fail "AgentPaths::DNS_CONFIG_PATH is '${config_path}', which is not inside the directory 89-dns.sh creates (${MARAN_DNS_CONFIG_DIR})."
  compared=$((compared + 1))

  value="$(agent_string_after 'const DNS_CONTROL_KEY_PATH' "$AGENT_PATHS")"
  [ "$value" = "$MARAN_DNS_CONTROL_KEY" ] \
    || fail "the DNS control key path has drifted: 89-dns.sh writes '${MARAN_DNS_CONTROL_KEY}' and AgentPaths::DNS_CONTROL_KEY_PATH is '${value}'."
  compared=$((compared + 1))

  value="$(agent_string_after 'const DNS_CONTROL_KEY_NAME' "$AGENT_PATHS")"
  [ "$value" = "$MARAN_DNS_CONTROL_KEY_NAME" ] \
    || fail "the DNS control key name has drifted: 89-dns.sh writes the key as '${MARAN_DNS_CONTROL_KEY_NAME}' and AgentPaths::DNS_CONTROL_KEY_NAME is '${value}'. The rendered controls statement would name a key the file does not hold."
  compared=$((compared + 1))

  value="$(agent_string_after 'const DNS_UNIT' "$AGENT_PATHS")"
  [ "$value" = "$MARAN_DNS_UNIT_NAME" ] \
    || fail "the DNS unit name has drifted: 89-dns.sh installs '${MARAN_DNS_UNIT_NAME}' and AgentPaths::DNS_UNIT is '${value}'."
  compared=$((compared + 1))

  [ -r "$DNS_UNIT_SOURCE" ] || fail "${DNS_UNIT_SOURCE} is not in this image; the Dockerfile must COPY installer/systemd/maran-dns.service"
  unit_text="$(grep -vE '^[[:space:]]*(#|$)' "$DNS_UNIT_SOURCE")"
  printf '%s\n' "$unit_text" | grep -qxF "EnvironmentFile=${MARAN_DNS_ENVIRONMENT_FILE}" \
    || fail "maran-dns.service does not read EnvironmentFile=${MARAN_DNS_ENVIRONMENT_FILE}, the file 89-dns.sh writes the daemon's user into."
  compared=$((compared + 1))
  printf '%s\n' "$unit_text" | grep -qxF 'ExecStart=/usr/sbin/named -f -c '"${config_path}"' -u ${MARAN_DNS_USER}' \
    || fail "maran-dns.service's ExecStart is not exactly '/usr/sbin/named -f -c ${config_path} -u \${MARAN_DNS_USER}', so it does not start the daemon against the file the agent renders."
  compared=$((compared + 1))
  case " $(MARAN_OS_FAMILY="$(dns_image_family)" dns_binaries_for_family) " in
    *" /usr/sbin/named "*) ;;
    *) fail "maran-dns.service runs /usr/sbin/named and 89-dns.sh does not require that path to exist after the install." ;;
  esac
  compared=$((compared + 1))
  if printf '%s\n' "$unit_text" | grep -qE '^(ExecStartPre|ExecStartPost|ExecStop|ExecReload)[[:space:]]*='; then
    fail "maran-dns.service carries an ExecStartPre/ExecStartPost/ExecStop/ExecReload hook; the shipped unit must not."
  fi
  compared=$((compared + 1))

  echo "The installer and the agent spell the DNS names identically, compared and not assumed: ${compared} agreements (user, group, state and zone directories and the three binaries on BOTH families, the configuration directory and path, the control key path and name, the unit name, and the unit's ExecStart and EnvironmentFile). UNOBSERVED HERE: that the packages install those binaries at those paths — 89-dns.sh requires it after the install and fails by name."
}
