#!/usr/bin/env bash
# Selftest for `maran release changelog` (issue #154): `maran release changelog-selftest`.
#
# SOURCED by scripts/lib/release-bundle.sh after scripts/lib/release-changelog.sh. It builds a
# throwaway git repository (local-only git configuration, never the global one) holding GitHub-style
# merge commits for pull requests, including hostile titles, then runs the real
# release_changelog_main against it and compares the notes byte for byte with a hand-written
# expectation, so a generator that disagreed with itself could not pass.
#
# Every refusal is paired with the inverse control rules/testing.md asks for: the same command with
# the offending input made valid ACCEPTS. Two vacuity guards sit on the axis that can go blind: the
# planted history is shown to really contain the escape character, the angle-bracket tag and the
# bracket link the neutralisation checks hunt for, and the unescaped-character grep is shown to find
# one in the raw history. Without them "nothing hostile in the output" would hold of an empty repo.
#
# UNOBSERVED HERE: the real repository history and the GitHub merge-commit wording are not read.

# release_changelog_selftest_pr: <repo> <number> <title> <ISO date>. Merges a one-commit branch into
# main with the subject and body GitHub writes: `Merge pull request #N from innovayse/pr-N`, then
# the title as the body. The title is passed as one argument, so it may hold newlines.
release_changelog_selftest_pr() {
  local repo="$1" number="$2" title="$3" when="$4"
  git -C "$repo" checkout -q -b "pr-${number}" main
  GIT_AUTHOR_DATE="$when" GIT_COMMITTER_DATE="$when" git -C "$repo" commit -q --allow-empty -m "work for ${number}"
  git -C "$repo" checkout -q main
  GIT_AUTHOR_DATE="$when" GIT_COMMITTER_DATE="$when" git -C "$repo" merge -q --no-ff \
    -m "Merge pull request #${number} from innovayse/pr-${number}" -m "$title" "pr-${number}"
}

