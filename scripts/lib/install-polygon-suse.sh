#!/usr/bin/env bash
# A LIBRARY, not a command: scripts/lib/install-polygon.sh sources this file.
#
# What the SUSE family owes that the other two families do not, observed on the SERVED artefact of a
# real install under real systemd, and never on a template or a rendered string. Four claims:
#
#   1. The firewall has ONE owner. The agent's table is in the kernel (`nft list ruleset` shows it) and
#      the distribution's firewalld is masked, not merely stopped (decision 220).
#   2. nginx serves the panel. `nginx -T` lists the panel's vhost among the files it loaded, which is
#      the server's own answer to "do you read this?" (this family's stock nginx.conf is believed not
#      to include conf.d, so a vhost written there is invisible until the installer adds the include).
#   3. FTPS is not offered (decision 222): the installer said so in plain words and exited 0, and no
#      vsftpd unit exists on the host.
#   4. Mail is not offered until measured: the same two observations.
#
# Every UNVERIFIED fact it leans on (the sentences the installer prints, the vhost path, the unit
# names) is read from the installer as it is in this tree; nothing about openSUSE was measured by us,
# so the FIRST run of this stage is what turns each of them into a fact or into a named failure.
#
# Each absence is paired with a PRESENCE of the same machinery, because a search that found nothing
# is a check that owes proof it was able to find something (rules/testing.md): `systemctl
# list-unit-files` is shown to list a unit that exists before it is trusted to say a unit does not;
# the `nginx -T` search is shown to find nginx.conf before it is trusted to say a file is missing, and
# to miss a file planted in a directory nothing includes; the install-log search is shown to find a
# line another step printed.
#
# Reads these globals set by install-polygon.sh: container, log, note, arch.

# The two sentences the installer prints when it skips a step, matched as fixed strings. They are the
# installer's own words (installer/lib/89-ftps.sh `step_ftps`, installer/lib/89-mail.sh `step_mail`).
SUSE_FTPS_SKIP_SENTENCE="FTPS is not available on this OS family"
SUSE_MAIL_SKIP_SENTENCE="Mail is not offered on this OS family"

# The panel's vhost on this family, the file `nginx -T` must name (installer/lib/80-nginx.sh
# `nginx_conf_dest`, SUSE arm).
SUSE_PANEL_VHOST="/etc/nginx/conf.d/maran.conf"

# suse_unit_files: the `systemctl list-unit-files` rows for one unit, empty when the host has none.
# `--no-legend` drops the header and the count, so emptiness IS the absence.
suse_unit_files() {
  docker exec "$container" systemctl list-unit-files --no-legend --no-pager "$1" 2>/dev/null | tr -d '\r' || true
}

