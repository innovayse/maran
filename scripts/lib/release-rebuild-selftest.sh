#!/usr/bin/env bash
# Selftest for `maran release rebuild`: `maran release rebuild-selftest`.
#
# SOURCED by scripts/lib/release-bundle.sh after scripts/lib/release-rebuild.sh. Docker is not available
# here, so the real agent build is replaced through the ONE seam release_rebuild_main has for it,
# release_rebuild_build_agent <export dir> <staging dir> <arch>. The selftest redefines that function
# INSIDE A SUBSHELL (release_rebuild_selftest below), so nothing outside it ever sees the stub. The stub
# "builds" a fixed fake binary from the CONTENT of the export it is handed, so a different export gives a
# different hash, and it records (a) that it was invoked, with the SOURCE_DATE_EPOCH it saw, and (b) the
# list of files in the export. Those two records are how the cases below observe what they claim:
#
#   - "refused BEFORE any hash is read" is observed by the stub's record staying EMPTY (the build never
#     started) and by the manifest's claimed hash never appearing in the output;
#   - "the export is clean" is observed by the stub's file list, which is the build's own input.
#
# Each refusal is paired with the inverse control rules/testing.md asks for: the same command on an
# untouched bundle ACCEPTS, so a rebuild mutated to refuse everything fails here.
#
# UNOBSERVED HERE: the real agent build (docker, qemu for aarch64) is not run; reproducibility of the real
# binary is measured only by running maran release rebuild on a tag.

# A hash no build can produce: a manifest naming it is wrong, and finding it in output proves the
# manifest was read.
readonly RR_CLAIMED_SHA="deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef"

# rr_git: <git args>. Git against the throwaway repository ($RR_REPO), with identity and signing given
# per call so that no git configuration, local or global, is written, and with fixed commit dates so a
# build that used the wall clock instead of the commit's time is visible.
rr_git() {
  env GIT_AUTHOR_DATE="2021-03-04T05:06:07Z" GIT_COMMITTER_DATE="2021-03-04T05:06:07Z" \
    git -C "$RR_REPO" -c user.name=rebuild-selftest -c user.email=selftest@example.invalid \
    -c commit.gpgsign=false -c tag.gpgsign=false "$@"
}

# rr_plant_repo: stages a two-commit throwaway repository with an `agent/` tree and tags the second
# commit v1.2.3. Leaves $RR_REPO set to it.
rr_plant_repo() {
  RR_REPO="${RR_WORK}/repo"
  install -d "${RR_REPO}/agent/src"
  git init -q "$RR_REPO"
  echo "fn main() {}" > "${RR_REPO}/agent/src/main.rs"
  echo "# lock 1" > "${RR_REPO}/agent/Cargo.lock"
  rr_git add -A
  rr_git commit -q -m "first"
  echo "fn main() { println!(\"agent\"); }" > "${RR_REPO}/agent/src/main.rs"
  rr_git add -A
  rr_git commit -q -m "second"
  rr_git tag v1.2.3
}

# release_rebuild_selftest_builder: <export dir> <staging dir> <arch>. The stub builder; see the header.
# It writes the staging layout build_agent writes: <staging>/agent-<arch>/maran-agent.
release_rebuild_selftest_builder() {
  local export_dir="$1" staging="$2" arch="$3"
  echo "invoked arch=${arch} epoch=${SOURCE_DATE_EPOCH:-unset}" >> "$RR_STUB_LOG"
  ( cd "$export_dir" && find . -type f | LC_ALL=C sort ) > "$RR_STUB_TREE"
  install -d "${staging}/agent-${arch}"
  {
    echo "fake maran-agent for ${arch}"
    cat "${export_dir}/agent/src/main.rs" "${export_dir}/agent/Cargo.lock"
  } > "${staging}/agent-${arch}/maran-agent"
  chmod 0755 "${staging}/agent-${arch}/maran-agent"
}

