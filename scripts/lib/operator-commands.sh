#!/usr/bin/env bash
# operator-commands.sh: `maran operator-commands [check|selftest]` - proves the operator's command
# catalogue (installer/cli/commands.json, printed by `sudo maran help --json`) against the wrapper's
# dispatcher, the flag parsers of the CLI libraries and the installer scripts, and the commands the
# operations docs quote (issue #155). The reader is operator-commands.py; this file owns the verdict
# line, the toolchain refusal and the selftest.
#
# Answers, as in scripts/lib/suite.sh: 0 and `OPERATOR-COMMANDS VERDICT: OK`, 1 and `FAILED` (the
# tree disagrees with its catalogue), 2 and `DID NOT RUN` (nothing was measured).
#
# UNOBSERVED HERE: flags a library accepts through a variable or a computed case label; helper files
# a verb's flags are parsed in (only the file defining the dispatched function is read); commands
# quoted outside docs/operations (the website's content check reads commands.json for those); runtime
# behaviour on a host (the polygon asserts that `maran help --json` prints the shipped file).
set -euo pipefail

readonly OPCMD_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly OPCMD_ROOT="$(cd "${OPCMD_DIR}/../.." && pwd)"
# Overridable so the selftest can prove the refusal without taking python3 off the machine's PATH.
readonly OPCMD_PYTHON="${OPERATOR_COMMANDS_PYTHON:-python3}"

# opcmd_check: runs the reader against one tree. $1 = repository root. Prints its output; status 0|1|2.
opcmd_check() {
  local tree="$1" status=0
  command -v "$OPCMD_PYTHON" >/dev/null 2>&1 || {
    echo "OPERATOR-COMMANDS VERDICT: DID NOT RUN"
    echo "REFUSED: ${OPCMD_PYTHON} is not on PATH, so nothing was compared."
    return 2
  }
  "$OPCMD_PYTHON" -I "${OPCMD_DIR}/operator-commands.py" "$tree" || status=$?
  case "$status" in
    0) echo "OPERATOR-COMMANDS VERDICT: OK" ;;
    1) echo "OPERATOR-COMMANDS VERDICT: FAILED" ;;
    *) echo "OPERATOR-COMMANDS VERDICT: DID NOT RUN"; status=2 ;;
  esac
  return "$status"
}

# opcmd_copy_tree: copies the files the reader reads into a scratch directory. $1 = destination.
opcmd_copy_tree() {
  local dest="$1"
  mkdir -p "${dest}/installer" "${dest}/scripts" "${dest}/docs"
  cp -R "${OPCMD_ROOT}/installer/cli" "${dest}/installer/cli"
  cp "${OPCMD_ROOT}/installer/install.sh" "${OPCMD_ROOT}/installer/uninstall.sh" "${OPCMD_ROOT}/installer/get.sh" \
    "${dest}/installer/"
  cp "${OPCMD_ROOT}/scripts/maran" "${dest}/scripts/maran"
  cp -R "${OPCMD_ROOT}/docs/operations" "${dest}/docs/operations"
}

# opcmd_case: one selftest case. $1 = name, $2 = expected status, $3 = text the output must contain
# ("-" for none), $4 = the function that mutates a fresh copy of the tree (its only argument is the
# copy's directory). A mutation that changed nothing fails the case: it would measure the unchanged tree.
opcmd_case() {
  local name="$1" want="$2" needle="$3" mutate="$4" dir out status=0
  dir="$(mktemp -d)"
  opcmd_copy_tree "$dir"
  local before after
  before="$(cd "$dir" && find . -type f -exec cksum {} + | sort)"
  "$mutate" "$dir"
  after="$(cd "$dir" && find . -type f -exec cksum {} + | sort)"
  if [ "$mutate" != opcmd_mutate_none ] && [ "$before" = "$after" ]; then
    echo "SELFTEST FAILED: case '${name}': the mutation changed nothing, so it measured the unchanged tree."
    rm -rf -- "$dir"
    return 1
  fi
  out="$(opcmd_check "$dir" 2>&1)" || status=$?
  rm -rf -- "$dir"
  if [ "$status" -ne "$want" ]; then
    echo "SELFTEST FAILED: case '${name}': expected status ${want}, got ${status}."
    printf '%s\n' "$out" | sed 's/^/    /'
    return 1
  fi
  if [ "$needle" != "-" ] && ! grep -qF -- "$needle" <<<"$out"; then
    echo "SELFTEST FAILED: case '${name}': the output does not name '${needle}'."
    printf '%s\n' "$out" | sed 's/^/    /'
    return 1
  fi
  echo "ok: ${name}"
}

