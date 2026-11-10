#!/usr/bin/env bash
# the panel's update-request units (issue #382): what step 69 (69-update-request.sh) installs and what the
# files may say
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The subject. `maran-update-request.service` is a root job started by `maran-update-request.path`
# when the panel's service account creates a marker file, so the whole of what an administrator's
# button can make root do is written in those two files and the tmpfiles snippet beside them. Four
# properties are worth a gate, because each one fails silently:
#
#   1. The service is the one fixed command, with exactly one pre-command that removes the marker.
#      A unit that gains an argument, a second command, an EnvironmentFile or a shell still parses,
#      still starts and still updates the panel, while letting a file the panel's account can write
#      choose what root runs.
#   2. The path unit triggers on exactly the marker. A `DirectoryNotEmpty=` or a `PathChanged=` on
#      the directory would start an update for any file the account dropped there.
#   3. The sandbox is the one the verb needs and the writable set is exactly the named paths, and
#      never `/var/lib/maran`, which the panel's account owns.
#   4. The request directory is root-owned 0770 on /run, and the API unit names it (and only it) as
#      the one extra place the panel may write.
#
# The release check's pair (issue #516), `maran-update-check.service` and `maran-update-check.path`, is
# held to the same standard in the same directory, with the differences that make it a different unit:
# the fixed command is `maran update --check` (it installs nothing), there is NO ExecStartPre (the
# marker stays while the check runs, because its existence is what the panel shows as "checking") and
# one ExecStopPost removes it, the timeout is pinned below the five minutes after which the panel stops
# believing a marker, and the writable set is narrower (no dump directory, only the request directory
# of /run). The two path units must fire on their own marker each: a check marker that started the
# update service would be an update caused by a button that promises to install nothing.
#
# Every checker below is shown to refuse before it is trusted: copies of the real file with ONE
# property broken at a time must each be refused, and copies that differ only in a comment must be
# ACCEPTED, so a refusal is known to come from the content and an acceptance to mean something.
# The assertions read the files' EFFECTIVE lines (comments and blanks removed) because the shipped
# files argue at length about the directives they leave out, and a search over the raw text finds the
# explanation and calls it a defect (rules/testing.md).
#
# UNOBSERVED HERE: this image boots no systemd. Nothing below starts the path unit, creates a marker
# or runs the service; a directive the running systemd does not know is logged and IGNORED, so the
# text can name a directive the loaded unit does not have. That the unit starts the verb under its
# sandbox on a booted host belongs to the real-host run (the threat note's "what the author could not
# verify").

readonly UPDATE_REQUEST_SERVICE_SOURCE="${INSTALLER_ROOT}/systemd/maran-update-request.service"
readonly UPDATE_REQUEST_PATH_SOURCE="${INSTALLER_ROOT}/systemd/maran-update-request.path"
readonly UPDATE_REQUEST_TMPFILES_SOURCE="${INSTALLER_ROOT}/systemd/maran-update-request.tmpfiles.conf"
readonly UPDATE_REQUEST_API_UNIT_SOURCE="${INSTALLER_ROOT}/systemd/maran-api.service"
readonly UPDATE_REQUEST_STEP="${INSTALLER_LIB}/69-update-request.sh"
readonly UPDATE_CHECK_SERVICE_SOURCE="${INSTALLER_ROOT}/systemd/maran-update-check.service"
readonly UPDATE_CHECK_PATH_SOURCE="${INSTALLER_ROOT}/systemd/maran-update-check.path"

# The lines the design fixes, spelled out here and not read from the units: deleting or loosening a
# line in a unit cannot also delete or loosen its check.
readonly UPDATE_REQUEST_EXEC_EXPECTED="ExecStart=/usr/local/sbin/maran update --requested"
readonly UPDATE_REQUEST_PRE_EXPECTED="ExecStartPre=/usr/bin/rm -f -- /run/maran-update-request/requested"
readonly UPDATE_REQUEST_RW_EXPECTED="ReadWritePaths=/usr/local/maran /etc/maran /var/backups/maran-update /var/lib/maran-cli-staging /run"
readonly UPDATE_REQUEST_PATH_EXPECTED="PathExists=/run/maran-update-request/requested"
readonly UPDATE_REQUEST_TMPFILES_EXPECTED="d /run/maran-update-request 0770 root __MARAN_GROUP__ -"
readonly UPDATE_CHECK_EXEC_EXPECTED="ExecStart=/usr/local/sbin/maran update --check"
readonly UPDATE_CHECK_STOP_EXPECTED="ExecStopPost=/usr/bin/rm -f -- /run/maran-update-request/check-requested"
readonly UPDATE_CHECK_RW_EXPECTED="ReadWritePaths=/usr/local/maran /etc/maran /var/lib/maran-cli-staging /run/maran-update-request"
readonly UPDATE_CHECK_PATH_EXPECTED="PathExists=/run/maran-update-request/check-requested"
readonly UPDATE_CHECK_TIMEOUT_EXPECTED="TimeoutStartSec=4min"