# rr_build_reference: <tag> <arch> <dest tarball>. Builds the "released" archive the cases compare
# against, with its OWN git archive and tar_component calls rather than release_rebuild_main's functions,
# so an export that is wrong in the main path cannot agree with a reference that shares the defect.
rr_build_reference() {
  local tag="$1" arch="$2" dest="$3"
  local exp="${RR_WORK}/ref-export" stage="${RR_WORK}/ref-stage" epoch
  rm -rf -- "$exp" "$stage"
  install -d "$exp"
  rr_git archive --format=tar "$tag" | tar -x -C "$exp"
  epoch="$(rr_git log -1 --format=%ct "$tag")"
  ( export SOURCE_DATE_EPOCH="$epoch"
    release_rebuild_selftest_builder "$exp" "$stage" "$arch"
    tar_component "${stage}/agent-${arch}" "$dest" )
}

# rr_write_manifest: <dir> <version> <arch=sha256>... A manifest in the shape `maran release build`
# writes, holding only the agent entries this command reads, one per `arch=sha256` pair.
rr_write_manifest() {
  local dir="$1" version="$2" pair arch sha first=1
  shift 2
  install -d "$dir"
  {
    echo "{"
    echo "  \"version\": \"${version}\","
    echo "  \"channel\": \"stable\","
    echo "  \"artifacts\": {"
    for pair in "$@"; do
      arch="${pair%%=*}"
      sha="${pair#*=}"
      [ "$first" -eq 1 ] || echo "    },"
      first=0
      echo "    \"agent-${arch}\": {"
      echo "      \"url\": \"https://releases.maran.innovayse.com/stable/agent-${arch}.tar.gz\","
      echo "      \"sha256\": \"${sha}\""
    done
    echo "    }"
    echo "  }"
    echo "}"
  } > "${dir}/manifest.json"
}

# rr_plant_bundle: <dir> <version> <arch> <sha256> <signing key> [downloaded tarball]. A bundle dir with
# a manifest signed by the key and, when given, the downloaded agent archive beside it.
rr_plant_bundle() {
  local dir="$1" version="$2" arch="$3" sha="$4" key="$5" tarball="${6:-}"
  rr_write_manifest "$dir" "$version" "${arch}=${sha}"
  sign_release_file "$key" "${dir}/manifest.json" "${dir}/manifest.json.sig"
  [ -z "$tarball" ] || cp "$tarball" "${dir}/agent-${arch}.tar.gz"
}

# rr_run: <output file> <rebuild args...>. Runs release_rebuild_main in a subshell (it exits), with the
# stub's records cleared first, capturing both streams in the file; sets RR_RC to its status.
rr_run() {
  local out="$1"
  shift
  rm -f -- "$RR_STUB_LOG" "$RR_STUB_TREE"
  RR_RC=0
  ( release_rebuild_main "$@" ) >"$out" 2>&1 || RR_RC=$?
}

# rr_expect: <label> <command...>. Passes when the command succeeds; otherwise the selftest fails naming
# the label.
rr_expect() {
  local label="$1"
  shift
  if "$@"; then
    echo "  ok: ${label}"
  else
    release_failed "rebuild-selftest: ${label}"
  fi
}

# rr_not: <command...>. Succeeds when the command fails, so rr_expect can state an absence.
rr_not() {
  ! "$@"
}

# rr_stub_ran: succeeds when the stub builder was invoked in the last rr_run.
rr_stub_ran() {
  [ -s "$RR_STUB_LOG" ]
}

