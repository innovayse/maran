#!/usr/bin/env bash
# Offline release notes for `maran release changelog` (issue #154).
#
# SOURCED by scripts/lib/release-bundle.sh, which supplies release_failed, release_ok, require_tool
# and VERDICT_PREFIX. Usage:
#   maran release changelog --to <ref> [--from <tag>] [--out <file>] [--version <v>] [--repo <dir>]
#
# WHAT IT WRITES. The release notes of ONE release: the pull requests merged after --from (exclusive)
# up to --to (inclusive), grouped by the Conventional Commit type prefix of their title. It reads
# only the local git history (no network, no token), so the same history always yields the same bytes.
# The hand-kept CHANGELOG.md is a different document and is never read or written here.
#
# WHERE THE PULL REQUESTS COME FROM. The repository merges pull requests with GitHub merge commits,
# so `git log --merges --first-parent` over the range lists them: the subject is
# `Merge pull request #N from <owner>/<branch>` and the body is the pull request title. Merge
# commits with any other subject (a branch merged by hand, a sync from dev) are not pull requests and
# are not listed.
#
# TWO LIMITS OF THE SOURCE. Pull requests are read from GitHub MERGE commits only: a pull request
# merged by squash or rebase leaves no `Merge pull request` commit and is NOT listed. The default
# --from is the newest v* tag of ANY channel reachable from --to, so a stable release cut after a
# beta lists only the changes since that beta; pass --from to span several tags.
#
# THE TITLE IS HOSTILE TEXT (docs/superpowers/notes/2026-11-04-release-pipeline-threat-note.md,
# item 3). It is read as data, cleaned by release_changelog_clean and escaped by
# release_changelog_escape, and only ever written to the notes file, never to a command line.
#
# PRE-RELEASES. rules/security.md allows a beta to reach main with second reviews outstanding only if
# the release notes say so in plain words. A version with a pre-release suffix therefore makes the
# notes open with RELEASE_CHANGELOG_BETA_NOTICE; a stable version never carries it.
#
# BLIND SPOTS. The prefix is the only classification: a title that lies about its type is listed
# under the type it claims. A bare URL in a title is not neutralised (Markdown renderers may turn it
# into a link; it cannot become an image, a tag or a link with hidden text). Unicode control and
# direction-override characters above U+007F are not stripped. Only the first 200 characters of a
# title are kept. Whether the pull requests were reviewed is not something git history records.

RELEASE_CHANGELOG_TITLE_MAX=200
RELEASE_CHANGELOG_BETA_NOTICE="Pre-release. The second security review of the privileged changes in this release is outstanding (rules/security.md)."

# release_changelog_clean: <text>. The text as one printable line: every ASCII control character
# (newline, tab, escape, DEL) becomes a space, runs of spaces collapse, the ends are trimmed, and
# anything past RELEASE_CHANGELOG_TITLE_MAX characters is replaced by "...". Cleaning comes BEFORE
# escaping so the cap can never cut an escape sequence in half. Does not neutralise Markdown.
release_changelog_clean() {
  local text
  text="$(printf '%s' "$1" | LC_ALL=C tr '[:cntrl:]' ' ' | LC_ALL=C tr -s ' ' | LC_ALL=C sed 's/^ //; s/ $//')"
  if [ "${#text}" -gt "$RELEASE_CHANGELOG_TITLE_MAX" ]; then
    text="${text:0:$RELEASE_CHANGELOG_TITLE_MAX}..."
  fi
  printf '%s' "$text"
}

# release_changelog_escape: <text>. Backslash-escapes the backslash, `<`, `>`, `[`, `]` and the
# backtick, which are the characters that open a link, an image, an HTML tag or a code span. The
# backslash goes first so the escapes added for the others are not themselves escaped again.
release_changelog_escape() {
  printf '%s' "$1" | LC_ALL=C sed -e 's/\\/\\\\/g' -e 's/[]<>[`]/\\&/g'
}

# release_changelog_section_of: <cleaned title>. The section key for the title's Conventional Commit
# prefix (`type:`, `type(scope):` or `type!:`, lower case, followed by a space). Anything else,
# including `Feat:` and `wip:`, is "other": no guessing beyond the prefix.
release_changelog_section_of() {
  local re='^([a-z]+)(\([^)]*\))?!?:[[:space:]]'
  if [[ "$1" =~ $re ]]; then
    case "${BASH_REMATCH[1]}" in
      feat) echo feat ;;
      fix) echo fix ;;
      docs) echo docs ;;
      test) echo test ;;
      refactor) echo refactor ;;
      chore|ci|build) echo ci ;;
      *) echo other ;;
    esac
  else
    echo other
  fi
}

# release_changelog_heading: <section key>. The heading printed for a section.
release_changelog_heading() {
  case "$1" in
    feat) echo "Features" ;;
    fix) echo "Fixes" ;;
    docs) echo "Documentation" ;;
    test) echo "Tests" ;;
    refactor) echo "Refactoring" ;;
    ci) echo "Build and CI" ;;
    *) echo "Other changes" ;;
  esac
}

# release_changelog_resolve: <repo> <ref>. Prints the commit id <ref> names, or returns 1. A ref that
# starts with "-" is refused before git sees it, so a ref can never be read as an option.
release_changelog_resolve() {
  case "$2" in "" | -*) return 1 ;; esac
  git -C "$1" rev-parse --verify --quiet "${2}^{commit}" 2>/dev/null
}

