#!/usr/bin/env bash
# `maran units` — the systemd sandbox score of every unit this repository ships, held to a ceiling.
#
# `systemd-analyze security` grades a unit's hardening from 0.0 (nothing exposed) to 10.0. The
# directives that earn the grade are the ones that keep a compromised process in its box, and
# nothing else here notices when one is deleted: the unit still starts, the install still passes,
# and the root agent is quietly more exposed. This is the gate that notices. Each shipped unit has
# a ceiling in installer/systemd/unit-security-ceilings.txt; a score above it fails.
#
#   maran units             check (the default)
#   maran units --accept    re-measure and rewrite the ceilings file (never run in CI)
#   maran units selftest    plant a weakened unit and require the check to refuse it
#
# THE SCORED TEXT IS WHAT THE INSTALLER INSTALLS. The units that carry placeholders are rendered by
# sourcing installer/lib/70-services.sh and calling its own render_* functions, so this file holds
# no second copy of the substitutions. The two units that carry none (maran-ftps.service,
# maran-panel-backup.service) are installed as they stand and are scored as they stand. The one
# thing stubbed is quota_device_binds, which asks findmnt about THIS machine's /home: it answers "no
# device", which is the installer's own answer on a host where none resolves, so the score does not
# depend on the workstation's disk layout. Each BindPaths= line it would add can only be absent from
# the scored text, never different.
#
# The package-install template unit is rendered for BOTH OS families and the worse score is the one
# held (the key is maran-php-install@.service, the template's name). Today the two are equal.
#
# THREE ANSWERS, the vocabulary of `maran test` and `maran openapi` (scripts/lib/suite.sh):
#
#   UNITS VERDICT: OK            exit 0   every unit scored at or below its ceiling
#   UNITS VERDICT: FAILED        exit 1   a score above its ceiling, a unit with no ceiling, a ceiling
#                                         with no unit, a unit this file cannot render, an unreadable
#                                         analysis, or no unit scored at all
#   UNITS VERDICT: DID NOT RUN   exit 2   systemd-analyze is missing, its major version is not the one
#                                         the ceilings were measured with, or a unit would not render
#
# The refusal sits BEFORE the first stage and nothing above it may kill the script: a missing tool
# that dies inside a stage prints zero bytes and returns 127 (rules/testing.md, "Toolchain
# prerequisites").
#
# WHAT THIS CHECK CANNOT SEE is printed on every verdict, because a green line from a gate that
# reads a score is not a statement about what the process can do.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
ceilings_file="$root/installer/systemd/unit-security-ceilings.txt"
unit_source_dir="$root/installer/systemd"

# shellcheck disable=SC1091
. "$root/scripts/lib/suite.sh"

# The families the template unit is rendered for; every other unit is rendered once per family too,
# which costs nothing and means no unit is scored on a render only one family ever sees.
readonly UNITS_OS_FAMILIES="debian rhel"

# units_blind_spots: stated after every verdict, FAILED included.
units_blind_spots() {
  echo "UNITS: UNOBSERVED HERE — the score is systemd-version dependent: a newer systemd adds checks and moves every unit, which is why the ceilings record the version they were measured with and a different major is DID NOT RUN."
  echo "UNITS: UNOBSERVED HERE — runtime behaviour: the score grades the directives in the file, not what the process can reach. A sandbox that was never applied (a unit that fails to start, 226/NAMESPACE) scores exactly like one that was."
  echo "UNITS: UNOBSERVED HERE — drop-ins and the host's own unit directory: only the shipped file is scored, not what an operator layers over it."
  echo "UNITS: UNOBSERVED HERE — --offline=yes cannot resolve users, groups or paths, so a directive that names a user that does not exist scores the same as one that does."
}

units_verdict_failed() {
  echo "UNITS VERDICT: FAILED — $*"
  units_blind_spots
  exit 1
}

# units_systemd_major: the major version of the systemd-analyze on PATH, or nothing when it is absent.
units_systemd_major() {
  local first
  first="$(systemd-analyze --version 2>/dev/null | head -n 1)" || return 0
  printf '%s' "$first" | sed -n 's/^systemd \([0-9][0-9]*\).*/\1/p'
}

# units_normalise_name: a unit file's key. An instance of the template (maran-php-install@i.service)
# is the template's score, so it is keyed by the template's name.
units_normalise_name() {
  printf '%s' "$1" | sed 's/@[^.]*\.service$/@.service/'
}

