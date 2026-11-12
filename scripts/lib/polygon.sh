#!/usr/bin/env bash
# A LIBRARY, not a command: `maran mutate --polygon` sources this file.
#
# It exists because of one measurement. Moving `setuid` before `setgroups` in the agent's privilege
# drop — the classic ordering defect, the one the whole `fork_as_account` design is built around —
# SURVIVES the entire local Rust workspace (1396 tests, 0 failures) and is killed only inside a
# polygon container, where the `#[ignore]`d host suites run as root on a real system. A harness that
# always scores against the local workspace would have answered SURVIVED for that mutant, and
# SURVIVED is the verdict an author quotes when deleting a check. That is the confidence-manufacturing
# direction rules/testing.md is written against, so the polygon is not an optional extra lane here:
# for anything in `privs/`, it is the only lane that can see the defect at all.
#
# What this library provides, and the trap each part exists for:
#
#   polygon_suites          discovers every polygon suite by what it CONTAINS (`#[ignore]`), never
#                           by a hard-coded list. A hard-coded list is how docker/README.md ran six
#                           suites out of ten for a whole plan while everyone believed otherwise.
#   polygon_lane_*          POLYGON_LANE_OWNED_SUITES: a suite another lane runs and proves (the mail
#                           suite, in the `polygon-mail` lane), declared once, read by the verifier
#                           and by that lane's own verdict.
#   polygon_fingerprint     the content hash of everything an image is built FROM.
#   polygon_ensure_image    refuses to run against a STALE image; builds one whose tag IS the
#                           fingerprint of its sources.
#   polygon_build_labelled  the only build in this repository: records the fingerprint IN the image.
#   polygon_require_current refuses an absent or stale floating image, naming the rebuild command.
#   polygon_stamp           run time: refuses a stale image, and writes which image is being used
#                           into the run's own log.
#   polygon_currency_from_log scoring time: refuses to score a log whose recorded image was built
#                           from sources this tree no longer has.
#   polygon_run             runs every discovered suite, as root, in one container per family, with
#                           `--no-fail-fast`, and reports a killed or empty container as ABORTED
#                           rather than scoring it.
#   polygon_reconcile_counts the "ran, but ran less" guard: every suite must execute exactly as many
#                           tests as the committed baseline records for it.
#   polygon_verify_ran      the "exit 0 for zero tests" guard: every discovered suite must have
#                           produced its own `test result:` line, the run must have executed at
#                           least one test, and the counts must reconcile with the baseline.
#
# Nothing here reads an exit code as a verdict, and nothing here can narrow the suite list: there is
# no scope parameter, exactly as in suite.sh, because the score a filtered run produces is SURVIVED.

# polygon_note: an honest line about what this library cannot see, printed by the commands that
# depend on the observation rather than buried in a comment.
polygon_note() {
  echo "UNOBSERVED HERE: $*"
}

# polygon_require_docker: proves there is a container engine before anything claims to have run.
#
# The same guard suite_require_toolchain is: a missing `docker` produces a run that measures nothing,
# and a reader taking the exit code would read it as a pass.
polygon_require_docker() {
  if ! command -v docker >/dev/null 2>&1; then
    echo "REFUSED: docker is not on PATH — a polygon run would measure nothing." >&2
    return 1
  fi
  if ! docker info >/dev/null 2>&1; then
    echo "REFUSED: the docker daemon does not answer — a polygon run would measure nothing." >&2
    return 1
  fi
  return 0
}

