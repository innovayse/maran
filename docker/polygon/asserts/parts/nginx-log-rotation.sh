# Log rotation reloads nginx and never signals it with USR1 (issue #490).
#
# SIGUSR1 makes every nginx WORKER reopen all the logs by path as the worker user, which cannot
# traverse /var/log/maran (root:<service group> 0750). Each rotation therefore logged
# `[emerg] open() ... (13: Permission denied)` and left the workers writing into the renamed file.
# Three senders existed: the two Maran stanzas and the distribution's own /etc/logrotate.d/nginx.
#
# Checked, on the artefacts the system reads:
#   * the shipped maran-sites and maran-panel policies: no executable USR1, an executable
#     `nginx -s reload`, and the REAL logrotate accepts both;
#   * make_distro_nginx_logrotate_reload, run against a Debian-shaped and an RHEL-shaped copy of the
#     distribution stanza: the signal is gone, the reload is in, the glob and options survive, a
#     second run changes nothing, and a stanza with no postrotate is refused;
#   * inverse controls: the signal detector must FIND USR1 in a stanza that has it (a detector that
#     matches nothing passes every file), and the real logrotate must REFUSE a broken policy.
#
# UNOBSERVED HERE: a rotation actually delivered to a running nginx. This image runs no nginx master
# and no timer, so that no worker logs [emerg] is a consequence of the reload semantics (the root
# master opens the files, new workers inherit descriptors), not something measured by this suite.

# signals_usr1 FILE: success when FILE has a non-comment line naming USR1 or the distro's rotate verb.
signals_usr1() {
  grep -Ev '^[[:space:]]*#' "$1" | grep -Eq 'USR1|nginx rotate'
}

assert_nginx_log_rotation_reloads_and_never_signals_usr1() {
  local sites="${INSTALLER_ROOT}/logrotate/maran-sites" panel="${INSTALLER_ROOT}/logrotate/maran-panel"
  local scratch policy debian rhel out state
  [ -r "$sites" ] || fail "installer/logrotate/maran-sites is not in this image; the suite Dockerfile must COPY it"
  [ -r "$panel" ] || fail "installer/logrotate/maran-panel is not in this image; the suite Dockerfile must COPY it"
  command -v logrotate >/dev/null 2>&1 || fail "logrotate is not installed in this image; nothing here could say whether the policies parse"
  scratch="$(mktemp -d)"
  state="${scratch}/logrotate.status"

  # Positive control for the detector, before it is trusted with an absence.
  printf '/x.log {\n    postrotate\n        kill -USR1 1\n    endscript\n}\n' > "${scratch}/with-usr1"
  signals_usr1 "${scratch}/with-usr1" \
    || { rm -rf "$scratch"; fail "the USR1 detector did not find USR1 in a stanza that has it, so its silence below means nothing"; }
  printf '# kill -USR1 only in a comment\n/x.log {\n    postrotate\n        nginx -s reload\n    endscript\n}\n' > "${scratch}/comment-only"
  if signals_usr1 "${scratch}/comment-only"; then
    rm -rf "$scratch"; fail "the USR1 detector fired on a comment; it would reject the shipped policies' explanations"
  fi

  for policy in "$sites" "$panel"; do
    if signals_usr1 "$policy"; then
      rm -rf "$scratch"; fail "${policy} signals nginx with USR1 again; its workers will log [emerg] open() Permission denied on every rotation (issue #490)"
    fi
    grep -Ev '^[[:space:]]*#' "$policy" | grep -Fq 'nginx -s reload' \
      || { rm -rf "$scratch"; fail "${policy} does not reload nginx after rotating, so nginx keeps writing into the renamed file"; }
    out="$(logrotate --debug --state "$state" "$policy" 2>&1)" \
      || { rm -rf "$scratch"; fail "logrotate refused ${policy}: ${out}"; }
  done
  # Inverse control for the parser: a policy it must refuse.
  sed 's/^    rotate 14$/    rotate twelve/' "$sites" > "${scratch}/broken"
  if logrotate --debug --state "$state" "${scratch}/broken" >/dev/null 2>&1; then
    rm -rf "$scratch"; fail "logrotate ACCEPTED a policy reading 'rotate twelve', so its acceptance of the shipped policies proves nothing"
  fi

  # The distribution's stanza, in the two shapes the supported families ship.
  debian="${scratch}/debian-nginx"
  printf '%s\n' '/var/log/nginx/*.log {' '	daily' '	rotate 14' '	create 0640 www-data adm' '	sharedscripts' \
    '	prerotate' '		if [ -d /etc/logrotate.d/httpd-prerotate ]; then \' '			run-parts /etc/logrotate.d/httpd-prerotate; \' '		fi \' '	endscript' \
    '	postrotate' '		invoke-rc.d nginx rotate >/dev/null 2>&1' '	endscript' '}' > "$debian"
  rhel="${scratch}/rhel-nginx"
  printf '%s\n' '/var/log/nginx/*.log {' '    create 0640 nginx root' '    missingok' '    notifempty' '    sharedscripts' \
    '    delaycompress' '    postrotate' '        /bin/kill -USR1 `cat /run/nginx.pid 2>/dev/null` 2>/dev/null || true' '    endscript' '}' > "$rhel"
  signals_usr1 "$debian" && signals_usr1 "$rhel" \
    || { rm -rf "$scratch"; fail "the planted distribution stanzas do not signal USR1, so rewriting them proves nothing"; }

  local file parsed=0
  for file in "$debian" "$rhel"; do
    bash -c 'set -euo pipefail