# units_render_all <out_dir> <family>: every shipped .service, rendered the way the installer renders
# it, into <out_dir> under the name systemd would load it as. A unit this function does not know how
# to render is an error: the glob is the input, and a new unit nobody taught this to render must fail
# loudly rather than go unscored. Run in a subshell by the caller, because the installer's functions
# `exit` on a refusal.
units_render_all() {
  local out="$1" family="$2" file name
  LIB_DIR="$root/installer/lib"
  MARAN_USER="maran"
  MARAN_GROUP="maran"
  MARAN_API_SOCKET_PATH="/run/maran-api/api.sock"
  MARAN_OS_FAMILY="$family"
  # shellcheck disable=SC1091
  . "$root/installer/lib/70-services.sh"
  # The one stub: see the header. Defined after sourcing so it replaces the installer's.
  quota_device_binds() {
    echo "# No block device resolved for /home; disk quotas will not be enforceable here."
  }
  mkdir -p "$out"
  for file in "$unit_source_dir"/*.service; do
    [ -e "$file" ] || { echo "no .service file under installer/systemd/" >&2; return 1; }
    name="$(basename "$file")"
    case "$name" in
      maran-api.service)           render_api_unit "$out/$name" ;;
      maran-agent.service)         render_agent_unit "$out/$name" ;;
      maran-php-install@.service)  render_php_install_unit "$out/maran-php-install@i.service" ;;
      maran-ftps.service|maran-panel-backup.service) cp "$file" "$out/$name" ;;
      *) echo "installer/systemd/$name: scripts/lib/unit-security.sh does not know how the installer installs this unit" >&2; return 1 ;;
    esac
  done
}

# units_score_dir <dir>: "<key>\t<score>" for every .service in <dir>. Returns 1, naming the file on
# stderr, when systemd-analyze printed no level for one.
units_score_dir() {
  local dir="$1" file output score
  for file in "$dir"/*.service; do
    [ -e "$file" ] || continue
    output="$(systemd-analyze security --offline=yes --no-pager "$file" 2>&1 || true)"
    score="$(printf '%s\n' "$output" | sed -n 's/.*Overall exposure level for [^:]*: \([0-9][0-9]*\.[0-9]\) .*/\1/p' | head -n 1)"
    if [ -z "$score" ]; then
      echo "systemd-analyze printed no exposure level for $(basename "$file"):" >&2
      printf '%s\n' "$output" | tail -n 5 >&2
      return 1
    fi
    printf '%s\t%s\n' "$(units_normalise_name "$(basename "$file")")" "$score"
  done
}

# units_worst: reads "<key>\t<score>" rows on stdin, writes one row per key holding the highest score.
units_worst() {
  sort -k1,1 -k2,2nr | awk -F'\t' '$1 != seen { print; seen = $1 }'
}

# units_ceiling_of <ceilings_file> <key>: the recorded ceiling, or nothing.
units_ceiling_of() {
  awk -F'\t' -v key="$2" '$0 !~ /^#/ && $1 == key { print $2; exit }' "$1"
}

# units_evaluate <ceilings_file> <scores_file>: prints a line per finding and per note; returns 1
# when there is at least one finding. A scores file with no row is a finding (the vacuity guard),
# and so is a ceilings line no scored unit answers to, because a stale line is a unit that dropped
# out of the measurement.
units_evaluate() {
  local ceilings="$1" scores="$2" key score ceiling failed=0 scored=0
  while IFS=$'\t' read -r key score; do
    [ -n "$key" ] || continue
    scored=$((scored + 1))
    ceiling="$(units_ceiling_of "$ceilings" "$key")"
    if [ -z "$ceiling" ]; then
      echo "FINDING $key scored $score and has no ceiling in installer/systemd/unit-security-ceilings.txt."
      failed=1
    elif awk -v s="$score" -v c="$ceiling" 'BEGIN { exit !(s + 0 > c + 0) }'; then
      echo "FINDING $key scored $score, above its ceiling $ceiling: a hardening directive was removed or weakened."
      failed=1
    elif awk -v s="$score" -v c="$ceiling" 'BEGIN { exit !(s + 0 < c + 0) }'; then
      echo "NOTE    $key scored $score, below its ceiling $ceiling: lower the ceiling with 'maran units --accept' in its own commit."
    else
      echo "ok      $key scored $score (ceiling $ceiling)"
    fi
  done < "$scores"
  if [ "$scored" -eq 0 ]; then
    echo "FINDING no unit was scored, so there is nothing the ceilings were compared with."
    failed=1
  fi
  while IFS=$'\t' read -r key ceiling; do
    case "$key" in ''|'#'*) continue ;; esac
    if ! awk -F'\t' -v key="$key" '$1 == key { found = 1 } END { exit !found }' "$scores"; then
      echo "FINDING $key has a ceiling but no scored unit: the unit was removed, renamed or not rendered."
      failed=1
    fi
  done < "$ceilings"
  return "$failed"
}

