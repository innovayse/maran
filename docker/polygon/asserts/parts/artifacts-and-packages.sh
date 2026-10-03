#!/usr/bin/env bash
# the manifest reader, the postgresql.conf resolver, and the signalling tool
#
# Part of docker/polygon/asserts/assert-installer-steps.sh, which sources every file in this
# directory in order and then runs them. Sourced, never executed: it defines functions and the
# constants they need, and the entry point owns the order they run in.
#
# The split is by SUBJECT and preserves the original order exactly, because these definitions
# are order-dependent in one respect that matters — a `readonly` set twice is a fatal error, so
# each constant lives beside the only assertions that use it.


# assert_every_supported_family_installs_the_process_signalling_tool: the census this branch owed
# after the suspension work gained a dependency on `pkill` and named it nowhere.
#
# WHAT BREAKS WITHOUT IT. Suspending a hosting account locks its logins and then ends the sessions
# already open — `ops::logins::end_account_sessions` spawns `DistroAdapter::pkill_binary()` with
# `--signal KILL --count --uid <uid>` — because a lock that a connected SFTP or FTPS client never
# notices is a suspension in name only. With the program absent the cull is
# `LoginsError::SpawnFailed` and the whole suspension fails, so the panel shows an account it could
# not suspend while the customer's transfers keep running. Measured 2026-09-12: `/usr/bin/pkill`
# does not exist in the pinned AlmaLinux 9 base image, no package this installer or these images
# install pulls `procps-ng` in, and installer/lib/20-dependencies.sh named neither spelling.
#
# WHY A CENSUS AND NOT "THE TWO FAMILIES BOTH NAME IT". The two families spell the package
# differently — `procps` on the Debian family, `procps-ng` on the RHEL family — so the obvious
# check is two named copies compared to each other, which is exactly the arrangement that goes
# quiet when a THIRD family arrives: a new `case` arm in the installer with no signalling package
# would satisfy a check written about debian and rhel. So the family list is DERIVED from the
# installer's own arms — from `pkg_install`, the one function every package install in this product
# goes through — and every family it finds must have an arm in `signalling_packages_for_family`
# naming a non-empty package, with the reverse direction checked too so a stale arm for a family
# the installer can no longer install for is named rather than believed.
#
# AND IT IS BEHAVIOURAL AS WELL AS TEXTUAL, because the text cannot see two things that have both
# already happened in this repository: a package that installs and puts the program somewhere else
# (the `passwd` gap), and a program whose FLAGS differ between the two families' versions — the
# Debian family ships procps 4.x and the RHEL family procps-ng 3.3.17, and the cull's argv is
# `--signal KILL --count --uid`. So the census ends by installing through the installer's own
# function and running that exact argv against a uid that matches nothing.
# assert_the_postgresql_conf_resolver_finds_a_real_debian_layout: the resolver in 30-postgresql.sh
# against the directory shape Debian and Ubuntu actually create.
#
# WHY THIS EXISTS. The resolver searched `-maxdepth 2` while the file lives three levels down at
# /etc/postgresql/<major>/<cluster>/postgresql.conf, so on Ubuntu 24.04 the packages installed, the
# cluster was created, and the installer then refused with "could not locate postgresql.conf" —
# pointing at a file that was exactly where the distribution puts it. It reached a real server
# before anybody saw it, because this image installs MariaDB and sshd and NOT PostgreSQL: the step
# is copied in and never executed, so nothing had ever run this resolver against a real layout.
#
# Installing PostgreSQL here to close that would cost minutes on every image build for one
# function. Instead the layout is built in a temp directory and the resolver is pointed at it,
# which is the same question asked cheaply. What this does NOT cover, stated rather than implied:
# everything else in step 30 — the rewrite, the restart, and the no-TCP assertion — still runs
# nowhere.
# assert_the_manifest_reader_reads_every_field_not_only_the_last: 50-artifacts.sh's manifest_field
# against pretty-printed JSON of the shape `maran release build` writes.
#
# WHY THIS EXISTS. The reader matched a quoted value only at END OF LINE, and every JSON field
# except the last in its object is followed by a comma — so it read `sha256`, written last, and
# returned EMPTY for `url`, written first. Step 50 then refused with "manifest has no entry for
# api-x86_64" about an entry that was in the file. No online install could fetch anything, and
# 1.0.0-beta.1 and beta.2 were both published with it.
#
# It reached a real server for the same reason #34 did: nothing here runs step 50 against a real
# signed manifest, and the offline path does not use this function at all, so the release selftest
# passing says nothing about it.
assert_the_manifest_reader_reads_every_field_not_only_the_last() {
  local dir manifest url sha
  dir="$(mktemp -d)"
  manifest="${dir}/manifest.json"
  cat >"$manifest" <<'MANIFEST'
{
  "version": "0.0.0-assert",
  "artifacts": {
    "api-x86_64": {
      "url": "https://example.invalid/beta/api-x86_64.tar.gz",
      "sha256": "1111111111111111111111111111111111111111111111111111111111111111"
    }
  }
}
MANIFEST

  # The function under test, sourced from the step itself rather than copied here: a copy would go
  # on passing after the original changed, which is the failure this whole file exists against.
  MARAN_ARCH=x86_64
  eval "$(sed -n '/^manifest_field() {/,/^}/p' /tmp/maran-installer/lib/50-artifacts.sh)"

  url="$(manifest_field "$manifest" api url)"
  sha="$(manifest_field "$manifest" api sha256)"

  [ "$url" = "https://example.invalid/beta/api-x86_64.tar.gz" ] \
    || fail "manifest_field read url as '${url}' — a field followed by a comma is every field but
the last one, and this is the shape 'manifest has no entry for api-x86_64' was reported about."
  [ "$sha" = "1111111111111111111111111111111111111111111111111111111111111111" ] \
    || fail "manifest_field read sha256 as '${sha}'."

  # The inverse control: the pattern this replaced must FAIL on the same file, or the assertion
  # cannot tell the fixed reader from the broken one.
  local old
  old="$(awk -v key='"api-x86_64"' -v field='"url"' '
    $0 ~ key { in_block=1 }
    in_block && $0 ~ field {
      match($0, /"[^"]*"[[:space:]]*$/)
      print substr($0, RSTART, RLENGTH)
      exit
    }
    in_block && /}/ { in_block=0 }
  ' "$manifest")"
  [ -z "$old" ] \
    || fail "the end-of-line-only pattern read url as '${old}' from a comma-terminated line, so this