# rr_cases: the cases themselves, run inside the subshell that holds the stub. Expects RR_WORK set.
rr_cases() {
  local arch good_sha out="${RR_WORK}/out.txt" keydir="${RR_WORK}/keys" bundle="${RR_WORK}/bundle"
  RR_STUB_LOG="${RR_WORK}/stub.log"
  RR_STUB_TREE="${RR_WORK}/stub.tree"
  arch="$(arch_name)"
  install -d -m 0700 "$keydir"
  # The SAME curve and digest the real key uses (rules/testing.md): a selftest that signs with another
  # algorithm would verify an invocation nobody ships. Two keys: the pinned one and a stranger's.
  openssl ecparam -name secp384r1 -genkey -noout -out "${keydir}/release.key" >/dev/null 2>&1 \
    || release_failed "rebuild-selftest: could not generate the throwaway release key"
  openssl pkey -in "${keydir}/release.key" -pubout -out "${keydir}/release.pub" >/dev/null 2>&1 \
    || release_failed "rebuild-selftest: could not derive the throwaway public key"
  openssl ecparam -name secp384r1 -genkey -noout -out "${keydir}/stranger.key" >/dev/null 2>&1 \
    || release_failed "rebuild-selftest: could not generate the stranger key"
  rr_plant_repo || release_failed "rebuild-selftest: could not plant the throwaway repository"
  rr_build_reference v1.2.3 "$arch" "${RR_WORK}/reference.tar.gz" \
    || release_failed "rebuild-selftest: could not build the reference archive"
  good_sha="$(sha256_of "${RR_WORK}/reference.tar.gz")" || release_failed "rebuild-selftest: no reference hash"
  local pub="${keydir}/release.pub" common=(--tag v1.2.3 --pubkey "${keydir}/release.pub" --repo "$RR_REPO" --arch "$arch")
  rr_plant_bundle "$bundle" 1.2.3 "$arch" "$good_sha" "${keydir}/release.key" "${RR_WORK}/reference.tar.gz"

  echo "-- case 1: a matching, correctly signed bundle is ACCEPTED (the inverse control) --"
  rr_run "$out" "${common[@]}" --bundle "$bundle"
  rr_expect "exit status 0" [ "$RR_RC" -eq 0 ]
  rr_expect "the verdict names the architecture and hash" grep -qF "RELEASE VERDICT: OK" "$out"
  rr_expect "the verdict carries the signed hash" grep -qF "${arch} sha256 ${good_sha}" "$out"
  rr_expect "the tool versions are printed" grep -qE "^  tar: " "$out"
  rr_expect "no reachability warning when origin/main is absent (the inverse of case 9)" rr_not grep -qF "WARNING" "$out"
  rr_expect "the stub builder ran once (so the later 'not invoked' checks can see a build)" \
    [ "$(wc -l < "$RR_STUB_LOG")" -eq 1 ]
  rr_expect "the build saw the tag commit's time as SOURCE_DATE_EPOCH" \
    grep -qF "epoch=$(rr_git log -1 --format=%ct v1.2.3)" "$RR_STUB_LOG"
  rr_expect "the output states what is NOT claimed" grep -qF "NOT CLAIMED" "$out"

  echo "-- case 2: a manifest signed by another key is refused before anything is read or built --"
  local forged="${RR_WORK}/forged"
  rr_plant_bundle "$forged" 1.2.3 "$arch" "$RR_CLAIMED_SHA" "${keydir}/stranger.key"
  rr_run "$out" "${common[@]}" --bundle "$forged"
  rr_expect "non-zero status" rr_not [ "$RR_RC" -eq 0 ]
  rr_expect "the refusal is about the signature" grep -qi "signature" "$out"
  rr_expect "the build never started" rr_not rr_stub_ran
  rr_expect "the manifest's claimed hash was never read into the output" rr_not grep -qF "$RR_CLAIMED_SHA" "$out"

  echo "-- case 3: a manifest edited after signing is refused on its signature, not on what it says --"
  # The edit changes BOTH the version (wrong for the tag) and the hash. If the version were checked
  # first the refusal would name the version; it must name the signature.
  local edited="${RR_WORK}/edited"
  cp -r "$bundle" "$edited"
  sed -i -e 's/"version": "1.2.3"/"version": "9.9.9"/' -e "s/${good_sha}/${RR_CLAIMED_SHA}/" "${edited}/manifest.json"
  rr_expect "the planted edit landed" grep -qF "$RR_CLAIMED_SHA" "${edited}/manifest.json"
  rr_run "$out" "${common[@]}" --bundle "$edited"
  rr_expect "non-zero status" rr_not [ "$RR_RC" -eq 0 ]
  rr_expect "the refusal is about the signature" grep -qi "signature" "$out"
  rr_expect "the version check did not run first" rr_not grep -qF "9.9.9" "$out"
  rr_expect "the build never started" rr_not rr_stub_ran
  rr_expect "the edited hash never reached the output" rr_not grep -qF "$RR_CLAIMED_SHA" "$out"

  echo "-- case 4: a missing signature file is refused --"
  local unsigned="${RR_WORK}/unsigned"
  cp -r "$bundle" "$unsigned"
  rm -f "${unsigned}/manifest.json.sig"
  rr_run "$out" "${common[@]}" --bundle "$unsigned"
  rr_expect "non-zero status" rr_not [ "$RR_RC" -eq 0 ]
  rr_expect "the build never started" rr_not rr_stub_ran

  echo "-- case 5: a validly signed manifest with the wrong hash is refused, showing both hashes --"
  local wrong="${RR_WORK}/wrong-hash"
  rr_plant_bundle "$wrong" 1.2.3 "$arch" "$RR_CLAIMED_SHA" "${keydir}/release.key" "${RR_WORK}/reference.tar.gz"
  rr_run "$out" "${common[@]}" --bundle "$wrong"
  rr_expect "non-zero status" rr_not [ "$RR_RC" -eq 0 ]
  rr_expect "the rebuilt hash is shown" grep -qF "$good_sha" "$out"
  rr_expect "the manifest's hash is shown" grep -qF "$RR_CLAIMED_SHA" "$out"
  rr_expect "no OK verdict was printed" rr_not grep -qF "RELEASE VERDICT: OK" "$out"

  echo "-- case 6: a downloaded archive that is not what the manifest signed is refused, naming the member --"
  local swapped="${RR_WORK}/swapped" swap_src="${RR_WORK}/swap-src" swapped_sha
  rm -rf -- "$swap_src"
  install -d "$swap_src"
  tar -xzf "${RR_WORK}/reference.tar.gz" -C "$swap_src"
  echo "an extra line in the shipped binary" >> "${swap_src}/maran-agent"
  rr_plant_bundle "$swapped" 1.2.3 "$arch" "$good_sha" "${keydir}/release.key"
  ( export SOURCE_DATE_EPOCH="$(rr_git log -1 --format=%ct v1.2.3)"; tar_component "$swap_src" "${swapped}/agent-${arch}.tar.gz" )
  swapped_sha="$(sha256_of "${swapped}/agent-${arch}.tar.gz")"
  rr_run "$out" "${common[@]}" --bundle "$swapped"
  rr_expect "non-zero status" rr_not [ "$RR_RC" -eq 0 ]
  rr_expect "the downloaded hash is shown" grep -qF "$swapped_sha" "$out"
  rr_expect "the rebuilt hash is shown" grep -qF "$good_sha" "$out"
  rr_expect "the first differing member is named" grep -qE "^ +[<>].*maran-agent" "$out"

  echo "-- case 7: a manifest signed for another version than the tag is refused --"
  local other_version="${RR_WORK}/other-version"
  rr_plant_bundle "$other_version" 1.2.4 "$arch" "$good_sha" "${keydir}/release.key" "${RR_WORK}/reference.tar.gz"
  rr_run "$out" "${common[@]}" --bundle "$other_version"
  rr_expect "non-zero status" rr_not [ "$RR_RC" -eq 0 ]
  rr_expect "the refusal names the version" grep -qF "1.2.4" "$out"
  rr_expect "the build never started" rr_not rr_stub_ran

  echo "-- case 8: a tag the repository does not have is refused --"
  rr_run "$out" --tag v9.9.9 --pubkey "$pub" --repo "$RR_REPO" --arch "$arch" --bundle "$(rr_bundle_for 9.9.9 "$arch" "$good_sha" "${keydir}/release.key")"
  rr_expect "non-zero status" rr_not [ "$RR_RC" -eq 0 ]
  rr_expect "the refusal names the missing tag" grep -qF "does not exist" "$out"
  rr_expect "the build never started" rr_not rr_stub_ran

  echo "-- case 9: a tag off origin/main only WARNS (the soft check), and still rebuilds --"
  rr_git update-ref refs/remotes/origin/main "$(rr_git rev-parse v1.2.3~1)"
  rr_run "$out" "${common[@]}" --bundle "$bundle"
  rr_expect "exit status 0" [ "$RR_RC" -eq 0 ]
  rr_expect "a warning names origin/main" grep -qF "WARNING" "$out"
  rr_git update-ref -d refs/remotes/origin/main

  echo "-- case 10: the export is clean: an untracked file and an uncommitted edit do not reach the build --"
  echo "planted" > "${RR_REPO}/agent/UNTRACKED-PLANT.txt"
  echo "uncommitted edit" >> "${RR_REPO}/agent/src/main.rs"
  rr_expect "positive control: the planted file is in the working repository" \
    [ -f "${RR_REPO}/agent/UNTRACKED-PLANT.txt" ]
  rr_expect "positive control: git sees it as untracked" \
    grep -qF "UNTRACKED-PLANT.txt" <(rr_git status --porcelain)
  rr_run "$out" "${common[@]}" --bundle "$bundle"
  rr_expect "still matches the released hash (the edit did not reach the build)" [ "$RR_RC" -eq 0 ]
  rr_expect "the build's input lists the tracked source (the listing can see files)" \
    grep -qF "./agent/src/main.rs" "$RR_STUB_TREE"
  rr_expect "the build's input does not list the untracked file" rr_not grep -qF "UNTRACKED-PLANT" "$RR_STUB_TREE"

  echo "-- case 11: a tag with characters outside the tag charset, or a leading dash, is refused --"
  local bad_tag
  for bad_tag in 'v1.2.3;touch x' '-v1.2.3' 'v1 2'; do
    rr_run "$out" --tag "$bad_tag" --pubkey "$pub" --repo "$RR_REPO" --arch "$arch" --bundle "$bundle"
    rr_expect "'${bad_tag}': non-zero status" rr_not [ "$RR_RC" -eq 0 ]
    rr_expect "'${bad_tag}': refused as a tag name" grep -qF "not a tag name" "$out"
    rr_expect "'${bad_tag}': the build never started" rr_not rr_stub_ran
  done

  echo "-- case 12: an architecture the release does not build for is a usage error (status 2) --"
  rr_run "$out" --tag v1.2.3 --pubkey "$pub" --repo "$RR_REPO" --arch riscv64 --bundle "$bundle"
  rr_expect "status 2" [ "$RR_RC" -eq 2 ]
  rr_expect "the build never started" rr_not rr_stub_ran

  echo "-- case 13: a missing or malformed signed sha256 for the architecture is refused before any build --"
  local other_arch="x86_64" sha_case dir
  [ "$arch" != "x86_64" ] || other_arch="aarch64"
  dir="${RR_WORK}/no-entry"
  rr_plant_bundle "$dir" 1.2.3 "$other_arch" "$good_sha" "${keydir}/release.key"
  rr_run "$out" "${common[@]}" --bundle "$dir"
  rr_expect "no entry for ${arch}: non-zero status" rr_not [ "$RR_RC" -eq 0 ]
  rr_expect "no entry for ${arch}: refused on the sha256" grep -qF "no valid sha256" "$out"
  rr_expect "no entry for ${arch}: the build never started" rr_not rr_stub_ran
  for sha_case in "not-a-hash" "abc123" "${good_sha^^}"; do
    dir="${RR_WORK}/bad-sha"
    rr_plant_bundle "$dir" 1.2.3 "$arch" "$sha_case" "${keydir}/release.key"
    rr_run "$out" "${common[@]}" --bundle "$dir"
    rr_expect "sha256 '${sha_case:0:12}': non-zero status" rr_not [ "$RR_RC" -eq 0 ]
    rr_expect "sha256 '${sha_case:0:12}': refused" grep -qE "no valid sha256|not 64 hex" "$out"
    rr_expect "sha256 '${sha_case:0:12}': the build never started" rr_not rr_stub_ran
  done

  echo "-- case 14: --arch both rebuilds both, prints no OK per architecture and exactly one verdict --"
  local both="${RR_WORK}/both" x_sha a_sha
  rr_build_reference v1.2.3 x86_64 "${RR_WORK}/ref-x86_64.tar.gz" || release_failed "rebuild-selftest: no x86_64 reference"
  rr_build_reference v1.2.3 aarch64 "${RR_WORK}/ref-aarch64.tar.gz" || release_failed "rebuild-selftest: no aarch64 reference"
  x_sha="$(sha256_of "${RR_WORK}/ref-x86_64.tar.gz")"
  a_sha="$(sha256_of "${RR_WORK}/ref-aarch64.tar.gz")"
  rr_expect "the two architectures' references differ (so a swap would be seen)" [ "$x_sha" != "$a_sha" ]
  rr_write_manifest "$both" 1.2.3 "x86_64=${x_sha}" "aarch64=${a_sha}"
  sign_release_file "${keydir}/release.key" "${both}/manifest.json" "${both}/manifest.json.sig"
  cp "${RR_WORK}/ref-x86_64.tar.gz" "${both}/agent-x86_64.tar.gz"
  cp "${RR_WORK}/ref-aarch64.tar.gz" "${both}/agent-aarch64.tar.gz"
  rr_run "$out" --tag v1.2.3 --pubkey "$pub" --repo "$RR_REPO" --arch both --bundle "$both"
  rr_expect "exit status 0" [ "$RR_RC" -eq 0 ]
  rr_expect "both architectures were built" [ "$(wc -l < "$RR_STUB_LOG")" -eq 2 ]
  rr_expect "exactly one verdict line" [ "$(grep -c "RELEASE VERDICT" "$out")" -eq 1 ]
  rr_expect "the verdict carries both hashes" grep -qF "x86_64 sha256 ${x_sha}, aarch64 sha256 ${a_sha}" "$out"

  echo "-- case 15: --arch both with one architecture's hash wrong fails, and prints no OK verdict --"
  local half="${RR_WORK}/half"
  rr_write_manifest "$half" 1.2.3 "x86_64=${x_sha}" "aarch64=${RR_CLAIMED_SHA}"
  sign_release_file "${keydir}/release.key" "${half}/manifest.json" "${half}/manifest.json.sig"
  rr_run "$out" --tag v1.2.3 --pubkey "$pub" --repo "$RR_REPO" --arch both --bundle "$half"
  rr_expect "non-zero status" rr_not [ "$RR_RC" -eq 0 ]
  rr_expect "the good architecture was reported ok" grep -qF "ok: agent x86_64" "$out"
  rr_expect "the bad architecture is named in the failure" grep -qF "does not match for: aarch64" "$out"
  rr_expect "no OK verdict was printed" rr_not grep -qF "RELEASE VERDICT: OK" "$out"
}