# units_measure <work_dir> <scores_file>: render for every family, score, keep the worst per key.
# Returns 2 when a unit would not render (nothing was scored), 1 when an analysis was unreadable.
units_measure() {
  local work="$1" scores="$2" family rows
  rows="$work/rows.tsv"
  mkdir -p "$work"
  : > "$rows"
  for family in $UNITS_OS_FAMILIES; do
    if ! ( units_render_all "$work/$family" "$family" ) >"$work/render-$family.log" 2>&1; then
      cat "$work/render-$family.log" >&2
      return 2
    fi
    units_score_dir "$work/$family" >> "$rows" || return 1
  done
  units_worst < "$rows" > "$scores"
}

# units_write_ceilings <scores_file> <major> <out_file>: the ceilings file, from measured scores.
units_write_ceilings() {
  {
    echo "# measured with systemd $2"
    echo "# <unit><TAB><ceiling>: the highest 'systemd-analyze security --offline=yes' score the unit may have."
    echo "# Written by 'maran units --accept' from the units as the installer renders them; edit by re-measuring."
    echo "# maran-php-install@.service is the worse of its Debian-family and RHEL-family renders."
    echo "# A ceiling above 9 is a unit shipped as weakly sandboxed as it is today, not a target."
    sort -k1,1 "$1"
  } > "$3"
}

# units_recorded_major: the major the ceilings file says it was measured with, or nothing.
units_recorded_major() {
  sed -n 's/^# measured with systemd \([0-9][0-9]*\)[[:space:]]*$/\1/p' "$1" | head -n 1
}

# units_selftest <work_dir>: the planted controls. Returns 0 when every control behaves.
units_selftest() {
  local work="$1" scores ceil mutant_dir
  scores="$work/self-scores.tsv"
  ceil="$work/self-ceilings.txt"
  mutant_dir="$work/mutant"
  local base_score mutant_score major ok=0
  major="$(units_systemd_major)"

  echo "selftest: measuring the shipped units"
  units_measure "$work/real" "$scores" || { echo "selftest: the shipped units could not be measured."; return 1; }
  units_write_ceilings "$scores" "$major" "$ceil"

  # Case 1, the accepting control: the shipped units against ceilings recorded from themselves must
  # pass. A gate mutated to refuse everything passes every case that only hands it broken input.
  if units_evaluate "$ceil" "$scores" >"$work/case1.log"; then
    echo "selftest 1 OK: the shipped units are accepted against ceilings measured from them."
  else
    cat "$work/case1.log"; echo "selftest 1 FAILED: the accepting control was refused."; ok=1
  fi

  # Case 2, the planted weakening: the agent unit with ProtectKernelTunables= deleted. The score
  # must RISE, or the plant is not observable here and the case proves nothing.
  mkdir -p "$mutant_dir"
  sed '/^ProtectKernelTunables=/d' "$work/real/debian/maran-agent.service" > "$mutant_dir/maran-agent.service"
  if cmp -s "$work/real/debian/maran-agent.service" "$mutant_dir/maran-agent.service"; then
    echo "selftest 2 FAILED: the planted weakening changed nothing (ProtectKernelTunables= is gone from the agent unit?)."; ok=1
  else
    base_score="$(units_ceiling_of "$ceil" maran-agent.service)"
    mutant_score="$(units_score_dir "$mutant_dir" | cut -f2)"
    if [ -z "$mutant_score" ] || ! awk -v m="$mutant_score" -v b="$base_score" 'BEGIN { exit !(m + 0 > b + 0) }'; then
      echo "selftest 2 FAILED: deleting ProtectKernelTunables= scored ${mutant_score:-nothing} against ${base_score}, so this systemd cannot observe the plant."; ok=1
    else
      # Every other row stays as measured, so the only thing that can refuse is the agent's.
      { grep -v '^maran-agent.service' "$scores"; printf 'maran-agent.service\t%s\n' "$mutant_score"; } > "$work/mutant-scores.tsv"
      if units_evaluate "$ceil" "$work/mutant-scores.tsv" >"$work/case2.log"; then
        cat "$work/case2.log"; echo "selftest 2 FAILED: the weakened agent unit ($mutant_score > $base_score) was accepted."; ok=1
      elif ! grep -q '^FINDING maran-agent.service scored .*above its ceiling' "$work/case2.log"; then
        cat "$work/case2.log"; echo "selftest 2 FAILED: the check refused, but not for the agent unit's score."; ok=1
      else
        echo "selftest 2 OK: the agent unit without ProtectKernelTunables= scored $mutant_score, above its ceiling $base_score, and was refused."
      fi
    fi
  fi

  # Case 3, a unit with no ceiling is refused.
  grep -v "^maran-api.service" "$ceil" > "$work/no-api-ceilings.txt"
  if units_evaluate "$work/no-api-ceilings.txt" "$scores" >"$work/case3.log"; then
    echo "selftest 3 FAILED: a unit with no ceiling was accepted."; ok=1
  elif ! grep -q '^FINDING maran-api.service .*has no ceiling' "$work/case3.log"; then
    echo "selftest 3 FAILED: refused, but not for the missing ceiling."; ok=1
  else
    echo "selftest 3 OK: a unit with no ceiling was refused."
  fi

  # Case 4, the vacuity guard: nothing scored must not read as a pass.
  : > "$work/empty-scores.tsv"
  if units_evaluate "$ceil" "$work/empty-scores.tsv" >"$work/case4.log"; then
    echo "selftest 4 FAILED: a run that scored no unit was accepted."; ok=1
  elif ! grep -q '^FINDING no unit was scored' "$work/case4.log"; then
    echo "selftest 4 FAILED: refused, but not by the vacuity guard."; ok=1
  else
    echo "selftest 4 OK: a run that scored no unit was refused."
  fi

  # Case 5, the recorded major: a ceilings file measured with another major must not be compared.
  if [ "$(units_recorded_major "$ceil")" = "$major" ] && [ -n "$major" ]; then
    echo "selftest 5 OK: the recorded major ($major) is read back and equals this machine's."
  else
    echo "selftest 5 FAILED: the recorded major '$(units_recorded_major "$ceil")' is not this machine's '$major'."; ok=1
  fi
  return "$ok"
}