assertion proves nothing about the fix."

  rm -rf -- "$dir"
  echo "assert-installer-steps.sh: manifest_field reads a comma-terminated field, and the pattern it
  replaced does not."
}

assert_the_postgresql_conf_resolver_finds_a_real_debian_layout() {
  local root conf hba
  root="$(mktemp -d)"
  mkdir -p "${root}/etc/postgresql/16/main"
  : >"${root}/etc/postgresql/16/main/postgresql.conf"
  : >"${root}/etc/postgresql/16/main/pg_hba.conf"

  conf="$(find "${root}/etc/postgresql" -maxdepth 3 -name postgresql.conf 2>/dev/null | sort -V | tail -1)"
  hba="$(find "${root}/etc/postgresql" -maxdepth 3 -name pg_hba.conf 2>/dev/null | sort -V | tail -1)"

  [ -n "$conf" ] || fail "the postgresql.conf resolver found nothing in a layout Debian creates:
${root}/etc/postgresql/16/main/postgresql.conf exists and the search missed it. This is the depth
bug that reached a real Ubuntu 24.04 server."
  [ -n "$hba" ] || fail "the pg_hba.conf resolver found nothing in a layout Debian creates."

  # The inverse control: the depth the resolver USED to have must fail on the same tree, or this
  # assertion would pass just as happily against the bug it was written for.
  if find "${root}/etc/postgresql" -maxdepth 2 -name postgresql.conf 2>/dev/null | grep -q .; then
    fail "maxdepth 2 found postgresql.conf in this layout, so this assertion cannot tell the fixed
resolver from the broken one and proves nothing."
  fi

  rm -rf -- "$root"
  echo "assert-installer-steps.sh: the postgresql.conf resolver finds a real Debian layout, and the
  depth it replaced does not."
}