# release_changelog_selftest_plant: <repo>. The history every case reads: a root commit, PR 1, tag
# v1.0.0-beta.1 (annotated), PRs 2 to 14 with a non-PR merge among them, tag v1.0.0-beta.2, PR 15,
# tag v1.0.0, one plain commit tagged v1.0.1 (no pull request after v1.0.0), and a branch off the
# root tagged side-tag that is NOT an ancestor of anything released. PR 14 is committed at 23:30 at
# UTC-5, which is the next day in UTC.
release_changelog_selftest_plant() {
  local repo="$1" long esc root_sha
  long="feat: $(printf 'x%.0s' $(seq 1 494))"
  esc="docs: esc"$'\x1b'"[31mred"$'\t'"tab   spaced"
  git -C "$repo" -c init.defaultBranch=main init -q
  git -C "$repo" config user.name "Selftest"
  git -C "$repo" config user.email "selftest@example.invalid"
  git -C "$repo" config commit.gpgsign false
  git -C "$repo" config tag.gpgsign false
  GIT_AUTHOR_DATE="2026-01-01T12:00:00+0000" GIT_COMMITTER_DATE="2026-01-01T12:00:00+0000" \
    git -C "$repo" commit -q --allow-empty -m "root"
  root_sha="$(git -C "$repo" rev-parse HEAD)"
  release_changelog_selftest_pr "$repo" 1 "feat: old feature" "2026-02-01T12:00:00+0000"
  git -C "$repo" tag -a -m "beta 1" v1.0.0-beta.1
  release_changelog_selftest_pr "$repo" 2 "feat: add widget" "2026-02-10T12:00:00+0000"
  release_changelog_selftest_pr "$repo" 3 "fix: repair thing" "2026-02-11T12:00:00+0000"
  release_changelog_selftest_pr "$repo" 4 "docs: explain widget" "2026-02-12T12:00:00+0000"
  release_changelog_selftest_pr "$repo" 5 "test: cover widget" "2026-02-13T12:00:00+0000"
  release_changelog_selftest_pr "$repo" 6 "refactor: split widget" "2026-02-14T12:00:00+0000"
  release_changelog_selftest_pr "$repo" 7 "ci: pin runner" "2026-02-15T12:00:00+0000"
  release_changelog_selftest_pr "$repo" 8 "chore: tidy lockfile" "2026-02-16T12:00:00+0000"
  git -C "$repo" checkout -q -b side-feature main
  GIT_AUTHOR_DATE="2026-02-17T12:00:00+0000" GIT_COMMITTER_DATE="2026-02-17T12:00:00+0000" \
    git -C "$repo" commit -q --allow-empty -m "side work"
  git -C "$repo" checkout -q main
  GIT_AUTHOR_DATE="2026-02-17T12:00:00+0000" GIT_COMMITTER_DATE="2026-02-17T12:00:00+0000" \
    git -C "$repo" merge -q --no-ff -m "Merge branch 'side-feature' into main" side-feature
  release_changelog_selftest_pr "$repo" 9 "no prefix at all" "2026-02-18T12:00:00+0000"
  release_changelog_selftest_pr "$repo" 10 "Feat: capital letter" "2026-02-19T12:00:00+0000"
  release_changelog_selftest_pr "$repo" 11 $'fix: first line\nsecond line `rm -rf`' "2026-02-20T12:00:00+0000"
  release_changelog_selftest_pr "$repo" 12 'feat: [x](http://evil) <script>alert(1)</script>' "2026-02-21T12:00:00+0000"
  release_changelog_selftest_pr "$repo" 13 "$long" "2026-02-22T12:00:00+0000"
  release_changelog_selftest_pr "$repo" 14 "$esc" "2026-03-15T23:30:00-0500"
  git -C "$repo" tag v1.0.0-beta.2
  release_changelog_selftest_pr "$repo" 15 "fix: stable bit" "2026-04-01T10:00:00+0000"
  git -C "$repo" tag v1.0.0
  GIT_AUTHOR_DATE="2026-04-02T10:00:00+0000" GIT_COMMITTER_DATE="2026-04-02T10:00:00+0000" \
    git -C "$repo" commit -q --allow-empty -m "plain commit, no pull request"
  git -C "$repo" tag v1.0.1
  git -C "$repo" checkout -q -b orphan-side "$root_sha"
  GIT_AUTHOR_DATE="2026-01-02T12:00:00+0000" GIT_COMMITTER_DATE="2026-01-02T12:00:00+0000" \
    git -C "$repo" commit -q --allow-empty -m "unreleased side commit"
  git -C "$repo" tag side-tag
  git -C "$repo" checkout -q main
}