# update_request_effective_lines: a file with its comments and blank lines removed. Read through a
# variable by the checkers below, never a pipe: under pipefail a `printf | grep -q` can fail with
# SIGPIPE when grep exits first, and a checker that flakes on its own plumbing gets switched off.
update_request_effective_lines() {
  grep -v '^[[:space:]]*#' "$1" | grep -v '^[[:space:]]*$' || true
}

# update_request_service_defect: prints the first reason the service at $1 is not the one the design
# allows, and prints nothing when it is.
update_request_service_defect() {
  local file="$1" effective key token count
  effective="$(update_request_effective_lines "$file")"
  eff_grep() { grep "$@" <<<"$effective"; }

  # One fixed argv, and it is the expected one.
  count="$(eff_grep -c '^ExecStart=' || true)"
  [ "$count" -eq 1 ] || { echo "has ${count} ExecStart= lines, not exactly one"; return 0; }
  [ "$(eff_grep '^ExecStart=')" = "$UPDATE_REQUEST_EXEC_EXPECTED" ] \
    || { echo "ExecStart= is not '${UPDATE_REQUEST_EXEC_EXPECTED}'"; return 0; }
  # Exactly one pre-command, the one that consumes the marker. Anything else it could name runs as root.
  count="$(eff_grep -c '^ExecStartPre=' || true)"
  [ "$count" -eq 1 ] || { echo "has ${count} ExecStartPre= lines, not exactly the one that removes the marker"; return 0; }
  [ "$(eff_grep '^ExecStartPre=')" = "$UPDATE_REQUEST_PRE_EXPECTED" ] \
    || { echo "ExecStartPre= is not '${UPDATE_REQUEST_PRE_EXPECTED}'"; return 0; }
  if eff_grep -Eq '^(ExecStartPost|ExecStop|ExecStopPost|ExecReload|ExecCondition)='; then
    echo "has a second command (ExecStartPost=, ExecStop=, ExecStopPost=, ExecReload= or ExecCondition=), and anything it names runs as root"; return 0
  fi
  if eff_grep -Eq '(^|[ =/])(sh|bash|dash)( |$)|[ ]-c[ ]'; then
    echo "runs a shell"; return 0
  fi
  if eff_grep -q '^EnvironmentFile='; then
    echo "reads an EnvironmentFile=, so a file other than the unit chooses the environment of a root job"; return 0
  fi

  # Whom it runs as, and how it ends.
  eff_grep -qx 'Type=oneshot' || { echo "is not Type=oneshot, so a failed run would not show as failed"; return 0; }
  eff_grep -qx 'User=root' || { echo "does not state User=root; the verb is the wrapper's root-only command"; return 0; }
  for key in Type User RemainAfterExit; do
    [ "$(eff_grep -c "^${key}=" || true)" -le 1 ] || { echo "${key}= appears more than once (systemd keeps the last)"; return 0; }
  done
  if eff_grep -q '^RemainAfterExit=' && ! eff_grep -qx 'RemainAfterExit=no'; then
    echo "sets RemainAfterExit to something other than no, which hides a request that did nothing"; return 0
  fi
  count="$(eff_grep '^TimeoutStartSec=' | sed 's/^TimeoutStartSec=//' | head -1 || true)"
  case "$count" in
    ''|0|infinity) echo "has no finite TimeoutStartSec= (read: '${count}'), so a hung update holds the update lock for ever"; return 0 ;;
  esac

  # The sandbox: each directive exactly once, with the exact value.
  for key in "NoNewPrivileges=yes" "PrivateTmp=yes" "ProtectHome=yes" "ProtectSystem=strict"; do
    count="$(eff_grep -c "^${key%%=*}=" || true)"
    [ "$count" -eq 1 ] || { echo "${key%%=*}= appears on ${count} lines, not exactly one (systemd keeps the last assignment)"; return 0; }
    eff_grep -qx "$key" || { echo "is missing '${key}'"; return 0; }
  done

  # The writable set: named paths, never a parent of them or a system tree, and never the panel's own.
  [ "$(eff_grep -c '^ReadWritePaths=' || true)" -eq 1 ] \
    || { echo "does not hold exactly one ReadWritePaths= line"; return 0; }
  for token in $(eff_grep '^ReadWritePaths=' | sed 's/^ReadWritePaths=//'); do
    token="${token#-}"
    case "$token" in
      # The one /etc path the verb writes: the release state, the check and running facts and the
      # failed-release memory live directly in it, and each is written beside itself and renamed, so
      # the directory (root:<service group> 0750, not owned by the panel's account) is the narrowest
      # thing that can be named. Every other /etc path stays refused below, and the exact-line check
      # after this loop pins the whole value.
      /etc/maran) ;;
      /|/var|/var/lib|/var/lib/maran|/var/backups|/etc|/usr|/boot|/home|/root|/opt|/srv|/lib|/bin|/sbin|/usr/lib|/usr/lib/*|/etc/*|/boot/*)
        echo "ReadWritePaths= names ${token}, which makes more than the named paths writable to a root job (or one the panel's own account owns)"; return 0 ;;
    esac
  done
  [ "$(eff_grep '^ReadWritePaths=')" = "$UPDATE_REQUEST_RW_EXPECTED" ] \
    || { echo "ReadWritePaths= is not '${UPDATE_REQUEST_RW_EXPECTED}'"; return 0; }

  # The loop stop for a marker root cannot remove: the service's own start limit, exactly once each.
  for key in "StartLimitIntervalSec=60" "StartLimitBurst=3"; do
    count="$(eff_grep -c "^${key%%=*}=" || true)"
    [ "$count" -eq 1 ] || { echo "${key%%=*}= appears on ${count} lines, not exactly one"; return 0; }
    eff_grep -qx "$key" || { echo "is missing '${key}', so a marker root cannot remove would restart the service without a limit"; return 0; }
  done

  # Nothing starts it but the path unit and an operator: no [Install], no section but the two.
  if eff_grep -Eq '^\[Install\]|^(WantedBy|RequiredBy|Alias|Also)='; then
    echo "has an [Install] section, so it can be enabled and started at boot"; return 0
  fi
  [ "$(eff_grep -c '^\[' || true)" -eq 2 ] \
    || { echo "does not have exactly the two sections [Unit] and [Service]"; return 0; }
  return 0
}

# update_check_service_defect: prints the first reason the check service at $1 is not the one the design
# allows, and prints nothing when it is (issue #516). It is the update service's checker with the
# differences the header names: no ExecStartPre, exactly one ExecStopPost, a pinned timeout and a
# narrower writable set.
update_check_service_defect() {
  local file="$1" effective key token count
  effective="$(update_request_effective_lines "$file")"
  eff_grep() { grep "$@" <<<"$effective"; }

  count="$(eff_grep -c '^ExecStart=' || true)"
  [ "$count" -eq 1 ] || { echo "has ${count} ExecStart= lines, not exactly one"; return 0; }
  [ "$(eff_grep '^ExecStart=')" = "$UPDATE_CHECK_EXEC_EXPECTED" ] \
    || { echo "ExecStart= is not '${UPDATE_CHECK_EXEC_EXPECTED}'"; return 0; }
  # The marker stays while the check runs, so nothing runs before the check.
  if eff_grep -q '^ExecStartPre='; then
    echo "has an ExecStartPre=, so the marker would be gone while the check runs and anything it names runs as root"; return 0
  fi
  count="$(eff_grep -c '^ExecStopPost=' || true)"
  [ "$count" -eq 1 ] || { echo "has ${count} ExecStopPost= lines, not exactly the one that removes the marker"; return 0; }
  [ "$(eff_grep '^ExecStopPost=')" = "$UPDATE_CHECK_STOP_EXPECTED" ] \
    || { echo "ExecStopPost= is not '${UPDATE_CHECK_STOP_EXPECTED}'"; return 0; }
  if eff_grep -Eq '^(ExecStartPost|ExecStop|ExecReload|ExecCondition)='; then
    echo "has a second command (ExecStartPost=, ExecStop=, ExecReload= or ExecCondition=), and anything it names runs as root"; return 0
  fi
  if eff_grep -Eq '(^|[ =/])(sh|bash|dash)( |$)|[ ]-c[ ]'; then
    echo "runs a shell"; return 0
  fi
  if eff_grep -q '^EnvironmentFile='; then
    echo "reads an EnvironmentFile=, so a file other than the unit chooses the environment of a root job"; return 0
  fi

  eff_grep -qx 'Type=oneshot' || { echo "is not Type=oneshot, so a failed run would not show as failed"; return 0; }
  eff_grep -qx 'User=root' || { echo "does not state User=root; the verb is the wrapper's root-only command"; return 0; }
  for key in Type User RemainAfterExit TimeoutStartSec; do
    [ "$(eff_grep -c "^${key}=" || true)" -le 1 ] || { echo "${key}= appears more than once (systemd keeps the last)"; return 0; }
  done
  if eff_grep -q '^RemainAfterExit=' && ! eff_grep -qx 'RemainAfterExit=no'; then
    echo "sets RemainAfterExit to something other than no, which hides a request that did nothing"; return 0
  fi
  # Pinned, not merely finite: the panel stops believing a marker after five minutes, and a check that can
  # run longer than that has a marker the panel calls stale while it is still running.
  eff_grep -qx "$UPDATE_CHECK_TIMEOUT_EXPECTED" \
    || { echo "TimeoutStartSec= is not '${UPDATE_CHECK_TIMEOUT_EXPECTED}', the finite limit that stays below the panel's five minutes"; return 0; }

  for key in "NoNewPrivileges=yes" "PrivateTmp=yes" "ProtectHome=yes" "ProtectSystem=strict"; do
    count="$(eff_grep -c "^${key%%=*}=" || true)"
    [ "$count" -eq 1 ] || { echo "${key%%=*}= appears on ${count} lines, not exactly one (systemd keeps the last assignment)"; return 0; }
    eff_grep -qx "$key" || { echo "is missing '${key}'"; return 0; }
  done

  [ "$(eff_grep -c '^ReadWritePaths=' || true)" -eq 1 ] \
    || { echo "does not hold exactly one ReadWritePaths= line"; return 0; }
  for token in $(eff_grep '^ReadWritePaths=' | sed 's/^ReadWritePaths=//'); do
    token="${token#-}"
    case "$token" in
      /etc/maran|/run/maran-update-request) ;;
      /run|/|/var|/var/lib|/var/lib/maran|/var/backups|/var/backups/*|/etc|/usr|/boot|/home|/root|/opt|/srv|/lib|/bin|/sbin|/usr/lib|/usr/lib/*|/etc/*|/boot/*)
        echo "ReadWritePaths= names ${token}, which makes more than the named paths writable to a root job (or one the panel's own account owns)"; return 0 ;;
    esac
  done
  [ "$(eff_grep '^ReadWritePaths=')" = "$UPDATE_CHECK_RW_EXPECTED" ] \
    || { echo "ReadWritePaths= is not '${UPDATE_CHECK_RW_EXPECTED}'"; return 0; }

  for key in "StartLimitIntervalSec=60" "StartLimitBurst=3"; do
    count="$(eff_grep -c "^${key%%=*}=" || true)"
    [ "$count" -eq 1 ] || { echo "${key%%=*}= appears on ${count} lines, not exactly one"; return 0; }
    eff_grep -qx "$key" || { echo "is missing '${key}', so a marker root cannot remove would restart the service without a limit"; return 0; }
  done

  if eff_grep -Eq '^\[Install\]|^(WantedBy|RequiredBy|Alias|Also)='; then
    echo "has an [Install] section, so it can be enabled and started at boot"; return 0
  fi
  [ "$(eff_grep -c '^\[' || true)" -eq 2 ] \
    || { echo "does not have exactly the two sections [Unit] and [Service]"; return 0; }
  return 0
}

# update_request_path_defect: prints the first reason the path unit at $1 is not the one the design
# allows, and prints nothing when it is. Every directive that makes a path unit FIRE is refused except
# the one expected, because a trigger on the directory is the same unattended root job by another
# spelling.
update_request_path_defect() {
  local file="$1" want_path="${2:-$UPDATE_REQUEST_PATH_EXPECTED}" want_unit="${3:-maran-update-request.service}" effective count key
  effective="$(update_request_effective_lines "$file")"
  eff_grep() { grep "$@" <<<"$effective"; }

  if eff_grep -Eq '^(PathChanged|PathModified|DirectoryNotEmpty)='; then
    echo "carries a trigger on a directory or a change (PathChanged=, PathModified= or DirectoryNotEmpty=), so any file the panel's account writes there would start an update"; return 0
  fi
  count="$(eff_grep -c '^PathExists=' || true)"
  [ "$count" -eq 1 ] || { echo "has ${count} PathExists= lines, not exactly one"; return 0; }
  [ "$(eff_grep '^PathExists=')" = "$want_path" ] \
    || { echo "PathExists= is not '${want_path}'"; return 0; }
  for key in "Unit=${want_unit}" "MakeDirectory=no" "WantedBy=multi-user.target"; do
    count="$(eff_grep -c "^${key%%=*}=" || true)"
    [ "$count" -eq 1 ] || { echo "${key%%=*}= appears on ${count} lines, not exactly one"; return 0; }
    eff_grep -qx "$key" || { echo "is missing '${key}'"; return 0; }
  done
  [ "$(eff_grep -c '^\[' || true)" -eq 3 ] \
    || { echo "does not have exactly the three sections [Unit], [Path] and [Install]"; return 0; }
  return 0
}

# update_request_tmpfiles_defect: prints the first reason the tmpfiles snippet at $1 is not the one the
# design allows, and prints nothing when it is. $2 is the line it must hold: the shipped snippet still
# carries its placeholder, a rendered one carries the panel's group.
update_request_tmpfiles_defect() {
  local file="$1" want="$2" effective
  effective="$(update_request_effective_lines "$file")"
  [ "$(grep -c '' <<<"$effective")" -eq 1 ] \
    || { echo "does not hold exactly one tmpfiles line"; return 0; }
  [ "$effective" = "$want" ] \
    || { echo "line is not '${want}' (the directory must be built root-owned, 0770, with no age-based cleanup)"; return 0; }
  return 0
}

# update_request_api_unit_defect: prints the first reason the API unit at $1 does not name the request
# directory as its one extra writable place, and prints nothing when it does. Both lists, because a
# directory the unit may write but not execute from is the arrangement the rest of the unit keeps.
update_request_api_unit_defect() {
  local file="$1" effective key line
  effective="$(update_request_effective_lines "$file")"
  for key in ReadWritePaths NoExecPaths; do
    line="$(grep "^${key}=" <<<"$effective" || true)"
    [ -n "$line" ] || { echo "has no ${key}= line"; return 0; }
    case " ${line#"${key}="} " in
      *" /run/maran-update-request "*) ;;
      *) echo "${key}= does not name /run/maran-update-request, so the panel cannot write its update request"; return 0 ;;
    esac
    case " ${line#"${key}="} " in
      *" /run "*|*" /run/ "*|*" /run/maran "*) echo "${key}= names a parent of /run/maran-update-request, which makes more of /run writable to the panel"; return 0 ;;
    esac
  done
  return 0
}

# update_request_plant: runs one planted control. $1 = checker function, $2 = source file, $3 = name,
# $4 = sed expression, the rest = extra arguments for the checker. The control must change the file
# (or it proves nothing) and the checker must refuse the result.
update_request_plant() {
  local checker="$1" source="$2" name="$3" control="$4" work defect
  shift 4
  work="$(mktemp)"
  sed "$control" "$source" > "$work"
  cmp -s "$source" "$work" \
    && fail "the planted control '${name}' changed nothing, so it proves nothing about ${checker}"
  defect="$("$checker" "$work" "$@")"
  rm -f -- "$work"
  [ -n "$defect" ] \
    || fail "${checker} ACCEPTED the planted defect '${name}'. A checker that accepts that guards nothing about a root job a web session can start."
}

# update_request_accept: runs one accepting control. A file that differs from the real one only in a
# comment must be accepted, or the checker has stopped reading effective lines.
update_request_accept() {
  local checker="$1" source="$2" comment="$3" work defect
  shift 3
  work="$(mktemp)"
  { cat "$source"; printf '%s\n' "$comment"; } > "$work"
  cmp -s "$source" "$work" \
    && fail "the accepting control for ${checker} changed nothing, so its acceptance proves nothing"
  defect="$("$checker" "$work" "$@")"
  rm -f -- "$work"
  [ -z "$defect" ] \
    || fail "${checker} REFUSED a file that differs from the real one only by the comment '${comment}' (${defect}); it refuses what it should accept"
}

# assert_the_update_request_units_are_what_the_design_allows: the checkers on the shipped files, then
# the checkers shown to refuse.
#
# The shipped files are checked first and the planted ones after, so a checker that refuses everything
# fails on the real file and one that refuses nothing fails on a planted break.
assert_the_update_request_units_are_what_the_design_allows() {
  local defect name control planted_count=0 accepted_count=0 rendered group_line

  defect="$(update_request_service_defect "$UPDATE_REQUEST_SERVICE_SOURCE")"
  [ -z "$defect" ] \
    || fail "maran-update-request.service ${defect}. This unit is a root job a web session can start; it must stay one fixed command with the sandbox of the threat note (docs/superpowers/notes/2026-11-07-panel-update-from-panel-threat-note.md)."
  defect="$(update_request_path_defect "$UPDATE_REQUEST_PATH_SOURCE")"
  [ -z "$defect" ] \
    || fail "maran-update-request.path ${defect}. It must fire on exactly the marker the panel creates."
  defect="$(update_check_service_defect "$UPDATE_CHECK_SERVICE_SOURCE")"
  [ -z "$defect" ] \
    || fail "maran-update-check.service ${defect}. This unit is a root job a web session can start; it must stay one fixed command with the sandbox of the threat note (docs/superpowers/notes/2026-11-09-panel-update-check-threat-note.md)."
  defect="$(update_request_path_defect "$UPDATE_CHECK_PATH_SOURCE" "$UPDATE_CHECK_PATH_EXPECTED" maran-update-check.service)"
  [ -z "$defect" ] \
    || fail "maran-update-check.path ${defect}. It must fire on exactly the check marker the panel creates, and start the check service and not the update service."
  defect="$(update_request_tmpfiles_defect "$UPDATE_REQUEST_TMPFILES_SOURCE" "$UPDATE_REQUEST_TMPFILES_EXPECTED")"
  [ -z "$defect" ] \
    || fail "maran-update-request.tmpfiles.conf ${defect}."
  defect="$(update_request_api_unit_defect "$UPDATE_REQUEST_API_UNIT_SOURCE")"
  [ -z "$defect" ] \
    || fail "maran-api.service ${defect}."

  # The inverse controls: a comment appended to each file, naming a directive that would be a defect if
  # it were a line.
  update_request_accept update_request_service_defect "$UPDATE_REQUEST_SERVICE_SOURCE" '# ProtectSystem=full'
  update_request_accept update_request_path_defect "$UPDATE_REQUEST_PATH_SOURCE" '# DirectoryNotEmpty=/run/maran-update-request'
  update_request_accept update_request_tmpfiles_defect "$UPDATE_REQUEST_TMPFILES_SOURCE" '# d /run/evil 0777 root root -' "$UPDATE_REQUEST_TMPFILES_EXPECTED"
  update_request_accept update_request_api_unit_defect "$UPDATE_REQUEST_API_UNIT_SOURCE" '# ReadWritePaths=/run'
  update_request_accept update_check_service_defect "$UPDATE_CHECK_SERVICE_SOURCE" '# ProtectSystem=full'
  update_request_accept update_request_path_defect "$UPDATE_CHECK_PATH_SOURCE" '# DirectoryNotEmpty=/run/maran-update-request' "$UPDATE_CHECK_PATH_EXPECTED" maran-update-check.service
  accepted_count=6

  # The refusals. Each is the real file with ONE thing broken; the name says what, the checker says why.
  while IFS='|' read -r name control; do
    [ -n "$name" ] || continue
    update_request_plant update_request_service_defect "$UPDATE_REQUEST_SERVICE_SOURCE" "$name" "$control"
    planted_count=$((planted_count + 1))
  done <<'SERVICE_CONTROLS'
an argument appended to ExecStart|s#^ExecStart=.*#& --yes#
a different verb in ExecStart|s#maran update --requested#maran update --yes#
a shell wrapped around ExecStart|s#^ExecStart=.*#ExecStart=/bin/sh -c "/usr/local/sbin/maran update --requested"#
a second ExecStart|s#^ExecStart=.*#&\nExecStart=/bin/true#
an ExecStartPost|s#^User=root#User=root\nExecStartPost=/bin/true#
a second ExecStartPre|s#^User=root#User=root\nExecStartPre=/bin/true#
an ExecStartPre that reads the marker|s#^ExecStartPre=.*#ExecStartPre=/usr/bin/cat /run/maran-update-request/requested#
rm without the end-of-options marker|s#rm -f --#rm -f#
User changed to the service account|s#^User=root#User=maran#
Type changed from oneshot|s#^Type=oneshot#Type=simple#
RemainAfterExit=yes|s#^RemainAfterExit=no#RemainAfterExit=yes#
an EnvironmentFile added|s#^UMask=0077#UMask=0077\nEnvironmentFile=/run/maran-update-request/env#
TimeoutStartSec infinity|s#^TimeoutStartSec=.*#TimeoutStartSec=infinity#
NoNewPrivileges removed|/^NoNewPrivileges=yes$/d
PrivateTmp removed|/^PrivateTmp=yes$/d
ProtectHome=no|s#^ProtectHome=yes#ProtectHome=no#
ProtectSystem loosened to full|s#^ProtectSystem=strict#ProtectSystem=full#
ProtectSystem set twice, the last wins|s#^ProtectSystem=strict#ProtectSystem=strict\nProtectSystem=no#
ReadWritePaths widened to /var|s#^ReadWritePaths=.*#ReadWritePaths=/var#
ReadWritePaths widened by the panel's own /var/lib/maran|s#^ReadWritePaths=.*#& /var/lib/maran#
ReadWritePaths widened to all of /etc|s#^ReadWritePaths=.*#& /etc#
ReadWritePaths widened to the system units under /etc|s#^ReadWritePaths=.*#& /etc/systemd/system#
ReadWritePaths widened to a tree inside /etc/maran|s#^ReadWritePaths=.*#& /etc/maran/tls#
an [Install] section|s#^Type=oneshot#Type=oneshot\n[Install]\nWantedBy=multi-user.target#
the start limit disabled|s#^StartLimitIntervalSec=.*#StartLimitIntervalSec=0#
SERVICE_CONTROLS

  while IFS='|' read -r name control; do
    [ -n "$name" ] || continue
    update_request_plant update_request_path_defect "$UPDATE_REQUEST_PATH_SOURCE" "$name" "$control"
    planted_count=$((planted_count + 1))
  done <<'PATH_CONTROLS'
a PathChanged on the directory|s#^MakeDirectory=no#MakeDirectory=no\nPathChanged=/run/maran-update-request#
DirectoryNotEmpty instead of PathExists|s#^PathExists=.*#DirectoryNotEmpty=/run/maran-update-request#
PathExists widened to the directory|s#^PathExists=.*#PathExists=/run/maran-update-request#
a second PathExists|s#^PathExists=.*#&\nPathExists=/etc/shadow#
Unit= pointing elsewhere|s#^Unit=.*#Unit=maran-update-security.service#
MakeDirectory=yes|s#^MakeDirectory=no#MakeDirectory=yes#
WantedBy changed|s#^WantedBy=multi-user.target#WantedBy=sockets.target#
PATH_CONTROLS

  while IFS='|' read -r name control; do
    [ -n "$name" ] || continue
    update_request_plant update_check_service_defect "$UPDATE_CHECK_SERVICE_SOURCE" "$name" "$control"
    planted_count=$((planted_count + 1))
  done <<'CHECK_SERVICE_CONTROLS'
an argument appended to ExecStart|s#^ExecStart=.*#& --yes#
a different verb in ExecStart|s#maran update --check#maran update --yes#
the update run in ExecStart|s#maran update --check#maran update --requested#
a shell wrapped around ExecStart|s#^ExecStart=.*#ExecStart=/bin/sh -c "/usr/local/sbin/maran update --check"#
a second ExecStart|s#^ExecStart=.*#&\nExecStart=/bin/true#
an ExecStartPre|s#^User=root#User=root\nExecStartPre=/bin/true#
an ExecStartPost|s#^User=root#User=root\nExecStartPost=/bin/true#
a second ExecStopPost|s#^ExecStopPost=.*#&\nExecStopPost=/bin/true#
an ExecStopPost that reads the marker|s#^ExecStopPost=.*#ExecStopPost=/usr/bin/cat /run/maran-update-request/check-requested#
rm without the end-of-options marker|s#rm -f --#rm -f#
an ExecStopPost that removes the update marker|/^ExecStopPost=/s#check-requested#requested#
User changed to the service account|s#^User=root#User=maran#
Type changed from oneshot|s#^Type=oneshot#Type=simple#
RemainAfterExit=yes|s#^RemainAfterExit=no#RemainAfterExit=yes#
an EnvironmentFile added|s#^UMask=0077#UMask=0077\nEnvironmentFile=/run/maran-update-request/env#
TimeoutStartSec infinity|s#^TimeoutStartSec=.*#TimeoutStartSec=infinity#
TimeoutStartSec longer than the panel believes a marker|s#^TimeoutStartSec=.*#TimeoutStartSec=10min#
NoNewPrivileges removed|/^NoNewPrivileges=yes$/d
PrivateTmp removed|/^PrivateTmp=yes$/d
ProtectHome=no|s#^ProtectHome=yes#ProtectHome=no#
ProtectSystem loosened to full|s#^ProtectSystem=strict#ProtectSystem=full#
ProtectSystem set twice, the last wins|s#^ProtectSystem=strict#ProtectSystem=strict\nProtectSystem=no#
ReadWritePaths widened to all of /run|s#^ReadWritePaths=.*#& /run#
ReadWritePaths widened by the panel's own /var/lib/maran|s#^ReadWritePaths=.*#& /var/lib/maran#
ReadWritePaths widened to the dump directory|s#^ReadWritePaths=.*#& /var/backups/maran-update#
ReadWritePaths widened to all of /etc|s#^ReadWritePaths=.*#& /etc#
ReadWritePaths widened to a tree inside /etc/maran|s#^ReadWritePaths=.*#& /etc/maran/tls#
the request directory dropped from ReadWritePaths|s# /run/maran-update-request$##
an [Install] section|s#^Type=oneshot#Type=oneshot\n[Install]\nWantedBy=multi-user.target#
the start limit disabled|s#^StartLimitIntervalSec=.*#StartLimitIntervalSec=0#
CHECK_SERVICE_CONTROLS

  while IFS='|' read -r name control; do
    [ -n "$name" ] || continue
    update_request_plant update_request_path_defect "$UPDATE_CHECK_PATH_SOURCE" "$name" "$control" "$UPDATE_CHECK_PATH_EXPECTED" maran-update-check.service
    planted_count=$((planted_count + 1))
  done <<'CHECK_PATH_CONTROLS'
a PathChanged on the directory|s#^MakeDirectory=no#MakeDirectory=no\nPathChanged=/run/maran-update-request#
DirectoryNotEmpty instead of PathExists|s#^PathExists=.*#DirectoryNotEmpty=/run/maran-update-request#
PathExists widened to the directory|s#^PathExists=.*#PathExists=/run/maran-update-request#
PathExists on the update marker|s#^PathExists=.*#PathExists=/run/maran-update-request/requested#
a second PathExists|s#^PathExists=.*#&\nPathExists=/etc/shadow#
Unit= pointing at the update service|s#^Unit=.*#Unit=maran-update-request.service#
MakeDirectory=yes|s#^MakeDirectory=no#MakeDirectory=yes#
WantedBy changed|s#^WantedBy=multi-user.target#WantedBy=sockets.target#
CHECK_PATH_CONTROLS

  while IFS='|' read -r name control; do
    [ -n "$name" ] || continue
    update_request_plant update_request_tmpfiles_defect "$UPDATE_REQUEST_TMPFILES_SOURCE" "$name" "$control" "$UPDATE_REQUEST_TMPFILES_EXPECTED"
    planted_count=$((planted_count + 1))
  done <<'TMPFILES_CONTROLS'
the mode opened to everyone|s#0770#0777#
the owner changed to the panel's group|s#0770 root#0770 __MARAN_GROUP__#
the directory moved under the panel's own tree|s#/run/maran-update-request#/var/lib/maran/update-request#
an age-based cleanup|s# -$# 10d#
a second line|$a d /run/evil 0777 root root -
TMPFILES_CONTROLS

  while IFS='|' read -r name control; do
    [ -n "$name" ] || continue
    update_request_plant update_request_api_unit_defect "$UPDATE_REQUEST_API_UNIT_SOURCE" "$name" "$control"
    planted_count=$((planted_count + 1))
  done <<'API_CONTROLS'
the request directory dropped from ReadWritePaths|/^ReadWritePaths=/s# /run/maran-update-request$##
the request directory dropped from NoExecPaths|/^NoExecPaths=/s# /run/maran-update-request##
ReadWritePaths opened to all of /run|s#^ReadWritePaths=.*#& /run#
API_CONTROLS

  [ "$planted_count" -eq 78 ] && [ "$accepted_count" -eq 6 ] \
    || fail "the update request checkers ran ${planted_count} planted refusals and ${accepted_count} accepting controls, not the 78 and 6 they carry. A control dropped from a list is a check that no longer exists."

  # The step renders the snippet for the panel's group, and a rendered snippet is the one line the
  # design fixes with the group in place of the placeholder. The step file is SOURCED in a child and
  # only its render function is called: nothing is installed.
  rendered="$(mktemp)"
  LIB_DIR="$INSTALLER_LIB" MARAN_GROUP=maran bash -c 'set -euo pipefail; . "$1"; render_update_request_tmpfiles "$2"' _ "$UPDATE_REQUEST_STEP" "$rendered" \
    || fail "step 69's render_update_request_tmpfiles failed in this image"
  group_line="d /run/maran-update-request 0770 root maran -"
  defect="$(update_request_tmpfiles_defect "$rendered" "$group_line")"
  [ -z "$defect" ] || fail "the tmpfiles snippet step 69 renders for the group 'maran' ${defect}"
  rm -f -- "$rendered"

  echo "maran-update-check.service is one fixed 'maran update --check' run as root with no ExecStartPre, one ExecStopPost that removes the check marker, a four-minute timeout, the narrower sandbox of the threat note and no [Install]; maran-update-check.path fires on exactly the check marker and starts the check service. maran-update-request.service is one fixed ExecStart run as root, one ExecStartPre that removes the marker, the sandbox of the security-patch unit, a finite timeout and no [Install]; maran-update-request.path fires on exactly the marker; the tmpfiles snippet builds a root-owned 0770 directory on /run; maran-api.service names that directory and nothing wider as its extra writable place. The checkers refused all ${planted_count} planted breaks and accepted the ${accepted_count} commented-out inverse controls, and step 69 renders the snippet for the panel's group."
  echo "UNOBSERVED HERE: this reads the files' TEXT. This image boots no systemd, so nothing started the path unit, created a marker or ran the service under its sandbox; that ReadWritePaths= leaves the verb what it needs (runuser, pg_dump, systemctl from inside the unit, SELinux on AlmaLinux) is the threat note's unverified item and belongs to a booted host."
}