# --- dispatch ---------------------------------------------------------------------------------

verb="${1:-check}"
case "$verb" in
  check|--check|--accept|accept|selftest) ;;
  *)
    echo "usage: maran units [--check | --accept | selftest]" >&2
    exit 1
    ;;
esac

# THE GUARD, before the first stage.
if ! command -v systemd-analyze >/dev/null 2>&1; then
  suite_did_not_run "UNITS VERDICT" "systemd-analyze is not on PATH, so no unit was scored. This is the environment, not the units."
  exit "$SUITE_STATUS_DID_NOT_RUN"
fi
major="$(units_systemd_major)"
if [ -z "$major" ]; then
  suite_did_not_run "UNITS VERDICT" "systemd-analyze did not report a version, so the scores could not be tied to the one the ceilings were measured with."
  exit "$SUITE_STATUS_DID_NOT_RUN"
fi
if [ "$verb" = "check" ] || [ "$verb" = "--check" ]; then
  [ -f "$ceilings_file" ] || units_verdict_failed "installer/systemd/unit-security-ceilings.txt is missing — record it with 'maran units --accept'."
  recorded="$(units_recorded_major "$ceilings_file")"
  [ -n "$recorded" ] || units_verdict_failed "installer/systemd/unit-security-ceilings.txt has no '# measured with systemd <major>' line."
  if [ "$recorded" != "$major" ]; then
    suite_did_not_run "UNITS VERDICT" \
      "the ceilings were measured with systemd $recorded and this machine has $major; scores move between majors, so comparing them would be a statement about the version. This is the environment, not the units."
    exit "$SUITE_STATUS_DID_NOT_RUN"
  fi
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

if [ "$verb" = "selftest" ]; then
  if units_selftest "$work"; then
    echo "UNITS SELFTEST: OK"
    units_blind_spots
    exit 0
  fi
  units_verdict_failed "the unit-score check failed its own planted controls, so it cannot be trusted to judge the units."
fi

echo "rendering and scoring the shipped units (systemd $major)"
measure_status=0
units_measure "$work" "$work/scores.tsv" || measure_status=$?
if [ "$measure_status" -eq 2 ]; then
  suite_did_not_run "UNITS VERDICT" "a shipped unit would not render the way the installer renders it (above), so none was scored. This is the installer's input, not the score."
  exit "$SUITE_STATUS_DID_NOT_RUN"
fi
[ "$measure_status" -eq 0 ] || units_verdict_failed "systemd-analyze gave no readable score for a unit (above)."

case "$verb" in
  --accept|accept)
    [ -s "$work/scores.tsv" ] || units_verdict_failed "refusing to record ceilings from a run that scored no unit."
    units_write_ceilings "$work/scores.tsv" "$major" "$ceilings_file"
    cat "$ceilings_file"
    echo "UNITS VERDICT: OK — ceilings recorded into ${ceilings_file#"$root"/}; commit them on their own."
    units_blind_spots
    exit 0
    ;;
esac

findings_status=0
units_evaluate "$ceilings_file" "$work/scores.tsv" || findings_status=$?
if [ "$findings_status" -ne 0 ]; then
  echo "  If the weaker sandbox is intended, record it with 'maran units --accept' in its own commit."
  units_verdict_failed "a unit is more exposed than its recorded ceiling, or the measurement does not line up with the ceilings (findings above)."
fi
echo "UNITS VERDICT: OK"
units_blind_spots