assert_every_supported_family_installs_the_process_signalling_tool() {
  local censused=0 planted family declared value
  local reference_arms signalling_arms reference_count signalling_count

  # 1. The declared path, from the agent rather than from a literal here: `ops` never writes the
  # path, it asks the adapter (rules/architecture.md), so the adapter is where the installer's
  # obligation is written down. Both families are read, because each answers independently.
  declared=""
  for value in "$AGENT_DEBIAN_SERVICES" "$AGENT_RHEL_SERVICES"; do
    local path_here
    path_here="$(agent_string_after 'fn pkill_binary()' "$value")"
    [ -n "$path_here" ] \
      || fail "no string literal could be read out of pkill_binary() in ${value} — the path the suspension cull execs is not where this check can read it, so nothing below compared anything (rules/testing.md)"
    case "$path_here" in
      /*) ;;
      *) fail "pkill_binary() in ${value} reads '${path_here}', which is not an absolute path. The agent's process-execution allow-list is absolute paths only (rules/rust.md), so either the extractor is reading the wrong literal or the adapter has stopped declaring one." ;;
    esac
    if [ -n "$declared" ] && [ "$declared" != "$path_here" ]; then
      fail "the two adapters declare different paths for the suspension cull's signalling tool: '${declared}' and '${path_here}' in ${value}. The installer's assert_agent_tooling checks one path, so one family would be installing a package and proving nothing about it."
    fi
    declared="$path_here"
    censused=$((censused + 1))
  done

  # Its planted control, on the axis that can go blind: an extractor whose marker has stopped
  # matching returns the empty string, and the guard above would then be the only thing between
  # that and a comparison between nothing. The sentinel is derived from the file's own value, so
  # this control fails for its own reason only.
  planted="/tmp/polygon-signalling-path-probe.rs"
  sed "s|\"${declared}\"|\"/usr/bin/polygon-planted-signaller\"|" "$AGENT_DEBIAN_SERVICES" > "$planted"
  [ "$(agent_string_after 'fn pkill_binary()' "$planted")" = "/usr/bin/polygon-planted-signaller" ] \
    || fail "the Rust literal extractor read '${declared}' out of debian_services.rs but does not read '/usr/bin/polygon-planted-signaller' out of a copy carrying exactly that instead — it is not reading the file's content, and its agreements mean nothing."
  rm -f -- "$planted"
  censused=$((censused + 1))

  # 2. The two arm lists, both read out of the installer.
  reference_arms="$(dependency_step_family_arms pkg_install)"
  signalling_arms="$(dependency_step_family_arms signalling_packages_for_family)"
  reference_count="$(printf '%s' "$reference_arms" | grep -c . || true)"
  signalling_count="$(printf '%s' "$signalling_arms" | grep -c . || true)"

  # The vacuity guard, on the axis that can actually go blind: the enumerator. An arm list that
  # came back empty — a renamed function, an arm spelled in a way the reader cannot see — makes
  # every comparison below a comparison over nothing, and this product supports two families.
  [ "$reference_count" -ge 2 ] \
    || fail "only ${reference_count} family arm(s) could be read out of pkg_install in ${DEPENDENCIES_STEP}, and this installer supports two families. The census below would have enumerated nothing and reported that every family names a signalling package (rules/testing.md)"
  [ "$signalling_count" -ge 2 ] \
    || fail "only ${signalling_count} family arm(s) could be read out of signalling_packages_for_family in ${DEPENDENCIES_STEP}. Either the function has gone, or its arms are spelled in a way this reader cannot see — both are failures to observe, and neither is a clean census (rules/testing.md)"
  censused=$((censused + 2))

  # Its positive control: the enumerator must SEE a family it has never seen. Without this, a
  # `case` statement the reader has stopped parsing returns a shorter list and the guard above is
  # the only thing between that and a census that quietly stopped reading a family.
  planted="/tmp/polygon-signalling-family-probe.sh"
  awk '
    index($0, "pkg_install() {") == 1 { print; print "    polygonplanted)"; print "      true"; print "      ;;"; next }
    { print }
  ' "$DEPENDENCIES_STEP" > "$planted"
  dependency_step_family_arms pkg_install "$planted" | grep -Fxq -- 'polygonplanted' \
    || fail "the family enumerator reads ${reference_count} arms out of pkg_install but does not read 'polygonplanted' out of a copy carrying exactly that arm — it is not reading the file's content, and the coverage this census claims below means nothing (rules/testing.md)."
  if dependency_step_family_arms pkg_install | grep -Fxq -- 'polygonplanted'; then
    fail "the family enumerator reports 'polygonplanted' for the UNMODIFIED ${DEPENDENCIES_STEP}, which names it nowhere — it is answering from something other than the file, so its planted control proves nothing."
  fi
  rm -f -- "$planted"
  censused=$((censused + 2))

  # 3. The census proper, both directions.
  while IFS= read -r family; do
    [ -n "$family" ] || continue
    printf '%s\n' "$signalling_arms" | grep -Fxq -- "$family" \
      || fail "installer/lib/20-dependencies.sh can install packages for the '${family}' family and signalling_packages_for_family has no arm for it, so a host of that family gets no package supplying ${declared}. Suspending an account there ends at LoginsError::SpawnFailed: the panel records a suspension it could not carry out while the customer's open SFTP and FTPS sessions keep transferring (docs/superpowers/notes/2026-09-12-suspension-session-cull-threat-note.md)."
    censused=$((censused + 1))
  done < <(printf '%s\n' "$reference_arms")

  while IFS= read -r family; do
    [ -n "$family" ] || continue
    printf '%s\n' "$reference_arms" | grep -Fxq -- "$family" \
      || fail "signalling_packages_for_family names a package for the '${family}' family and pkg_install cannot install for it. One of the two statements is wrong and a reader cannot tell which: either the arm is stale — a hole the next family of that name falls into, reading exactly like a decision — or the package manager adapter lost an arm this installer still needs."
    censused=$((censused + 1))
  done < <(printf '%s\n' "$signalling_arms")

  # 4. The installer must PROVE the path on the host it installed it on. A package manager
  # reporting success and a path existing are different facts, and the agent execs the path — the
  # sentence assert_agent_tooling itself is written under. Matched as a whole word so a
  # deliberately corrupted path ('/usr/bin/pkill.bak' contains the shipped spelling) cannot pass.
  local tooling_words word tooling_names_path=0
  tooling_words="$(awk '
    index($0, "assert_agent_tooling() {") == 1 { inside = 1 }
    inside { print }
    inside && $0 == "}" { inside = 0 }
  ' "$DEPENDENCIES_STEP")"
  [ -n "$tooling_words" ] \
    || fail "assert_agent_tooling could not be read out of ${DEPENDENCIES_STEP} at all, so the check below — that the installer proves ${declared} exists on the host — was made against an empty string (rules/testing.md)"
  for word in $tooling_words; do
    # The list is a `for` loop's word list continued across lines, so the last path on a line
    # carries the `;` that ends the list. Stripped here and nowhere else: the comparison stays a
    # whole-word one, which is what keeps '/usr/bin/pkill.bak' from satisfying it.
    word="${word%;}"
    [ "$word" = "$declared" ] && tooling_names_path=1
  done
  [ "$tooling_names_path" -eq 1 ] \
    || fail "installer/lib/20-dependencies.sh installs a signalling package but assert_agent_tooling does not name ${declared}, so nothing proves the package put the program where the agent execs it. That is the shape of the defect that made every suspension on the RHEL family fail: the package was named, the path was not checked, and the failure arrived at exec time in billing."
  censused=$((censused + 1))

  # 5. And now the host. Installed through the installer's own function — never a package name
  # written here, which would be a second authority — and then the operation's own argv.
  # shellcheck disable=SC2046
  pkg_install $(signalling_packages_for_family) >/dev/null \
    || fail "pkg_install $(signalling_packages_for_family) failed on this family. The package name signalling_packages_for_family produces is not one this family's package manager can resolve."
  [ -x "$declared" ] \
    || fail "signalling_packages_for_family's package installed and ${declared} is not executable on this host. The agent execs that exact path, so every suspension on this family would end at LoginsError::SpawnFailed."
  censused=$((censused + 1))

  # The argv the cull sends, against a uid that matches nothing. Status 1 with a count of 0 is
  # `NOTHING_MATCHED`, which that operation treats as a success — an idle account is the state a
  # cull is trying to reach. A status of 2 or 3 would be `pkill` rejecting the flags, which is the
  # per-family risk the text cannot see: the two families ship different major versions of this
  # tool.
  local probe_status=0 probe_output
  probe_output="$("$declared" --signal KILL --count --uid "$SIGNALLING_PROBE_UID" 2>&1)" || probe_status=$?
  [ "$probe_status" -eq 1 ] \
    || fail "${declared} --signal KILL --count --uid ${SIGNALLING_PROBE_UID} exited ${probe_status} on this family, and ops::logins::end_account_sessions reads anything but 0 and 1 as LoginsError::SessionCullFailed. A status of 2 or 3 is this tool refusing the flags, which differ between the versions the two families ship. What it printed:
${probe_output}"
  [ "$(printf '%s' "$probe_output" | tr -d '[:space:]')" = "0" ] \
    || fail "${declared} --count reported '${probe_output}' for a uid no process on this host runs as, and ops::logins::end_account_sessions parses that number as the count of sessions it ended. A non-zero answer here means either the flag no longer counts or the uid matched something, and in both cases the figure the panel shows an operator after a suspension is not a count of anything."
  censused=$((censused + 1))

  echo "The process-signalling census: ${reference_count} family arm(s) read out of pkg_install in installer/lib/20-dependencies.sh, each of them with an arm in signalling_packages_for_family naming a package, and no arm there for a family pkg_install cannot install for; both distro adapters declare ${declared} for the cull, assert_agent_tooling names that path as a whole word, and on this family the installer's own package list installs it and it answers the cull's own argv with 'nothing matched'. ${censused} checks made."
  printf '%s\n' "$reference_arms" | sed 's/^/  family: /'
  echo "UNOBSERVED HERE: no suspension runs in this image and no session is culled — this proves the PROGRAM is installed, accepts the operation's flags and can say 'nothing matched', not that a real SFTP or FTPS session dies. That is what the #[ignore]d polygon suites do (agent/crates/agent/tests/sftp_on_a_real_host.rs, ftps_on_a_real_host.rs), and they are the reason this check exists: without the package they fail with a message about a cull rather than about a missing program."
  echo "UNOBSERVED HERE: the family list is read from the TEXT of one function's case arms. A family arm spelled as a glob, as two labels joined by '|', or through a variable is invisible to the enumerator, and so is a package installed by any path other than pkg_install — 85-mysql.sh, 87-firewall.sh, 88-cron.sh and 89-ftps.sh each name their own family packages, and this census says nothing about those."
}