# release_changelog_previous_tag: <repo> <commit>. The newest v* tag reachable from the commit's
# FIRST PARENT, so a tag on the commit itself never counts as "the previous release". Prints nothing
# when there is none (a root commit, or no such tag).
release_changelog_previous_tag() {
  local parent
  parent="$(git -C "$1" rev-parse --verify --quiet "${2}^1" 2>/dev/null)" || return 0
  git -C "$1" describe --tags --abbrev=0 --match 'v*' "$parent" 2>/dev/null || true
}

# release_changelog_main: see the file header. Refuses (release_failed, no override) when --to or
# --from does not resolve, when --from is not an ancestor of --to, or when the range holds no pull
# request, and writes nothing in those cases. Without --out the notes go to stdout.
release_changelog_main() {
  local to="" from="" out="" version="" repo="$root"
  while [ $# -gt 0 ]; do
    case "$1" in
      --to) to="${2:?--to requires a value}"; shift 2 ;;
      --from) from="${2:?--from requires a value}"; shift 2 ;;
      --out) out="${2:?--out requires a value}"; shift 2 ;;
      --version) version="${2:?--version requires a value}"; shift 2 ;;
      --repo) repo="${2:?--repo requires a value}"; shift 2 ;;
      *) echo "release changelog: unknown argument: $1" >&2; usage ;;
    esac
  done
  require_tool git "reads the merge history"
  [ -n "$to" ] || release_failed "changelog needs --to <tag or commit>"

  local to_sha from_sha="" from_label=""
  to_sha="$(release_changelog_resolve "$repo" "$to")" \
    || release_failed "--to '$(release_changelog_clean "$to")' does not name a commit in ${repo}"
  if [ -n "$from" ]; then
    from_sha="$(release_changelog_resolve "$repo" "$from")" \
      || release_failed "--from '$(release_changelog_clean "$from")' does not name a commit in ${repo}"
    git -C "$repo" merge-base --is-ancestor "$from_sha" "$to_sha" \
      || release_failed "--from '$(release_changelog_clean "$from")' is not an ancestor of --to '$(release_changelog_clean "$to")'"
    from_label="$from"
  else
    from_label="$(release_changelog_previous_tag "$repo" "$to_sha")"
    [ -z "$from_label" ] || from_sha="$(git -C "$repo" rev-parse --verify "refs/tags/${from_label}^{commit}")"
  fi

  [ -n "$version" ] || version="${to#v}"
  case "$version" in
    "" | *[!A-Za-z0-9._+-]*) release_failed "the version '$(release_changelog_clean "$version")' may hold only letters, digits and . _ + - ; pass --version" ;;
  esac

  local range="$to_sha" log_file
  [ -z "$from_sha" ] || range="${from_sha}..${to_sha}"
  log_file="$(mktemp)"
  if ! git -C "$repo" -c log.showSignature=false log -z --merges --first-parent --no-show-signature \
      --format='%H%x1f%s%x1f%b' "$range" > "$log_file"; then
    rm -f -- "$log_file"
    release_failed "git could not read the history of ${range}"
  fi

  local -A bucket=()
  local record rest subject body title key count=0 pr_re='^Merge pull request #([0-9]+) from '
  while IFS= read -r -d '' record; do
    rest="${record#*$'\x1f'}"
    subject="${rest%%$'\x1f'*}"
    body="${rest#*$'\x1f'}"
    [[ "$subject" =~ $pr_re ]] || continue
    title="$(release_changelog_clean "$body")"
    [ -n "$title" ] || title="untitled pull request"
    key="$(release_changelog_section_of "$title")"
    bucket[$key]+="- $(release_changelog_escape "$title") (#${BASH_REMATCH[1]})"$'\n'
    count=$((count + 1))
  done < "$log_file"
  rm -f -- "$log_file"
  [ "$count" -gt 0 ] || release_failed "no merged pull request between '$(release_changelog_clean "${from_label:-the start of the repository}")' and '$(release_changelog_clean "$to")' (only 'Merge pull request #N from ...' commits count)"

  local date to_label range_line notes="" noun="pull requests"
  date="$(TZ=UTC git -C "$repo" show -s --format=%cd --date=format-local:%Y-%m-%d "$to_sha")"
  to_label="$(release_changelog_escape "$(release_changelog_clean "$to")")"
  if [ -n "$from_sha" ]; then
    range_line="Range: $(release_changelog_escape "$(release_changelog_clean "$from_label")") (exclusive) to ${to_label} (inclusive), commits ${from_sha:0:12}..${to_sha:0:12}."
  else
    range_line="Range: the start of the repository (no earlier v-prefixed tag is reachable from ${to_label}) to ${to_label} (inclusive), commits up to ${to_sha:0:12}."
  fi
  [ "$count" -ne 1 ] || noun="pull request"

  case "${version%%+*}" in
    *-*) notes+="${RELEASE_CHANGELOG_BETA_NOTICE}"$'\n\n' ;;
  esac
  notes+="# Maran ${version}"$'\n\n'"Released ${date} (UTC)."$'\n'"${range_line}"$'\n'"${count} ${noun}."$'\n'
  for key in feat fix docs test refactor ci other; do
    [ -z "${bucket[$key]:-}" ] || notes+=$'\n'"## $(release_changelog_heading "$key")"$'\n\n'"${bucket[$key]}"
  done

  if [ -n "$out" ]; then
    printf '%s' "$notes" > "$out" || release_failed "could not write ${out}"
    release_ok "wrote release notes for ${version}: ${count} ${noun} to ${out}"
  else
    printf '%s' "$notes"
  fi
}