# polygon_suites: names every polygon suite in a tree, one per line.
#
# A polygon suite is identified by what it contains — an `#[ignore]` attribute in a crate-level
# `tests/*.rs` file — and not by its file name. `maran structure` identifies them the same way and
# for the same reason: the naming convention is not the subject, the suite is. A file called
# `quota_polygon.rs` holding ignored host tests is a polygon suite; `handshake.rs` and
# `golden_test.rs` carry no `#[ignore]` and are not.
#
# This is what makes "the scope is never filtered" true for polygon mode: the list is derived from
# the tree on every run, so a suite added today is scored today, and a suite that disappears is
# visible as a missing result line rather than as a smaller, plausible total.
polygon_suites() {
  local root="$1"
  local suite_file
  while IFS= read -r suite_file; do
    [ -e "$suite_file" ] || continue
    grep -q '#\[ignore' "$suite_file" || continue
    basename "$suite_file" .rs
  done < <(find "$root"/agent/crates/*/tests -maxdepth 1 -name '*.rs' 2>/dev/null | sort)
}

# POLYGON_FAMILY_SKIPS: the ONE declaration of which polygon suites a family does not run, and why.
#
# Each row is `image-family|suite|installer step|sentence|OS family`. A suite listed here has nothing
# to run against on that image (FTPS on the SUSE family: vsftpd is not packaged), so the run does not
# pass `--test` for it and `maran polygon verify` expects it ABSENT for that family and for no other.
# Only the image family named in the row is exempt: ubuntu24 and alma9 have no row, so the same suite
# is still required there (polygon_selftest_skips proves it). The installer step and sentence are the
# agreement with the adapter: the row is valid only while that step prints that sentence for that OS
# family, checked by polygon_skips_agree_with_installer.
# Image family `leap16` is installer family `suse`.
POLYGON_FAMILY_SKIPS=(
  "leap16|ftps_on_a_real_host|installer/lib/89-ftps.sh|FTPS is not available on this OS family|suse"
)

# polygon_family_skipped_suites <image-family>: the suites that family does not run, one per line.
polygon_family_skipped_suites() {
  local row
  for row in "${POLYGON_FAMILY_SKIPS[@]}"; do
    [ "${row%%|*}" = "$1" ] || continue
    row="${row#*|}"
    printf '%s\n' "${row%%|*}"
  done
}

# polygon_test_args <image-family> <suite>...: `--test <suite>` for every suite the family runs, one
# word per line. The run steps use this so the list they run and the list verify expects come from
# the declaration above and nowhere else.
polygon_test_args() {
  local family="$1" suite
  shift
  for suite in "$@"; do
    if polygon_family_skipped_suites "$family" | grep -Fxq -- "$suite"; then
      continue
    fi
    printf -- '--test\n%s\n' "$suite"
  done
}

# POLYGON_LANE_OWNED_SUITES: the ONE declaration of which polygon suites belong to another lane, and
# how that lane proves them.
#
# Each row is `suite|lane|lane script|passed`. A suite listed here is an ignored real-host suite that
# the `polygon` job cannot run: it needs a stack only its own lane builds (the mail suite needs a
# running Postfix, Rspamd and nftables table, which `docker/polygon/asserts/mail-lane.sh` installs).
# This is not a skip and not an exemption from being proved. It MOVES the proof: `maran polygon
# verify` does not require the suite in the `polygon` job's logs and says so in its output, and the
# owning lane's verdict (polygon_lane_verdict, called by the lane script) requires the suite to have
# started, finished and passed EXACTLY `passed` tests. The lane script reads its suite and that count
# from this array, so the job, the verifier and the lane cannot disagree. A suite NOT declared here
# is held to every rule above: an undeclared ignored suite missing from the `polygon` logs is a failure.
# polygon_lane_declaration_problems proves each row against the tree, the lane script and the workflow.
POLYGON_LANE_OWNED_SUITES=(
  "mail_server_on_a_real_host|polygon-mail|docker/polygon/asserts/mail-lane.sh|1"
)

# polygon_lane_row_field <suite> <field-number>: one field of the suite's row; status 1 when undeclared.
polygon_lane_row_field() {
  local row key
  for row in ${POLYGON_LANE_OWNED_SUITES[@]+"${POLYGON_LANE_OWNED_SUITES[@]}"}; do
    key="${row%%|*}"
    [ "$key" = "$1" ] || continue
    printf '%s' "$row" | cut -d'|' -f"$2"
    return 0
  done
  return 1
}

# polygon_lane_of <suite>: the lane that owns the suite; status 1 when no lane does.
polygon_lane_of() { polygon_lane_row_field "$1" 2; }

# polygon_lane_script_of <suite>: the lane script's path, relative to the repository root.
polygon_lane_script_of() { polygon_lane_row_field "$1" 3; }

# polygon_lane_expected_passed <suite>: how many tests the owning lane must see pass, exactly.
polygon_lane_expected_passed() { polygon_lane_row_field "$1" 4; }

# polygon_lane_owned_suites: every lane-owned suite, one per line.
polygon_lane_owned_suites() {
  local row
  for row in ${POLYGON_LANE_OWNED_SUITES[@]+"${POLYGON_LANE_OWNED_SUITES[@]}"}; do
    printf '%s\n' "${row%%|*}"
  done
}

# polygon_lane_suites <lane>: the suites that lane owns, one per line.
polygon_lane_suites() {
  local row
  for row in ${POLYGON_LANE_OWNED_SUITES[@]+"${POLYGON_LANE_OWNED_SUITES[@]}"}; do
    [ "$(printf '%s' "$row" | cut -d'|' -f2)" = "$1" ] || continue
    printf '%s\n' "${row%%|*}"
  done
}

# polygon_without_lane_owned: reads suite names on stdin, writes those NO other lane owns. What the
# `polygon` job runs and requires is the discovered list through this filter.
polygon_without_lane_owned() {
  local suite
  while IFS= read -r suite; do
    polygon_lane_of "$suite" >/dev/null || printf '%s\n' "$suite"
  done
}

# polygon_lane_verdict <suite> <log>: the owning lane's proof that the suite ran. Prints the problems
# and returns 1 unless the log shows the suite's binary started, exactly one `test result:` line, and
# that line `ok` with exactly the declared number passed and none failed. "No tests ran", a different
# count and a failure are all refusals; an undeclared suite is refused too (nothing says what it owes).
polygon_lane_verdict() {
  local suite="$1" log="$2" expected clean results problems=""
  if ! expected="$(polygon_lane_expected_passed "$suite")"; then
    echo "  '$suite' is not declared in POLYGON_LANE_OWNED_SUITES, so no lane owes it and nothing says how many tests it must pass."
    return 1
  fi
  clean="$(mktemp)"
  sed -E 's/\x1b\[[0-9;]*[A-Za-z]//g' "$log" >"$clean" 2>/dev/null || true
  if ! polygon_banner_seen "$clean" "$suite"; then
    problems="$problems
  suite '$suite' never started: no 'Running tests/$suite.rs' line in the lane's output."
  fi
  results="$(grep -c '^test result:' "$clean" || true)"
  if [ "${results:-0}" -ne 1 ]; then
    problems="$problems
  suite '$suite' printed ${results:-0} 'test result:' lines; the lane runs exactly this one suite and owes exactly one."
  elif ! grep -Eq "^test result: ok\. ${expected} passed; 0 failed;" "$clean"; then
    problems="$problems
  suite '$suite' did not report 'ok. ${expected} passed; 0 failed': the lane owes exactly ${expected} passing test(s) (POLYGON_LANE_OWNED_SUITES)."
  fi
  rm -f "$clean"
  [ -z "$problems" ] && return 0
  printf '%s\n' "$problems"
  return 1
}

# polygon_lane_declaration_problems <root>: proves every lane-owned row against what it points at: the
# suite exists and is ignored, the passed count is a positive number, the lane script exists and asks
# this declaration for its suite and verdict, and the workflow has a job of that name. Prints the
# problems; status 1 on any. A row that points at nothing would exempt a suite from the polygon job
# and prove it nowhere.
polygon_lane_declaration_problems() {
  local root="$1" row suite lane script passed problems="" suite_file
  for row in ${POLYGON_LANE_OWNED_SUITES[@]+"${POLYGON_LANE_OWNED_SUITES[@]}"}; do
    IFS='|' read -r suite lane script passed <<<"$row"
    suite_file="$(find "$root"/agent/crates/*/tests -maxdepth 1 -name "$suite.rs" 2>/dev/null | head -1)"
    if [ -z "$suite_file" ]; then
      problems="$problems
  $suite is declared owned by lane $lane and the tree has no such suite (a stale declaration)."
    elif ! grep -q '#\[ignore' "$suite_file"; then
      problems="$problems
  $suite is declared owned by lane $lane but carries no #[ignore], so it is not a polygon suite."
    fi
    case "$passed" in ''|*[!0-9]*|0)
      problems="$problems
  $suite: the passed count '$passed' is not a positive number." ;;
    esac
    if [ ! -r "$root/$script" ]; then
      problems="$problems
  $suite: lane script $script does not exist."
    elif ! grep -q 'polygon_lane_suites' "$root/$script" || ! grep -q 'polygon_lane_verdict' "$root/$script"; then
      problems="$problems
  $suite: $script does not read its suite from POLYGON_LANE_OWNED_SUITES (polygon_lane_suites) and judge it with polygon_lane_verdict, so the lane would not prove the suite the polygon job stopped requiring."
    fi
    grep -q "^  $lane:" "$root/.github/workflows/agent.yml" 2>/dev/null || problems="$problems
  $suite: .github/workflows/agent.yml has no job named '$lane'."
  done
  [ -z "$problems" ] && return 0
  printf '%s\n' "$problems"
  return 1
}