eval "$(sed -n "/^make_distro_nginx_logrotate_reload()/,/^}/p" "$1")"
make_distro_nginx_logrotate_reload "$2"' _ "$NGINX_STEP" "$file" \
      || { rm -rf "$scratch"; fail "make_distro_nginx_logrotate_reload failed on ${file}"; }
    if signals_usr1 "$file"; then
      rm -rf "$scratch"; fail "the distribution stanza still signals USR1 after make_distro_nginx_logrotate_reload (${file##*/})"
    fi
    grep -Fq 'nginx -s reload' "$file" || { rm -rf "$scratch"; fail "the rewritten distribution stanza (${file##*/}) does not reload nginx"; }
    grep -Fq '/var/log/nginx/*.log {' "$file" && grep -Fq 'create 0640' "$file" \
      || { rm -rf "$scratch"; fail "the rewrite of ${file##*/} lost the distribution's glob or its create line"; }
    # The real logrotate parses only THIS image's family's stanza: the other family's names its own
    # account (nginx on RHEL, www-data on Debian), which this image does not have, and logrotate refuses
    # an unknown user before it reads anything else. The rewrite itself is checked on both above.
    if [ "${file##*/}" = "${MARAN_OS_FAMILY}-nginx" ]; then
      out="$(logrotate --debug --state "$state" "$file" 2>&1)" \
        || { rm -rf "$scratch"; fail "logrotate refused the rewritten distribution stanza (${file##*/}): ${out}"; }
      parsed=$((parsed + 1))
    fi
  done
  case "${MARAN_OS_FAMILY:-}" in
    debian|rhel)
      [ "$parsed" -eq 1 ] \
        || { rm -rf "$scratch"; fail "no rewritten stanza matched MARAN_OS_FAMILY='${MARAN_OS_FAMILY}', so the real logrotate parsed none of them"; } ;;
    *)
      echo "UNOBSERVED HERE: no planted distribution stanza has the shape of the '${MARAN_OS_FAMILY:-unset}' family, so the real logrotate parsed neither rewrite on this image." ;;
  esac
  grep -Fq 'run-parts /etc/logrotate.d/httpd-prerotate' "$debian" \
    || { rm -rf "$scratch"; fail "the rewrite damaged the Debian stanza's prerotate script"; }

  # Idempotent: a second run is byte-identical.
  cp -p "$debian" "${scratch}/debian-once"
  bash -c 'set -euo pipefail
eval "$(sed -n "/^make_distro_nginx_logrotate_reload()/,/^}/p" "$1")"
make_distro_nginx_logrotate_reload "$2"' _ "$NGINX_STEP" "$debian"
  cmp -s "$debian" "${scratch}/debian-once" || { rm -rf "$scratch"; fail "a second make_distro_nginx_logrotate_reload run changed the file"; }

  # Refusal, with its inverse control being the two accepted runs above: no postrotate stops the install.
  printf '%s\n' '/var/log/nginx/*.log {' '	daily' '}' > "${scratch}/no-postrotate"
  if bash -c 'set -euo pipefail
eval "$(sed -n "/^make_distro_nginx_logrotate_reload()/,/^}/p" "$1")"
make_distro_nginx_logrotate_reload "$2"' _ "$NGINX_STEP" "${scratch}/no-postrotate" >/dev/null 2>&1; then
    rm -rf "$scratch"; fail "make_distro_nginx_logrotate_reload accepted a stanza with no postrotate script"
  fi
  # A family that writes its signalling in lastaction (openSUSE's package, `nginx -s reopen`) is rewritten like
  # one that uses postrotate; the refusal above, which has neither, is its inverse control.
  printf '%s\n' '/var/log/nginx/*.log {' '	lastaction' '		[ -f /run/nginx.pid ] && /usr/sbin/nginx -s reopen' '	endscript' '}' > "${scratch}/suse-lastaction"
  bash -c 'set -euo pipefail