# release_changelog_selftest_expected_beta: <repo> <long title line>. The exact notes for
# `--to v1.0.0-beta.2`, written by hand: the notice, the date of PR 14 in UTC, the range, 13 pull
# requests (the non-PR merge is absent), newest first inside each section, hostile text escaped.
release_changelog_selftest_expected_beta() {
  local repo="$1" long="$2" text
  text="$(cat <<'EOF'
Pre-release. The second security review of the privileged changes in this release is outstanding (rules/security.md).

# Maran 1.0.0-beta.2

Released 2026-03-16 (UTC).
Range: v1.0.0-beta.1 (exclusive) to v1.0.0-beta.2 (inclusive), commits @FROM@..@TO@.
13 pull requests.

## Features

- @LONG@ (#13)
- feat: \[x\](http://evil) \<script\>alert(1)\</script\> (#12)
- feat: add widget (#2)

## Fixes

- fix: first line second line \`rm -rf\` (#11)
- fix: repair thing (#3)

## Documentation

- docs: esc \[31mred tab spaced (#14)
- docs: explain widget (#4)

## Tests

- test: cover widget (#5)

## Refactoring

- refactor: split widget (#6)

## Build and CI

- chore: tidy lockfile (#8)
- ci: pin runner (#7)

## Other changes

- Feat: capital letter (#10)
- no prefix at all (#9)
EOF
)"
  text="${text//@FROM@/$(git -C "$repo" rev-parse --short=12 v1.0.0-beta.1^{commit})}"
  text="${text//@TO@/$(git -C "$repo" rev-parse --short=12 v1.0.0-beta.2^{commit})}"
  printf '%s\n' "${text//@LONG@/$long}"
}

# release_changelog_selftest_run: <repo> <args...>. Runs release_changelog_main in a subshell
# (release_failed exits) against the throwaway repo; prints stdout and stderr together and returns
# the subshell's status.
release_changelog_selftest_run() {
  local repo="$1"
  shift
  ( release_changelog_main --repo "$repo" "$@" ) 2>&1
}

# release_changelog_selftest_refuse: <label> <pattern> <repo> <args...>. The refusal must happen
# (non-zero status), for the stated reason (a refusal for another reason, such as a typo in the
# fixture, would pass for the one under test), and no --out file may be left behind.
release_changelog_selftest_refuse() {
  local label="$1" pattern="$2" repo="$3" output leftover
  shift 3
  leftover="${repo}.refused-notes"
  rm -f -- "$leftover"
  if output="$(release_changelog_selftest_run "$repo" "$@" --out "$leftover")"; then
    release_failed "changelog selftest: ${label} was ACCEPTED — the check behind it is decoration."
  fi
  printf '%s' "$output" | grep -qE -- "$pattern" \
    || release_failed "changelog selftest: ${label} was refused for another reason than expected (wanted /${pattern}/): ${output}"
  [ ! -e "$leftover" ] || release_failed "changelog selftest: ${label} was refused but still wrote notes."
  echo "changelog selftest ok — ${label} REFUSED, nothing written (expected)."
}

# release_changelog_selftest_accept: <label> <repo> <args...>. The inverse control: the same kind of
# command with the offending input made valid must succeed and write a non-empty file.
release_changelog_selftest_accept() {
  local label="$1" repo="$2" out="${2}.accepted-notes" output
  shift 2
  rm -f -- "$out"
  output="$(release_changelog_selftest_run "$repo" "$@" --out "$out")" \
    || release_failed "changelog selftest: ${label} was REFUSED but must be accepted: ${output}"
  [ -s "$out" ] || release_failed "changelog selftest: ${label} was accepted but wrote no notes."
  echo "changelog selftest ok — ${label} ACCEPTED (inverse control, expected)."
}

# release_changelog_selftest: see the file header.
release_changelog_selftest() {
  require_tool git "builds the throwaway repository"
  require_tool cmp "compares two runs byte for byte"
  local work repo long expected unescaped
  work="$(mktemp -d)"
  trap 'rm -rf -- "${work:-}"' RETURN
  repo="${work}/repo"
  install -d "$repo"
  export GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_NOSYSTEM=1
  release_changelog_selftest_plant "$repo"
  long="feat: $(printf 'x%.0s' $(seq 1 194))..."
  unescaped='(^|[^\\])[][<>`]'

  # Vacuity guards: the hostile text really is in the history.
  local raw
  raw="$(git -C "$repo" log --merges --format=%B)"
  printf '%s' "$raw" | grep -qF '<script>' || release_failed "changelog selftest: the planted history lacks the <script> title."
  printf '%s' "$raw" | grep -qF $'\x1b' || release_failed "changelog selftest: the planted history lacks the escape character."
  printf '%s' "$raw" | grep -qE -- "$unescaped" || release_failed "changelog selftest: the unescaped-character search finds nothing in the raw history, so it could not catch a leak."

  printf '%s' "$raw" | grep -qE '^.{260,}$' || release_failed "changelog selftest: the raw history holds no line of 260 or more columns, so the length check could not catch an uncapped title."

  # 1. Beta notes: exact bytes, stable on a second run, same on stdout and via --out.
  release_changelog_selftest_expected_beta "$repo" "$long" > "${work}/expected-beta.md"
  release_changelog_selftest_run "$repo" --to v1.0.0-beta.2 > "${work}/beta-1.md" \
    || release_failed "changelog selftest: the beta notes were refused: $(cat "${work}/beta-1.md")"
  cmp -s "${work}/expected-beta.md" "${work}/beta-1.md" \
    || release_failed "changelog selftest: beta notes differ from the expectation: $(diff "${work}/expected-beta.md" "${work}/beta-1.md" | head -20)"
  release_changelog_selftest_run "$repo" --to v1.0.0-beta.2 --out "${work}/beta-2.md" >/dev/null
  cmp -s "${work}/beta-1.md" "${work}/beta-2.md" || release_failed "changelog selftest: a second run (written with --out) produced other bytes."
  echo "changelog selftest ok — beta notes match the expected text byte for byte, a second run is identical, the date is the commit's UTC date (2026-03-16, not the clock), and the range stops at the previous tag (expected)."

  # 2. Neutralisation, checked on the output independently of the expected text.
  if grep -qE -- "$unescaped" "${work}/beta-1.md"; then
    release_failed "changelog selftest: the notes hold an unescaped < > [ ] or backtick: $(grep -nE -- "$unescaped" "${work}/beta-1.md" | head -3)"
  fi
  ! grep -qF $'\x1b' "${work}/beta-1.md" || release_failed "changelog selftest: an escape character reached the notes."
  [ "$(grep -c '^- ' "${work}/beta-1.md")" -eq 13 ] || release_failed "changelog selftest: a newline in a title split an entry, or an entry is missing."
  ! grep -qE '^.{260,}$' "${work}/beta-1.md" || release_failed "changelog selftest: a 500-character title was not capped."
  echo "changelog selftest ok — no unescaped < > [ ] or backtick, no control character, 13 entries for 13 pull requests, the 500-character title is capped (expected)."

  # 3. Stable notes: no notice, only the PRs after the previous tag.
  release_changelog_selftest_run "$repo" --to v1.0.0 > "${work}/stable.md" || release_failed "changelog selftest: the stable notes were refused."
  [ "$(head -1 "${work}/stable.md")" = "# Maran 1.0.0" ] || release_failed "changelog selftest: stable notes do not open with their title: $(head -1 "${work}/stable.md")"
  ! grep -qF 'Pre-release' "${work}/stable.md" || release_failed "changelog selftest: the stable notes carry the pre-release notice."
  grep -qF -- '- fix: stable bit (#15)' "${work}/stable.md" || release_failed "changelog selftest: the stable notes lack PR 15."
  ! grep -qE 'add widget|old feature' "${work}/stable.md" || release_failed "changelog selftest: the stable notes list pull requests of an earlier release."
  [ "$(head -1 "${work}/beta-1.md")" = "$RELEASE_CHANGELOG_BETA_NOTICE" ] || release_failed "changelog selftest: the beta notes do not open with the notice."
  echo "changelog selftest ok — the beta notes open with the notice, the stable notes do not carry it and list only PR 15 (expected)."

  # 4. No earlier tag: the root is the start, and the output says so.
  release_changelog_selftest_run "$repo" --to v1.0.0-beta.1 > "${work}/first.md" || release_failed "changelog selftest: the first release was refused."
  grep -qF 'Range: the start of the repository' "${work}/first.md" || release_failed "changelog selftest: the root range is not stated."
  grep -qF -- '- feat: old feature (#1)' "${work}/first.md" || release_failed "changelog selftest: the first release lacks PR 1."
  ! grep -qF 'the start of the repository' "${work}/stable.md" || release_failed "changelog selftest: a range with a previous tag claims to start at the root."
  echo "changelog selftest ok — with no earlier tag the notes say the range starts at the repository root (expected)."

  # 5. Refusals, each beside its accepting control.
  release_changelog_selftest_refuse "an unknown --to" "does not name a commit" "$repo" --to v9.9.9
  release_changelog_selftest_refuse "an option-shaped --to" "does not name a commit" "$repo" --to --all
  release_changelog_selftest_accept "a known --to" "$repo" --to v1.0.0
  release_changelog_selftest_refuse "a --from that is not an ancestor of --to" "is not an ancestor" "$repo" --to v1.0.0 --from side-tag
  release_changelog_selftest_accept "a --from that is an ancestor of --to" "$repo" --to v1.0.0 --from v1.0.0-beta.1
  release_changelog_selftest_refuse "a range with no pull request" "no merged pull request" "$repo" --to v1.0.1
  release_changelog_selftest_accept "a range with pull requests" "$repo" --to v1.0.0


  echo "UNOBSERVED HERE: the real repository history and the GitHub merge-commit wording are not read"
  release_ok "release changelog selftest passed: exact grouped output, byte-stable, UTC commit date, hostile titles neutralised, range excludes the previous release, pre-release notice only on a pre-release, 4 refusals and 3 accepting controls."
}