opcmd_mutate_none() { :; }
opcmd_mutate_drop_verb() { sed -i '/^    status) cli_status/d' "$1/installer/cli/maran"; }
opcmd_mutate_add_flag() {
  sed -i 's/^      --clear-two-factor) clear_two_factor=1 ;;/&\n      --frobnicate) clear_two_factor=1 ;;/' "$1/installer/cli/lib/reset.sh"
}
opcmd_mutate_catalogue_flag() { sed -i 's/"--no-logs", "--out"/"--no-logs", "--out", "--ghost"/' "$1/installer/cli/commands.json"; }
opcmd_mutate_catalogue_verb() { sed -i 's/"verb": "rollback"/"verb": "rollbackk"/' "$1/installer/cli/commands.json"; }
opcmd_mutate_installer_flag() {
  sed -i 's/^    --os-security-updates)/    --frobnicate-installer)\n      shift ;;\n&/' "$1/installer/install.sh"
}
opcmd_mutate_doc_verb() { printf '\nRun `sudo maran frobnicate` now.\n' >> "$1/docs/operations/panel-update.md"; }
opcmd_mutate_doc_flag() { printf '\nRun `sudo maran update --bogus` now.\n' >> "$1/docs/operations/panel-update.md"; }
opcmd_mutate_doc_installer_flag() { printf '\n```\nsudo ./install.sh --bogus-install\n```\n' >> "$1/docs/operations/panel-update.md"; }
opcmd_mutate_doc_toolbox() { printf '\nOn a workstation run `maran test backend`.\n' >> "$1/docs/operations/panel-update.md"; }
opcmd_mutate_doc_toolbox_as_root() { printf '\nOn a server run `sudo maran test backend`.\n' >> "$1/docs/operations/panel-update.md"; }
opcmd_mutate_blind_docs() { rm -rf -- "$1/docs/operations" && mkdir -p "$1/docs/operations"; }

# opcmd_selftest: proves the check against planted breakage, and its inverse controls against
# things it must accept. Every refusing case has a twin that must not refuse.
opcmd_selftest() {
  local failed=0
  opcmd_case "an unchanged tree is accepted (the inverse control)" 0 "VERDICT: OK" opcmd_mutate_none || failed=1
  opcmd_case "a verb removed from the dispatcher is named" 1 "'status'" opcmd_mutate_drop_verb || failed=1
  opcmd_case "a flag the code parses and the catalogue lacks is named" 1 "--frobnicate" opcmd_mutate_add_flag || failed=1
  opcmd_case "a catalogued flag the code never parses is named" 1 "--ghost" opcmd_mutate_catalogue_flag || failed=1
  opcmd_case "a catalogued verb the dispatcher does not route is named" 1 "rollbackk" opcmd_mutate_catalogue_verb || failed=1
  opcmd_case "an installer flag missing from the catalogue is named" 1 "--frobnicate-installer" opcmd_mutate_installer_flag || failed=1
  opcmd_case "a doc quoting a verb that does not exist is named" 1 "frobnicate" opcmd_mutate_doc_verb || failed=1
  opcmd_case "a doc quoting a flag that does not exist is named" 1 "--bogus" opcmd_mutate_doc_flag || failed=1
  opcmd_case "a doc quoting an installer flag that does not exist is named" 1 "--bogus-install" opcmd_mutate_doc_installer_flag || failed=1
  opcmd_case "a doc quoting a developer-toolbox command is ignored" 0 "VERDICT: OK" opcmd_mutate_doc_toolbox || failed=1
  opcmd_case "a toolbox command written as the root command is refused" 1 "maran test" opcmd_mutate_doc_toolbox_as_root || failed=1
  opcmd_case "a docs folder that quotes nothing is refused as blind" 1 "went blind" opcmd_mutate_blind_docs || failed=1

  local out status=0
  out="$(OPERATOR_COMMANDS_PYTHON=no-such-python-here bash "${BASH_SOURCE[0]}" check 2>&1)" || status=$?
  if [ "$status" -eq 2 ] && grep -qF "VERDICT: DID NOT RUN" <<<"$out"; then
    echo "ok: a machine without the reader's interpreter answers DID NOT RUN, status 2"
  else
    echo "SELFTEST FAILED: without the interpreter the check answered status ${status}: ${out}"
    failed=1
  fi

  echo "UNOBSERVED HERE: the selftest plants breakage in a copy of the tree and never touches the real one."
  if [ "$failed" -eq 0 ]; then
    echo "OPERATOR-COMMANDS VERDICT: OK"
    return 0
  fi
  echo "OPERATOR-COMMANDS VERDICT: FAILED"
  return 1
}

main() {
  case "${1:-check}" in
    check) opcmd_check "$OPCMD_ROOT" ;;
    selftest) opcmd_selftest ;;
    *) echo "usage: maran operator-commands [check|selftest]" >&2; return 64 ;;
  esac
}

main "$@"