# rr_bundle_for: <version> <arch> <sha256> <signing key>. Plants a correctly signed one-entry bundle for
# the version under the work directory and prints its path.
rr_bundle_for() {
  local dir="${RR_WORK}/bundle-for-$1"
  rr_plant_bundle "$dir" "$1" "$2" "$3" "$4"
  echo "$dir"
}

# release_rebuild_selftest: `maran release rebuild-selftest`. Plants a throwaway repository, keys and
# bundles, and runs rr_cases with the builder stubbed. Prints the blind spot last.
release_rebuild_selftest() {
  require_tool openssl "generates the throwaway keys and verifies signatures"
  require_tool git "plants the throwaway repository"
  RR_WORK="$(mktemp -d)"
  # shellcheck disable=SC2064
  trap "rm -rf -- '${RR_WORK}'" EXIT
  trap 'exit 130' INT
  trap 'exit 143' TERM
  # The stub replaces the seam for this subshell only; release_rebuild_build_agent is untouched outside.
  ( release_rebuild_build_agent() { release_rebuild_selftest_builder "$@"; }
    rr_cases ) \
    || release_failed "rebuild-selftest: a case failed (above)"
  echo
  release_ok "rebuild selftest: a matching bundle accepted; a forged or edited manifest refused before any hash was read or build started; a wrong hash, a swapped download, a version mismatch, an unknown tag, a bad tag name, a bad --arch and a missing or malformed signed hash refused; --arch both gave one verdict; the export held no untracked or uncommitted content."
  echo "UNOBSERVED HERE: the real agent build (docker, qemu for aarch64) is not run; reproducibility of the real binary is measured only by running maran release rebuild on a tag"
}