# polygon_family_from_log <log>: the image family the log's stamp lines name, when they name exactly
# one. Nothing otherwise (no stamp, or several families), and the caller then exempts NOTHING: a log
# that cannot say which family it came from is held to every suite.
polygon_family_from_log() {
  local families
  families="$(grep -ao "$POLYGON_STAMP_PREFIX family=[A-Za-z0-9._-]*" "$1" 2>/dev/null \
    | sed 's/.* family=//' | sort -u)"
  [ "$(printf '%s' "$families" | grep -c . || true)" -eq 1 ] || return 1
  printf '%s' "$families"
}

# polygon_step_declares_unoffered <step-file> <sentence> <os-family>: 0 when the installer step prints
# the sentence and has the OS family's arm returning 1 from its `*_is_offered_on_family` gate.
polygon_step_declares_unoffered() {
  [ -r "$1" ] || return 1
  grep -qF -- "$2" "$1" || return 1
  grep -Eq "^[[:space:]]*$3\)[[:space:]]+return 1" "$1"
}

# polygon_skips_agree_with_installer <root>: every declared skip is backed by the installer, and the
# declaration is non-empty only for the family that has rows. Prints the problems; status 1 on any.
# Controls: the checker is shown to refuse a file lacking the sentence and to accept the real step,
# and the exemption is shown absent for the two PR families.
polygon_skips_agree_with_installer() {
  local root="$1" row family suite step sentence os problems="" planted fake
  planted="$(mktemp)"
  printf 'no such sentence here\n    suse) return 1 ;;\n' >"$planted"
  if polygon_step_declares_unoffered "$planted" "FTPS is not available on this OS family" suse; then
    problems="$problems
  the agreement checker ACCEPTED a file that lacks the sentence, so its acceptance of the installer means nothing."
  fi
  rm -f "$planted"
  [ "${#POLYGON_FAMILY_SKIPS[@]}" -gt 0 ] || problems="$problems
  POLYGON_FAMILY_SKIPS is empty, so nothing was checked."
  for fake in ubuntu24 alma9; do
    if [ -n "$(polygon_family_skipped_suites "$fake")" ]; then
      problems="$problems
  $fake is declared to skip a suite; only a family whose installer does not offer the feature may."
    fi
  done
  for row in "${POLYGON_FAMILY_SKIPS[@]}"; do
    IFS='|' read -r family suite step sentence os <<<"$row"
    if [ "$os" != suse ]; then
      problems="$problems
  $family|$suite: only the SUSE family is declared not to offer a feature."
    fi
    polygon_step_declares_unoffered "$root/$step" "$sentence" "$os" || problems="$problems
  $family|$suite: $step does not print '$sentence' with an '$os) return 1' gate, so this suite would be skipped for a family whose installer still offers the feature."
    [ -r "$root/agent/crates/agent/tests/$suite.rs" ] || [ -n "$(find "$root"/agent/crates/*/tests -name "$suite.rs" 2>/dev/null)" ] || problems="$problems
  $family|$suite: no such suite in agent/crates/*/tests, so the declaration is stale."
  done
  [ -z "$problems" ] && return 0
  printf '%s\n' "$problems"
  return 1
}

# polygon_fingerprint: the sha256 of everything a polygon image is built FROM.
#
# Currency is asked as a content question, not as a timestamp question, because a timestamp answers
# neither half of it: `touch` on an unchanged file makes a current image look stale, and a file
# restored from a backup with `cp -p` makes a stale image look current — the same mtime trap
# rules/testing.md records for `cp -p` restores.
#
# The inputs are the Dockerfile and its two source trees: `docker/polygon/**` (the Dockerfiles and
# the scripts they install and run at build time) and `installer/**` — the images RUN the installer's
# own steps at build time, which is the point of them, so an installer edit changes what the image
# is. That is exactly what was measured while writing this: both images predated four installer
# edits made the same afternoon.
polygon_fingerprint() {
  local root="$1" family="$2"
  {
    echo "family=$family"
    find "$root/docker/polygon" "$root/installer" -type f 2>/dev/null |
      sed "s|^$root/||" | sort |
      while IFS= read -r relative; do
        printf '%s  %s\n' "$(sha256sum <"$root/$relative" | cut -d' ' -f1)" "$relative"
      done
  } | sha256sum | cut -c1-12
}

# polygon_ensure_image: makes sure the image about to be measured against is the one this tree
# describes, and says what "current" was checked to mean.
#
# The tag IS the fingerprint, so currency is not a comparison the caller has to remember to make: an
# image built from different sources has a different name and simply is not there. A suite passing
# against a stale image measures the previous tree, which docker/README.md names as the most
# expensive defect shape this repository has produced — the installer's nginx gate validated the
# PREVIOUS configuration for the whole life of the installer and reported success.
polygon_ensure_image() {
  local context="$1" family="$2" fingerprint="$3" log="$4"
  local image="maran-polygon-$family:mut-$fingerprint"

  {
  echo "-- image currency ($family)"
  echo "   fingerprint of docker/polygon/** + installer/** : $fingerprint"
  if docker image inspect "$image" >/dev/null 2>&1; then
    echo "   CURRENT: $image is already built from exactly these sources."
  else
    local floating="maran-polygon-$family:latest"
    if docker image inspect "$floating" >/dev/null 2>&1; then
      echo "   STALE: $floating exists but was built from other sources (created" \
        "$(docker image inspect -f '{{.Created}}' "$floating"))."
      echo "          It is NOT used. A suite run against it would measure a tree that has changed."
    else
      echo "   ABSENT: no $family polygon image at all."
    fi
    echo "   building $image (context: the frozen snapshot, so a concurrent edit cannot enter it)"
    # Through polygon_build_labelled, so the fingerprint is recorded INSIDE the image as well as in
    # its tag. The tag protects this lane, which chooses the image by name; the label is what lets a
    # lane that did NOT choose the name — the `:latest` pair the scoring lane consumes — be checked
    # at all.
    if ! polygon_build_labelled "$context" "$family" "$fingerprint" "$image" "$log"; then
      echo "REFUSED: the $family polygon image did not build — nothing can be measured on it." >&2
      tail -30 "$log" >&2
      return 1
    fi
    echo "   built."
  fi

  polygon_note "currency here is a fingerprint of the SOURCES in this tree. It cannot see upstream"
  polygon_note "drift: the base image digest is pinned in the Dockerfile, but the nginx, php-fpm and"
  polygon_note "MariaDB package versions float deliberately (docker/README.md), so an image with a"
  polygon_note "matching fingerprint may still hold older packages than a fresh build would install."
  } >&2
  echo "$image"
  return 0
}

# POLYGON_FINGERPRINT_LABEL: the OCI label an image records its own source fingerprint in.
#
# The fingerprint has to travel WITH the image, not beside it, because the two questions are asked
# in two places and only one of them can see the image. `polygon_ensure_image` puts the fingerprint
# in the tag, which works because it builds the image itself; nothing puts it anywhere on the
# `:latest` pair an operator builds by hand from docker/README.md, and that pair is what the scoring
# lane consumes. A label is written at build time by the same command that builds, is carried by the
# image wherever it goes, and can be read back by one `docker image inspect` — so the run that uses
# the image can state, in its own log, which sources the image was made of.
#
# Absence of the label is NOT treated as "probably fine". An image built before this label existed —
# every `:latest` on this host today — records nothing about its sources, and an image that cannot
# say what it was built from cannot be shown to be current. It is refused by name, exactly like a
# mismatch, because the failure this closes is a run believed to have measured the current tree.
POLYGON_FINGERPRINT_LABEL="maran.polygon.fingerprint"

# POLYGON_STAMP_PREFIX: the word that makes a run's image identity findable in its own log.
#
# Why it is in the LOG and not in a side file: `maran polygon verify` scores logs, and it may score
# them minutes or days after the container exited, on a machine that never held the image. So the
# only durable place for "which image produced this" is the observation itself. Until now no log
# recorded it at all, which is why a stale image was invisible to the scorer — the scorer had no
# sentence to disbelieve.
POLYGON_STAMP_PREFIX="MARAN-POLYGON-IMAGE"

# polygon_image_fingerprint: the fingerprint an image records for itself, or the empty string.
#
# `docker image inspect` prints `<no value>` for a missing label under a `{{index …}}` template, and
# an image that is not present at all makes the command fail. Both are reduced to the empty string
# here: a caller must not be able to tell "no label" from "no image" by accident, because both mean
# the same thing to every caller of this function — nothing is known about these sources.
polygon_image_fingerprint() {
  local image="$1" value
  value="$(docker image inspect \
    -f "{{index .Config.Labels \"$POLYGON_FINGERPRINT_LABEL\"}}" "$image" 2>/dev/null)" || return 0
  case "$value" in
    ''|'<no value>') return 0 ;;
  esac
  printf '%s\n' "$value"
}

# polygon_build_labelled: builds a polygon family image with its own source fingerprint recorded in
# it. Every build this repository performs goes through here, so that "the image says what it was
# built from" is a property of the builder and not a thing each caller remembers.
#
# POLYGON_BUILD_CACHE_SCOPE (optional, set by the CI workflow only): when non-empty the build goes
# through `docker buildx build --load` and reads and writes the GitHub Actions layer cache under
# that scope (issue #289). What the cache may and may not change:
#   - It cannot change WHAT a COPY step contains: BuildKit keys a COPY layer on the content of the
#     files it copies, so an edit to docker/polygon/** or installer/** misses at that layer and
#     everything after it is rebuilt. The label is a build option, not a layer, so the fingerprint
#     is written on a cache hit exactly as on a cold build and `polygon_currency_from_log` still
#     compares the image against the tree.
#   - It CAN serve a package-install layer (`dnf install`, whose versions float on purpose) older
#     than a cold build would install. The workflow bounds that by putting the ISO week in the
#     scope, so the oldest package set a hit can serve is a week old, and the currency axis does
#     not claim to see upstream drift in any case (polygon_ensure_image's note).
# `ignore-error=true`: a cache outage costs minutes, never a verdict.
polygon_build_labelled() {
  local context="$1" family="$2" fingerprint="$3" image="$4" log="$5"
  local scope="${POLYGON_BUILD_CACHE_SCOPE:-}"
  local file="$context/docker/polygon/images/$family/suite.Dockerfile"
  if [ -z "$scope" ]; then
    docker build \
      --label "$POLYGON_FINGERPRINT_LABEL=$fingerprint" \
      -f "$file" -t "$image" "$context" >"$log" 2>&1
    return
  fi
  docker buildx build --load --progress plain \
    --cache-from "type=gha,scope=$scope" \
    --cache-to "type=gha,mode=max,scope=$scope,ignore-error=true" \
    --label "$POLYGON_FINGERPRINT_LABEL=$fingerprint" \
    -f "$file" -t "$image" "$context" >"$log" 2>&1
}

# polygon_stamp: the RUN-TIME half of the currency check, and the line the score reads later.
#
# It answers the question at the one moment the image is in front of it — before the container is
# started — and it emits a single line naming the family, the image and the fingerprint the image
# records. That line goes into the run's log, which is what makes the SCORING half possible at all.
#
# What this placement can see that scoring cannot: the image itself. It can read the label, it can
# say when the image is absent, and it can stop the run before an hour of container time is spent
# measuring the previous tree.
#
# What it cannot see: whether the log it is writing into is the log that will be scored, and whether
# the tree will still be this tree when the score is taken. So it does not replace the scoring check;
# the two are deliberately both present, and each names the other's blind spot.
#
# Status: 0 when the image records exactly this tree's fingerprint, 1 otherwise. A caller that
# ignores the status still cannot get a pass, because the line it printed is the evidence the scorer
# refuses on.
polygon_stamp() {
  local root="$1" family="$2" image="$3"
  local expected recorded
  expected="$(polygon_fingerprint "$root" "$family")"
  recorded="$(polygon_image_fingerprint "$image")"

  printf '%s family=%s image=%s built-from=%s\n' \
    "$POLYGON_STAMP_PREFIX" "$family" "$image" "${recorded:-UNSTAMPED}"

  if [ -z "$recorded" ]; then
    echo "REFUSED: $image records no $POLYGON_FINGERPRINT_LABEL label, so what it was built from is" >&2
    echo "         unknown. An image that cannot say what it was built from cannot be shown to be" >&2
    echo "         current, and a suite passing against it would have measured an unknown tree." >&2
    echo "         Rebuild it through this harness so the fingerprint is recorded:" >&2
    echo "           $(polygon_build_command "$family")" >&2
    return 1
  fi
  if [ "$recorded" != "$expected" ]; then
    echo "REFUSED: $image was built from OTHER SOURCES than this tree describes." >&2
    echo "         the image records : $recorded" >&2
    echo "         this tree hashes  : $expected" >&2
    echo "         (docker/polygon/** + installer/**; created $(docker image inspect \
      -f '{{.Created}}' "$image" 2>/dev/null))" >&2
    echo "         Rebuild it before running anything against it:" >&2
    echo "           $(polygon_build_command "$family")" >&2
    return 1
  fi
  return 0
}

# polygon_build_command: the one command a person types to rebuild a family's floating image.
#
# Every refusal about a missing or stale image prints this and nothing else, so the remedy is a
# `maran` command with the label already handled, never a raw `docker build` a reader can retype
# without the label — which produces exactly the unlabelled image the refusal was about.
polygon_build_command() {
  printf 'maran polygon build %s\n' "$1"
}

# polygon_require_current: refuses an image that is absent, records no fingerprint, or records one
# that is not this tree's. Status 0 only for an image built from exactly these sources; every
# refusal is on stderr and ends with the rebuild command.
#
# This is the question `maran dev` asks before it starts anything. A stack on a stale image runs
# the previous tree's OS layer and installer-provided stand-ins, and a behaviour that only the old
# image has then reads as a product defect — which is how issue #238 was found. So staleness is a
# refusal here, not a warning: a warning that lets the bring-up continue is one nobody reads.
polygon_require_current() {
  local root="$1" family="$2" image="$3" expected recorded
  if ! docker image inspect -f '{{.Id}}' "$image" >/dev/null 2>&1; then
    echo "REFUSED: the polygon image $image does not exist on this machine. Build it:" >&2
    echo "           $(polygon_build_command "$family")" >&2
    return 1
  fi
  expected="$(polygon_fingerprint "$root" "$family")"
  recorded="$(polygon_image_fingerprint "$image")"
  if [ -n "$recorded" ] && [ "$recorded" = "$expected" ]; then
    return 0
  fi
  if [ -z "$recorded" ]; then
    echo "REFUSED: $image records no $POLYGON_FINGERPRINT_LABEL label, so what it was built from" >&2
    echo "         is unknown (built before the label existed, or by hand)." >&2
  else
    echo "REFUSED: $image was built from OTHER SOURCES than this tree describes." >&2
    echo "         the image records : $recorded" >&2
    echo "         this tree hashes  : $expected  (docker/polygon/** + installer/**)" >&2
  fi
  echo "         Rebuild it (takes minutes), then run this command again:" >&2
  echo "           $(polygon_build_command "$family")" >&2
  return 1
}

# polygon_currency_from_log: the SCORING-TIME half. Reads the stamp lines out of an already-executed
# run's log and holds each one against this tree's fingerprint for that family.
#
# Why scoring needs its own check when the run already had one: the run's check is a sentence in a
# log, and a sentence in a log is only worth something if something later refuses to score a log
# that carries the wrong one. Without this half, a run could be started against a stale image by a
# caller that ignored the refusal — or by an operator following a README rather than the harness —
# and the score would be computed from it exactly as before.
#
# What this placement CANNOT see, stated rather than implied:
#   - the image. It may be gone, or on another machine; only the fingerprint the run wrote down.
#   - whether the tree moved between the run and the score. The comparison is against the tree as it
#     is NOW, so an installer edit made after a correct run makes that run unscoreable. That
#     direction is deliberate: a score is a claim about the current tree, and the conservative answer
#     when the two disagree is to refuse, not to guess which side is stale.
#   - a log with no stamp at all. It cannot distinguish "produced before this check existed" from
#     "produced against an image nobody checked", so it treats both the same way, which is the only
#     answer that is not a guess.
#
# Output is the finding text; status 0 when every stamp matches.
polygon_currency_from_log() {
  local root="$1" log="$2"
  local -a stamps=()
  local line family recorded expected problems=""

  mapfile -t stamps < <(grep -ao \
    "$POLYGON_STAMP_PREFIX family=[A-Za-z0-9._-]* image=[^[:space:]]* built-from=[A-Za-z0-9]*" \
    "$log" 2>/dev/null | sort -u)

  if [ "${#stamps[@]}" -eq 0 ]; then
    echo "  these logs do not record WHICH IMAGE produced them, so whether the container measured
  this tree or a previous one cannot be observed at all. A shared ':latest' tag has no relationship
  to the tree that defines it — one was found here holding /etc/shadow at the mode of an older
  build, which put twelve tests red over correct code. Emit the stamp line in the step that runs the
  container, before the 'docker run':
      scripts/maran polygon stamp <family> [image]"
    return 1
  fi

  for line in "${stamps[@]}"; do
    family="${line#* family=}"; family="${family%% *}"
    recorded="${line##*built-from=}"
    expected="$(polygon_fingerprint "$root" "$family")"
    if [ "$recorded" = "UNSTAMPED" ]; then
      problems="$problems
  the $family container ran against an image that records no source fingerprint, so what it was
  built from is unknown and these logs cannot be shown to describe this tree."
    elif [ "$recorded" != "$expected" ]; then
      problems="$problems
  the $family container ran against an image built from OTHER SOURCES than this tree describes:
      the image was built from : $recorded
      this tree hashes to      : $expected
  ($line)
  Every result in these logs is a measurement of a tree that has since changed. Rebuild the image
  and run it again; a stale image is why twelve tests once went red over correct code."
    fi
  done

  if [ -n "$problems" ]; then
    echo "  the image these logs were produced by is not the image this tree describes:$problems"
    return 1
  fi
  echo "${#stamps[@]} image stamp(s) in these logs, every one recording exactly the fingerprint of
  this tree's docker/polygon/** + installer/**
  $(polygon_note "the fingerprint is of the SOURCES, compared against the tree AS IT IS NOW. It cannot see upstream package drift, and it cannot see a tree that changed between the run and this score.")"
  return 0
}

# polygon_run: runs EVERY discovered suite of one family, as root, in one container.
#
# `--no-fail-fast` is passed, and it is not decoration: without it cargo stops at the first failing
# target and leaves every later suite unrun while reporting a single failure — so a mutant that kills
# a test in `account_deletion` would be reported as having killed nothing in `sftp`, `cron` or
# `privileges`, which were never executed. The polygon invocations in docker/README.md and in
# .github/workflows/agent.yml are missing it today.
#
# `--test-threads=1` because the suites share one nginx tree, one php-fpm pool directory, one system
# user database, one database server and one sshd — not a fixture two tests may hold at once.
#
# `--privileged` for the whole run rather than per group. CI splits the suites into three steps by
# the capabilities each needs, which is the right shape for CI (a suite started without its
# capabilities fails in a way that reads as a code defect). Here the subject is the mutant, not the
# runner, and a capability split would mean a hard-coded group membership — the thing that let four
# suites go unrun. Every suite runs, in one container, with everything they collectively need.
#
# The container is NAMED so it can be removed by hand if this process is killed, and the run is under
# a timeout: `docker stop` on a running suite was measured producing exit 0 for zero tests executed,
# so the exit status of this function is not the verdict. polygon_verify_ran is.
polygon_run() {
  local image="$1" snapshot="$2" target_dir="$3" log="$4" name="$5"
  shift 5
  local suite arguments=()
  for suite in "$@"; do
    arguments+=(--test "$suite")
  done

  mkdir -p "$target_dir"
  local status=0
  timeout --signal=TERM --kill-after=60 "${MARAN_POLYGON_TIMEOUT:-3600}" \
    docker run --rm --init --privileged --name "$name" \
    -v "$snapshot:/maran" \
    -v "$target_dir:/tmp/target" \
    -w /maran/agent \
    -e CARGO_TARGET_DIR=/tmp/target \
    "$image" \
    cargo test "${arguments[@]}" --no-fail-fast -- --ignored --test-threads=1 \
    >"$log" 2>&1 || status=$?
  docker rm -f "$name" >/dev/null 2>&1 || true
  echo "$status"
  return 0
}

# polygon_reconcile_counts: the count axis — every suite must execute exactly as many tests as the
# committed baseline records for it, and a suite the baseline has never heard of is refused.
#
# Why this exists. The four axes in polygon_verify_ran are all axes of PRESENCE: did the suite
# start, did it print a result line, did the run execute anything at all. None of them is an axis of
# SIZE. So a suite that starts in the container and runs FEWER tests than the tree declares — a test
# deleted, renamed out of the harness, filtered away, or losing its `#[test]` attribute — produced a
# green polygon lane, measured on this tree today: the host says a suite has ten ignored tests, the
# container runs eight, and nothing objected. `maran test` gained exactly this axis for the local
# lane (suite_compare's DECLARED total), and until now the polygon lane, the ONLY lane where those
# tests actually execute, had no equivalent.
#
# Where the expected number comes from, and why it needs no new file: `#[ignore]`d host tests do not
# run on a developer's machine, so the whole count of a `*_on_a_real_host` suite sits in the
# `ignored` column of scripts/test-baseline.txt, and `cargo test --ignored` in the container runs
# precisely that set. So the expectation is `baseline ignored` and the observation is container
# `passed + failed`, and the relation is EQUALITY, not an inequality. Measured on both families
# against the committed baseline: eleven suites, 2/10/1/5/6/10/5/3/15/10/10, equal on both. A second
# expectations file would be a second copy of numbers that only a docker run keeps honest, and the
# copy nobody runs is the copy that drifts.
#
# Equality, not "at least": a container running MORE than the baseline records is a suite that grew
# without `maran test rust --accept`, and letting that pass silently is how the baseline stops
# describing the tree. It is reported as a mismatch with the remedy named, not as a failure of the
# code under test.
#
# A suite the baseline does not know is REFUSED. The argument, since both answers cost something:
# accepting it leaves the hole open exactly where it is widest — on the newest, least-reviewed
# suite, whose count nothing else in the repository has ever checked — and a lane that quietly skips
# the check for some of its suites is the "green gate read as evidence" shape rules/testing.md is
# written against. Refusing does block work for one command, and that is the cost accepted here: the
# unblock is `maran test rust --accept` plus committing the diff, which is ALREADY mandatory for a
# new target, because suite_compare prints `target is new since the baseline` and test-verdict.sh
# turns any finding into `TEST VERDICT: FAILED`. So refusing adds no new obligation; it only refuses
# to score a lane whose scope it cannot check.
#
# The baseline handed here is the stack-stripped rust view of scripts/test-baseline.txt. If that
# file is missing entirely, this is not scored as a pass: an unreadable baseline refuses every
# suite as unrecorded, for the same reason.
#
# ONE CALL MAY CARRY MORE THAN ONE RUN, AND THE ANSWER IS PER LANE, NOT A SUM
#
# `maran polygon verify` takes several logs and concatenates them, because ONE family's run is split
# across capability steps. Hand it two FAMILIES and every suite reports twice, which used to feed a
# two-line string into `$((total_executed + executed))` and kill this function with
# `syntax error in expression` — an ABORTED verdict naming no finding, with bash's complaint on
# stderr as the only explanation, over a run that was clean. Reproduced on 2026-09-11 by handing one
# call the logs of both families.
#
# The repair is not a sum. A sum across families is the one shape that cannot see the failure this
# whole function exists for: a suite that ran on one family and VANISHED from the other still adds
# up to something plausible — 12 + 12 against a declared 24 is indistinguishable from 24 + 0 as far
# as a total is concerned, and "a suite vanished" is exactly what the count axis is for. So the
# observation is grouped into LANES — one complete run per lane — and the relation `executed ==
# baseline` is required PER LANE, with the lane named in every message.
#
# How a lane is identified, and the blind spot that comes with it: the libtest log carries test
# names and result lines and NOTHING that names a distribution, so this function cannot say
# "alma9". A lane here is an ORDINAL — the n-th time a suite reported, in the order the logs were
# given on the command line — and the lane count is the greatest number of times any one suite
# reported. That is stated in the output when there is more than one lane, because a reader who
# thinks "lane 2" means alma9 is reading something this code never observed.
#
# What the ordinal shape cannot see, stated rather than left to be discovered: two logs from the
# SAME family look exactly like two families, so this cannot refuse a caller who scored ubuntu24
# twice and never ran alma9 at all; and if a suite is missing from lane 1 rather than lane 2, the
# rows of the suites behind it still shift up, so the lane a suite is REPORTED missing from is not
# necessarily the lane that lost it — only the number of lanes it is missing from is exact. Naming
# the family would need the runner to label its logs, which is a change in the caller and not here.
#
# Every count is checked to be digits before any arithmetic touches it. A crash is not one of this
# harness's three answers, and the way this function failed was precisely an unnamed non-answer.
polygon_reconcile_counts() {
  local observed="$1" baseline="$2"
  shift 2
  local suite key expected executed problems="" total_expected=0 total_executed=0
  local lanes=0 reported index lane_label
  local -a rows

  # The lane count comes first, because every message below names it. It is the GREATEST number of
  # times any one suite reported: a suite missing from a lane must not be able to shrink the number
  # of lanes this function then holds every other suite to, which is the same "the run got smaller
  # so the expectation got smaller" defect the count axis exists to refuse.
  for suite in "$@"; do
    key="$suite [tests/$suite.rs]"
    reported="$(awk -F'\t' -v k="$key" '$1 == k {n++} END {print n + 0}' "$observed" 2>/dev/null)"
    case "$reported" in ''|*[!0-9]*) reported=0 ;; esac
    [ "$reported" -gt "$lanes" ] && lanes="$reported"
  done

  if [ "$lanes" -eq 0 ]; then
    echo "  the count does not reconcile with scripts/test-baseline.txt:
  NOT ONE discovered suite reported a 'test result:' line, so there is no lane here to score."
    return 1
  fi

  for suite in "$@"; do
    key="$suite [tests/$suite.rs]"
    expected="$(awk -F'\t' -v k="$key" '$1 == k {print $4; found = 1} END {if (!found) print "?"}' \
      "$baseline" 2>/dev/null)"
    if [ "$expected" = "?" ] || [ -z "$expected" ]; then
      problems="$problems
  suite '$suite' is NOT in the committed baseline, so how many tests it owes cannot be checked.
  This lane will not score a suite whose scope is unknown. Record it and commit the diff:
      maran test rust --accept"
      continue
    fi
    case "$expected" in ''|*[!0-9]*)
      problems="$problems
  suite '$suite' has a baseline row whose ignored column is not a number ('$expected'), so nothing
  can be compared against it. The baseline is not readable as a count."
      continue ;;
    esac

    mapfile -t rows < <(awk -F'\t' -v k="$key" '$1 == k {print $2 + $3}' "$observed" 2>/dev/null)
    if [ "${#rows[@]}" -lt "$lanes" ]; then
      problems="$problems
  suite '$suite' reported in ${#rows[@]} of the $lanes lanes these logs hold — it is MISSING from
  $((lanes - ${#rows[@]})) of them. A suite that runs on one family and vanishes from another is
  the failure this axis exists for, and a summed total cannot see it."
    fi

    index=0
    for executed in "${rows[@]}"; do
      index=$((index + 1))
      lane_label=""
      [ "$lanes" -gt 1 ] && lane_label=" in lane $index of $lanes"
      case "$executed" in ''|*[!0-9]*)
        problems="$problems
  suite '$suite'$lane_label reported a count that is not a number ('$executed'). The log is not
  parseable as a score, so it is not scored."
        continue ;;
      esac
      total_expected=$((total_expected + expected))
      total_executed=$((total_executed + executed))
      if [ "$executed" -lt "$expected" ]; then
        problems="$problems
  suite '$suite'$lane_label executed $executed tests, but the baseline declares $expected for it
  ($((expected - executed)) fewer). A suite that runs a smaller set is not the suite that was
  baselined, and every test missing from it was scored against nothing."
      elif [ "$executed" -gt "$expected" ]; then
        problems="$problems
  suite '$suite'$lane_label executed $executed tests against a baseline of $expected
  ($((executed - expected)) more). The suite grew without the baseline: re-record with
  'maran test rust --accept'."
      fi
    done
  done

  if [ -n "$problems" ]; then
    echo "  the count does not reconcile with scripts/test-baseline.txt:$problems"
    [ "$lanes" -gt 1 ] && echo "  $(polygon_note "a lane above is an ordinal, not a family: these logs name no distribution.")"
    return 1
  fi
  if [ "$lanes" -gt 1 ]; then
    echo "$total_executed tests executed across $lanes lanes, exactly the $total_expected the
baseline declares for $lanes runs of every suite — reconciled PER LANE, never summed.
  $(polygon_note "a lane is an ordinal, not a family: these logs name no distribution, so this says every lane ran every suite at its baselined size, not WHICH family each lane was.")"
    return 0
  fi
  echo "$total_executed tests executed, exactly the $total_expected the baseline declares"
  return 0
}

# polygon_banner_seen: answers whether the container's log shows cargo starting THIS suite's binary.
#
# The banner is looked for ANYWHERE on a line, not at the start of one. Cargo writes it to stderr
# while a test binary writes `test <name> ... ` to stdout with no trailing newline, so under `2>&1`
# it is routinely spliced into the middle of a test line — measured on ubuntu24, where three of
# eleven suites announced themselves that way, and the presence axis (together with the count axis,
# whose rows suite_parse lost to the same anchor) reported a real mutation kill as ABORTED. A gate
# that turns a kill into an abort is not strict, it is wrong, and it teaches the next author to
# route around it.
#
# What refuses the trap this axis exists for is NOT the line anchor, which never contributed
# anything: it is the word `Running` and the `(<dir>/deps/<suite>-<hex>)` tail. A container that
# executed nothing exits 0 printing `Finished` and naming every suite executable; `Finished` is a
# different word, it does not match, and the binary named in the tail must be this suite's own.
polygon_banner_seen() {
  local log="$1" suite="$2"
  grep -qE "[[:space:]]Running .*tests/$suite\.rs \(\S*deps/$suite-[0-9a-f]+\)" "$log"
}

# polygon_selftest_presence: the POSITIVE and NEGATIVE control for polygon_banner_seen.
#
# The axis is a search, and a search that has silently stopped matching reports "never started" for
# a suite that ran — which is how a real kill became an ABORTED lane. So the expression is handed
# two planted lines before it is believed: the spliced shape it MUST accept, and the `Finished`
# shape naming a suite executable that it MUST refuse. It runs inside polygon_verify_ran, in the
# same shell, against the same function the verdict uses.
polygon_selftest_presence() {
  local sample accepted=0 refused=0
  sample="$(mktemp)"
  printf '%s\n' \
    'test something_in_the_previous_suite ...      Running tests/selftest_spliced_on_a_real_host.rs (/tmp/target/debug/deps/selftest_spliced_on_a_real_host-0123456789abcdef)' \
    '     Finished tests/selftest_absent_on_a_real_host.rs (/tmp/target/debug/deps/selftest_absent_on_a_real_host-0123456789abcdef)' \
    >"$sample"
  polygon_banner_seen "$sample" selftest_spliced_on_a_real_host && accepted=1
  polygon_banner_seen "$sample" selftest_absent_on_a_real_host || refused=1
  rm -f "$sample"

  if [ "$accepted" -ne 1 ]; then
    echo "SELF-TEST FAILED: the presence axis was handed a banner spliced into a test line — the" >&2
    echo "                  shape a real ubuntu24 run produced for three of eleven suites — and" >&2
    echo "                  did not see it. It would report a suite that ran as never started." >&2
    return 1
  fi
  if [ "$refused" -ne 1 ]; then
    echo "SELF-TEST FAILED: the presence axis accepted a 'Finished' line naming a suite" >&2
    echo "                  executable. That is the exact output of a container that executed" >&2
    echo "                  nothing and exited 0, and this axis exists to refuse it." >&2
    return 1
  fi
  return 0
}

# polygon_verify_ran: the guard for the two ways a container reports nothing as if it were something.
#
# 1. A KILLED container. `docker stop` on a running suite was measured here producing **exit 0 for
#    zero tests executed** — the daemon reports a clean stop and cargo never got to print a result
#    line. 137 (SIGKILL) and 143 (SIGTERM) are named, and so is 124 from `timeout`, but none of them
#    is trusted as the observation: the observation is the result lines below.
# 2. A container that never got to run at all — an image whose entrypoint failed, a mount that was
#    not there, a `cargo` that exited before compiling. Same shape: no result line.
#
# So the requirement is stated positively and per suite: every suite this run asked for must have
# produced its own `test result:` line, and the run as a whole must have executed at least one test.
# A run that fails this is ABORTED. It is not a kill and it is not a survivor.
polygon_verify_ran() {
  local log="$1" status="$2" observed="$3" baseline="$4"
  shift 4
  local problems="" suite

  case "$status" in
    0|101) ;;
    124|137|143)
      problems="$problems
  the container was KILLED or timed out (status $status). A killed container measured nothing."
      ;;
    *)
      problems="$problems
  docker exited $status — not a test result. Whatever that is, it is not a score."
      ;;
  esac

  if ! polygon_selftest_presence; then
    problems="$problems
  the presence axis failed its own self-test, so nothing it says below can be believed."
  fi

  for suite in "$@"; do
    if ! polygon_banner_seen "$log" "$suite"; then
      problems="$problems
  suite '$suite' never started: no 'Running tests/$suite.rs' line in the container's output."
    elif ! grep -qF "$suite [tests/$suite.rs]" "$observed"; then
      problems="$problems
  suite '$suite' started but produced NO 'test result:' line — it did not finish."
    fi
  done

  # Axis five: the COUNT. The four axes above ask whether a suite spoke; this one asks whether it
  # said as much as the committed baseline says it has to say. See polygon_reconcile_counts.
  local counts
  if ! counts="$(polygon_reconcile_counts "$observed" "$baseline" "$@")"; then
    problems="$problems
$counts"
  fi

  local executed
  executed="$(awk -F'\t' '{n += $2 + $3} END {print n + 0}' "$observed")"
  if [ "${executed:-0}" -eq 0 ]; then
    problems="$problems
  ZERO tests executed. An empty run is not a green run — this is the exact shape a stopped
  container produces, and it exits 0."
  fi

  if [ -n "$problems" ]; then
    echo "ABORTED — this lane measured nothing:$problems"
    return 1
  fi
  # The count axis is stated even when it passes: a gate whose agreement is invisible is a gate the
  # next reader cannot tell was asked at all.
  echo "$# suites discovered, $executed tests executed in total — $counts"
  return 0
}