eval "$(sed -n "/^make_distro_nginx_logrotate_reload()/,/^}/p" "$1")"
make_distro_nginx_logrotate_reload "$2"' _ "$NGINX_STEP" "${scratch}/suse-lastaction" >/dev/null 2>&1 \
    || { rm -rf "$scratch"; fail "make_distro_nginx_logrotate_reload refused a stanza that signals from lastaction"; }
  grep -q 'nginx -s reload' "${scratch}/suse-lastaction" && ! grep -q 'reopen' "${scratch}/suse-lastaction" \
    || { rm -rf "$scratch"; fail "make_distro_nginx_logrotate_reload left the lastaction stanza signalling reopen"; }
  # A stanza whose rotate verb hides OUTSIDE postrotate is still caught by the post-check.
  printf '%s\n' '/var/log/nginx/*.log {' '	prerotate' '		kill -USR1 1' '	endscript' '	postrotate' '		true' '	endscript' '}' > "${scratch}/usr1-in-prerotate"
  if bash -c 'set -euo pipefail
eval "$(sed -n "/^make_distro_nginx_logrotate_reload()/,/^}/p" "$1")"
make_distro_nginx_logrotate_reload "$2"' _ "$NGINX_STEP" "${scratch}/usr1-in-prerotate" >/dev/null 2>&1; then
    rm -rf "$scratch"; fail "make_distro_nginx_logrotate_reload accepted a stanza that signals USR1 outside postrotate"
  fi

  # The SUSE family's stanza is not one of the two shapes planted above, and nothing in this
  # repository has seen it, so the planted copies say nothing about it. The image has the REAL file
  # (the nginx package is installed), so on this family the rewrite is run on a copy of what the
  # package shipped. Two honest outcomes besides a pass: the package ships no stanza (nothing to
  # rewrite, said aloud), or step 80 already rewrote it earlier in this run (the shipped shape is
  # gone, so a rewrite of the copy proves nothing, said aloud).
  local distro="/etc/logrotate.d/nginx" suse_note=""
  if [ "${MARAN_OS_FAMILY:-}" = suse ]; then
    if [ ! -f "$distro" ]; then
      suse_note="UNOBSERVED HERE: this family's nginx package ships no ${distro}, so there is no distribution stanza to rewrite and nothing was checked about it."
    elif ! signals_usr1 "$distro"; then
      suse_note="UNOBSERVED HERE: ${distro} carries no USR1 or rotate verb in this image (step 80 rewrote it earlier in this run, or the package never had one), so the shape the package SHIPS is not visible and rewriting a copy proves nothing."
    else
      cp -p -- "$distro" "${scratch}/suse-nginx"
      bash -c 'set -euo pipefail
eval "$(sed -n "/^make_distro_nginx_logrotate_reload()/,/^}/p" "$1")"
make_distro_nginx_logrotate_reload "$2"' _ "$NGINX_STEP" "${scratch}/suse-nginx" \
        || { rm -rf "$scratch"; fail "make_distro_nginx_logrotate_reload failed on a copy of this family's shipped ${distro}; the installer would stop on a real host"; }
      if signals_usr1 "${scratch}/suse-nginx"; then
        rm -rf "$scratch"; fail "a copy of this family's shipped ${distro} still signals USR1 after make_distro_nginx_logrotate_reload"
      fi
      grep -Fq 'nginx -s reload' "${scratch}/suse-nginx" \
        || { rm -rf "$scratch"; fail "the rewrite of this family's shipped ${distro} does not reload nginx"; }
      out="$(logrotate --debug --state "$state" "${scratch}/suse-nginx" 2>&1)" \
        || { rm -rf "$scratch"; fail "logrotate refused the rewritten copy of this family's shipped ${distro}: ${out}"; }
      suse_note="This family's SHIPPED ${distro} (a copy of the real file) is rewritten to reload and parsed by the real logrotate."
    fi
  fi

  rm -rf "$scratch"
  [ -z "$suse_note" ] || echo "$suse_note"
  echo "Rotation reloads nginx: maran-sites and maran-panel carry no USR1 and are parsed by the real logrotate; the distribution's stanza (Debian and RHEL shapes) is rewritten to reload, idempotently, and a stanza it cannot rewrite stops the install."
  echo "UNOBSERVED HERE: a rotation delivered to a running nginx — this image runs no nginx master and no timer, so the absence of worker [emerg] lines rests on reload semantics, not on a measurement."
}