# stage_suse_family: the four observations above. Called by install-polygon.sh only when the family
# is suse and the install itself passed.
stage_suse_family() {
  local before="$failures" ruleset state unit present listing checked=0 plant

  # 1a. The agent's table is in force. The ruleset is asked of the kernel (`nft list ruleset` in the
  # container's own network namespace), not of the file the installer rendered.
  if [ "$arch" != x86_64 ]; then
    echo "    UNOBSERVED HERE: the loaded ruleset on ${arch}. An emulated nft binary cannot drive the netlink interface of the host kernel (docker/polygon/images/README.md), so 'table inet maran' is not asked about."
  else
    ruleset="$(docker exec "$container" nft list ruleset 2>&1 | tr -d '\r' || true)"
    case "$ruleset" in
      *'table inet maran'*) checked=$((checked + 1)) ;;
      *) note "suse: 'nft list ruleset' does not show 'table inet maran', so the agent's firewall is not in the kernel: ${ruleset:0:300}" ;;
    esac
    # The inverse control: the same listing must NOT contain a table nobody loaded, or the match
    # above is satisfied by any text at all.
    case "$ruleset" in
      *'table inet maran_polygon_absent'*) note "suse: 'nft list ruleset' lists a table no step loaded, so the match on 'table inet maran' proves nothing" ;;
      *) checked=$((checked + 1)) ;;
    esac
  fi

  # 1b. firewalld is MASKED. The word comes from systemd, and the link is the mask itself; a unit
  # that is merely disabled or stopped answers `disabled` and could be started by a dependency.
  state="$(docker exec "$container" systemctl is-enabled firewalld.service 2>&1 | tr -d '\r' || true)"
  [ "$state" = "masked" ] \
    && checked=$((checked + 1)) \
    || note "suse: 'systemctl is-enabled firewalld.service' answers '${state}', not 'masked'; a second firewall could return"
  [ "$(docker exec "$container" readlink /etc/systemd/system/firewalld.service 2>/dev/null | tr -d '\r' || true)" = "/dev/null" ] \
    && checked=$((checked + 1)) \
    || note "suse: /etc/systemd/system/firewalld.service is not a link to /dev/null"
  # The inverse control: the same query must be able to say something else. A unit the install
  # enabled answers `enabled`, so a reader stuck on `masked` would be caught here.
  state="$(docker exec "$container" systemctl is-enabled maran-api.service 2>&1 | tr -d '\r' || true)"
  [ "$state" = "enabled" ] \
    && checked=$((checked + 1)) \
    || note "suse: 'systemctl is-enabled maran-api.service' answers '${state}', not 'enabled', so the 'masked' answer above is not shown to be a distinguishing one"
  state="$(docker exec "$container" systemctl is-active firewalld.service 2>&1 | tr -d '\r' || true)"
  [ "$state" != "active" ] \
    && checked=$((checked + 1)) \
    || note "suse: firewalld.service is active after the install"

  # 2. nginx's own list of the files it loaded. The positive control is nginx.conf itself; the
  # inverse control is a file planted in a directory nothing includes, which `nginx -t` accepts and
  # `nginx -T` does not list.
  listing="$(docker exec "$container" nginx -T 2>/dev/null | tr -d '\r' | grep -F '# configuration file ' || true)"
  case "$listing" in
    *'# configuration file /etc/nginx/nginx.conf:'*) checked=$((checked + 1)) ;;
    *) note "suse: 'nginx -T' lists no '# configuration file' header for nginx.conf, so this search cannot see which files nginx loaded" ;;
  esac
  case "$listing" in
    *"# configuration file ${SUSE_PANEL_VHOST}:"*) checked=$((checked + 1)) ;;
    *) note "suse: 'nginx -T' does not list ${SUSE_PANEL_VHOST}, so nginx would never serve the panel (loaded: $(printf '%s' "$listing" | tr '\n' ' ' | cut -c1-300))" ;;
  esac
  plant="/etc/nginx/polygon-not-included/zz-planted.conf"
  docker exec "$container" bash -c 'mkdir -p /etc/nginx/polygon-not-included && : > "$1"' _ "$plant"
  listing="$(docker exec "$container" nginx -T 2>/dev/null | tr -d '\r' | grep -F '# configuration file ' || true)"
  docker exec "$container" rm -rf /etc/nginx/polygon-not-included
  case "$listing" in
    *"$plant"*) note "suse: 'nginx -T' lists a file in a directory nothing includes, so its listing of ${SUSE_PANEL_VHOST} does not mean the file is served" ;;
    *) checked=$((checked + 1)) ;;
  esac

  # 3 and 4. FTPS and mail are skipped in plain words, and no unit for either exists. The unit lister
  # is first shown to list a unit that is there.
  listing="$(suse_unit_files maran-api.service)"
  case "$listing" in
    *maran-api.service*) checked=$((checked + 1)) ;;
    *) note "suse: 'systemctl list-unit-files' does not list maran-api.service, so its empty answers below prove nothing" ;;
  esac
  case "$(cat "$log" 2>/dev/null || true)" in
    *'Installing nginx vhost'*) checked=$((checked + 1)) ;;
    *) note "suse: the install log carries no line another step printed, so the sentence searches below read nothing" ;;
  esac
  case "$(cat "$log" 2>/dev/null || true)" in
    *"$SUSE_FTPS_SKIP_SENTENCE"*) checked=$((checked + 1)) ;;
    *) note "suse: the install log does not say '${SUSE_FTPS_SKIP_SENTENCE}'; FTPS must be skipped in plain words on this family" ;;
  esac
  case "$(cat "$log" 2>/dev/null || true)" in
    *"$SUSE_MAIL_SKIP_SENTENCE"*) checked=$((checked + 1)) ;;
    *) note "suse: the install log does not say '${SUSE_MAIL_SKIP_SENTENCE}'; mail must be skipped in plain words on this family" ;;
  esac
  for unit in maran-ftps.service vsftpd.service postfix.service dovecot.service rspamd.service; do
    present="$(suse_unit_files "$unit")"
    [ -z "$present" ] \
      && checked=$((checked + 1)) \
      || note "suse: the host has ${unit} (${present}), so a step this family does not offer left a unit behind"
  done

  if [ "$failures" -eq "$before" ]; then
    echo "    suse: ${checked} observations on the served artefacts: 'table inet maran' in the kernel's ruleset, firewalld masked (and a unit that is enabled reads 'enabled'), ${SUSE_PANEL_VHOST} listed by 'nginx -T' (and a file in an unincluded directory is not), FTPS and mail skipped in the installer's words with no unit left behind."
  fi
  echo "    UNOBSERVED HERE: SELinux (the container shares the host kernel and loads no policy, so 'enforcing' and any denial are unseen); that a boot of the host leaves the same firewall owner (a container is not rebooted here); the effect of the mask on a real firewalld that was RUNNING (the image's firewalld never gets an active network to manage); and the unattended-update hold list, which this run does not switch on (the build-time assertion in docker/polygon/asserts/parts/os-security-updates-step.sh reads it from 'zypper locks')."
}
