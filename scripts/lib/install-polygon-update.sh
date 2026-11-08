#!/usr/bin/env bash
# A LIBRARY, not a command: scripts/lib/install-polygon.sh sources this file.
#
# stage_update_rollback: the closing criterion of issue #128, against the real systemd container the
# other stages of the install polygon use.
#
#   "A panel updates to a newer signed release, keeps its data and its sites, rolls back on demand,
#    and refuses to be left broken by a release that does not start or a bundle it cannot trust."
#
# What it does, in order:
#   1. Builds signed offline bundles from THROWAWAY keys made for this run: releases A and B, two
#      whose api does not run, a FIXED one, and three the CLI must refuse. The component archives are
#      the real ones this run installed; only the manifest, its signature and a frontend marker file
#      (so which release is SERVED is observable) differ between releases.
#   2. Installs release A into a THIRD server; the harness copies the throwaway public key over the
#      CLI's baked path /usr/local/maran/cli/keys/release-signing.pub (no override exists in the product).
#   3. Seeds the panel through its API, creates a static site and fingerprints the seeded rows.
#   4. `maran update --check`, then `update --offline-tarball B --yes`: the switch, release.state, the
#      dump pre-A-to-B-<UTC>.dump, the daemons' real executable paths, health, data, site, marker.
#   5. `maran rollback --yes` back to A: PREVIOUS is then EMPTY, so a second rollback is refused.
#   6. Release C (api exits at once) and C2 (--migrate succeeds, the normal run exits 1): `maran
#      update` rolls back by itself, exits 1 and says so; release.state then holds PREVIOUS=VERSION
#      and a plain `maran rollback` is refused with the pointer to --restore-database.
#   7. Refusals that change NOTHING: the installed version again (exit 0), a flipped artifact byte, a
#      manifest signed by another key, no signature, a bundle in a world-writable directory, no --yes
#      without a terminal, --offline-tarball with --channel (usage, exit 64).
#   8. The retry: a FIXED release with the failed one's version replaces the abandoned directory and
#      succeeds; the dump directory then keeps only its 3 newest dumps.
#   9. The legacy host: the installed tree converted back to real directories (no `current`, no
#      releases/, no release.state); `maran update` adopts it into releases/legacy-<UTC>; `maran
#      rollback --restore-database --yes` returns to it and restores the dump named with that name.
#
# Bundle builder: scripts/lib/release-bundle.sh cannot be sourced (it runs its own dispatch at the
# bottom) and builds only from the real toolchains, so this file mirrors the format of `maran release
# build`, `sign` and `package`: manifest.json with version, the SIGNED channel and per-component url
# and sha256, ECDSA P-384 over SHA-384 through `openssl dgst`, integrity-manifest.json (the CLI no
# longer reads it; it keeps the bundle the shape of a real one), both signed, and one offline tarball
# whose members are listed by name (no `./` member, as release-package.sh does). Each bundle is
# verified on the HOST with the same openssl call before the container sees it, the production key is
# shown NOT to verify it, and the channel is shown to be covered by the signature. The CLI reads a
# tarball only from a root-owned directory nobody else can write; the harness puts the bundles in
# /root/update-bundles, sets that explicitly and asserts it (upd_assert_bundle_trust).
#
# Reads these globals from install-polygon.sh: container, root, log_dir, os, arch, mode, bundle,
# web_group, extra_containers, note, recovery_expect, wait_for_panel, start_server_container; and from
# install-polygon-panel-api.sh / -recovery.sh: rec_call, rec_login, rec_second_factor, rec_psql,
# rec_put_file, rec_lines, rec_journal_matches, rec_seed_panel and the REC_* values it sets.

UPD_VERSION_A="1.0.0-polygon.1" UPD_VERSION_B="1.0.0-polygon.2"
UPD_VERSION_BROKEN_MIGRATE="1.0.0-polygon.3" UPD_VERSION_BROKEN_GATE="1.0.0-polygon.4"
UPD_VERSION_FLIPPED="1.0.0-polygon.5" UPD_VERSION_WRONG_KEY="1.0.0-polygon.6" UPD_VERSION_UNSIGNED="1.0.0-polygon.7"
# The FIXED release has the version of the gate-failing one on purpose (the retry replaces its directory).
UPD_VERSION_FIXED="$UPD_VERSION_BROKEN_GATE"
UPD_CHANNEL="beta"
UPD_SITE_DOMAIN="polyupdate.test"
UPD_SITE_MARKER=""
UPD_BUNDLES_IN="/root/update-bundles"
UPD_DUMP_DIR="/var/backups/maran-update"
UPD_WORK="" UPD_API_DIR="" UPD_AGENT_DIR="" UPD_FRONTEND_DIR="" UPD_SITE_OK="no"
UPD_FINGERPRINT="" UPD_STATUS=0 UPD_OUT="" UPD_LEGACY_NAME=""

# upd_expect: one scored check. $1 = what it asserts, $2 = observed, $3 = expected.
upd_expect() { recovery_expect "update: $1" "$2" "$3"; }

# upd_q: runs the command in the current container and prints its standard output; an absent file or
# a failed command is an empty answer, never a harness death.
upd_q() { docker exec "$container" "$@" 2>/dev/null | tr -d '\r' || true; }

# upd_t: runs the command in the current container and succeeds only when the command does, for the
# checks that are a yes or a no (upd_q cannot say: it always succeeds).
upd_t() { docker exec "$container" "$@" >/dev/null 2>&1; }

# upd_current_version: the version `current` resolves to, by the NAME of the directory it points at.
upd_current_version() { upd_q readlink /usr/local/maran/current | sed 's#.*/##'; }

# upd_state: the value of the key $1 in /etc/maran/release.state, empty when absent.
upd_state() { upd_q sed -n "s/^$1=//p" /etc/maran/release.state; }

# upd_dump_names: the pre-update dumps in the dump directory, sorted, one per line.
upd_dump_names() {
  { upd_q find "$UPD_DUMP_DIR" -maxdepth 1 -type f -name 'pre-*.dump' -printf '%f\n' || true; } | sort
}

# upd_dump_for: the newest dump whose name fits pre-<FROM>-to-<TO>-<UTC stamp>.dump exactly (the
# grammar of installer/cli/lib/update-dumps.sh, written out again here on purpose: the stage must not
# read its expectation out of the code it checks). Prints nothing when there is none.
#   $1 = FROM, $2 = TO.
upd_dump_for() {
  local prefix="pre-$1-to-$2-" name
  upd_dump_names | while IFS= read -r name; do
    if [[ "$name" == "$prefix"* && "${name#"$prefix"}" =~ ^[0-9]{8}T[0-9]{6}Z\.dump$ ]]; then echo "$name"; fi
  done | tail -n 1
}

# upd_fingerprint: one line summarising the seeded rows: count and a digest of the stable columns of
# the users, the accounts and the sites. Columns a sign-in or a restart rewrites are left out.
upd_fingerprint() {
  rec_psql "$(cat <<'SQL'
select (select count(*) || ':' || coalesce(md5(string_agg("Username" || '|' || coalesce("TotpSecret", ''), ',' order by "Username")), '-') from identity."Users")
    || ' ' || (select count(*) || ':' || coalesce(md5(string_agg("Name" || '|' || "PrimaryDomain", ',' order by "Name")), '-') from accounts."Accounts")
    || ' ' || (select count(*) || ':' || coalesce(md5(string_agg("Domain", ',' order by "Domain")), '-') from sites."Sites")
SQL
)"
}

# upd_later_objects: how many of the objects the legacy phase plants after the dump exist now: the
# schema zz_later_schema and the tables named zz_later_table in any schema (3 when all are present).
upd_later_objects() {
  rec_psql "select (select count(*) from pg_namespace where nspname = 'zz_later_schema') + (select count(*) from pg_class where relname = 'zz_later_table' and relkind = 'r')"
}

# upd_snapshot: everything a refused or rolled-back update must leave as it found it, as one text.
upd_snapshot() {
  printf 'current=%s\nstate=%s\nreleases=%s\ndumps=%s\ndata=%s\n' \
    "$(upd_current_version)" "$(upd_q cat /etc/maran/release.state | tr '\n' ';')" \
    "$(upd_q ls -1 /usr/local/maran/releases | tr '\n' ',')" "$(upd_dump_names | tr '\n' ',')" "$(upd_fingerprint)"
}

# upd_secret_leaks: how many of the panel's two key values appear in the text $1, or "blind" when a
# value cannot be read (a search for nothing finds nothing, which must not read as clean).
upd_secret_leaks() {
  local text="$1" name value hits=0
  for name in Security__EncryptionKey Jwt__SigningKey; do
    value="$(upd_q sed -n "s/^${name}=//p" /etc/maran/panel.env)"
    [ "${#value}" -ge 16 ] || { echo "blind"; return 0; }
    case "$text" in *"$value"*) hits=$((hits + 1)) ;; esac
  done
  echo "$hits"
}

# ---- the bundle builder (mirrors scripts/lib/release-bundle.sh; see the header) ----------------

# upd_make_key: a throwaway ECDSA P-384 key pair, the curve and digest the real key uses. $1 = name.
upd_make_key() {
  openssl ecparam -name secp384r1 -genkey -noout -out "${UPD_WORK}/$1.key" >/dev/null 2>&1 \
    && openssl pkey -in "${UPD_WORK}/$1.key" -pubout -out "${UPD_WORK}/$1.pub" >/dev/null 2>&1
}

# upd_collect_base: leaves the three real component trees in UPD_API_DIR, UPD_AGENT_DIR and
# UPD_FRONTEND_DIR: from the --bundle of an offline run, else from the server the install stage built
# (releases/<version>/, or the legacy directories). Returns 1 when one lacks its entry file.
upd_collect_base() {
  local base="${UPD_WORK}/base" component dir release
  install -d "$base"
  if [ "$mode" = "offline" ] && [ -n "$bundle" ]; then
    for component in api agent frontend; do
      install -d "${base}/${component}"
      tar -xzf "$bundle" -O "${component}.tar.gz" | tar -xzf - -C "${base}/${component}" || return 1
    done
  else
    docker cp "${origin_container}:/usr/local/maran/." "${base}/tree" >/dev/null || return 1
    for component in api agent frontend; do
      dir="${base}/tree/${component}"
      if [ -d "${base}/tree/releases" ]; then
        release="$(ls -1 "${base}/tree/releases" | head -n 1)"
        dir="${base}/tree/releases/${release}/${component}"
      fi
      install -d "${base}/${component}"
      cp -a "${dir}/." "${base}/${component}/" || return 1
    done
    rm -rf -- "${base}/tree"
  fi
  UPD_API_DIR="${base}/api"
  UPD_AGENT_DIR="${base}/agent"
  UPD_FRONTEND_DIR="${base}/frontend"
  [ -f "${UPD_API_DIR}/Maran.Host" ] && [ -f "${UPD_AGENT_DIR}/maran-agent" ] && [ -f "${UPD_FRONTEND_DIR}/index.html" ]
}

# upd_tar_dir: the directory $1's CONTENTS at the archive root, owned by nobody in particular.
upd_tar_dir() { tar --owner=0 --group=0 --numeric-owner -czf "$2" -C "$1" .; }

# upd_integrity_manifest: integrity-manifest.json for the stage $1, hashing the files of the three
# trees $2 (api), $3 (agent), $4 (frontend); $5 = version.
upd_integrity_manifest() {
  local pair component dir
  for pair in "api:$2" "agent:$3" "frontend:$4"; do
    component="${pair%%:*}"
    dir="${pair#*:}"
    ( cd "$dir" && find . -type f -print0 | xargs -0 sha256sum ) | sed "s#^\([0-9a-f]*\)  \./#${component}\t\1\t#"
  done | awk -F'\t' -v v="$5" '
    BEGIN { print "{"; print "  \"version\": \"" v "\","; print "  \"files\": {" }
    { printf "%s    \"%s/%s\": \"%s\"", (NR > 1 ? ",\n" : ""), $1, $3, $2 }
    END { print ""; print "  }"; print "}" }' > "${1}/integrity-manifest.json"
}

# upd_manifest: manifest.json for the stage $1, version $2: the shape release-bundle.sh writes, with
# the channel the CLI checks.
upd_manifest() {
  local stage="$1" version="$2" component first=1
  {
    echo "{"
    echo "  \"version\": \"${version}\","
    echo "  \"channel\": \"${UPD_CHANNEL}\","
    echo "  \"artifacts\": {"
    for component in api agent frontend; do
      [ "$first" -eq 1 ] || echo "    },"
      first=0
      echo "    \"${component}-${arch}\": {"
      echo "      \"url\": \"https://releases.maran.innovayse.com/${UPD_CHANNEL}/${component}-${arch}.tar.gz\","
      echo "      \"sha256\": \"$(sha256sum "${stage}/${component}.tar.gz" | awk '{print $1}')\""
    done
    echo "    }"
    echo "  }"
    echo "}"
  } > "${stage}/manifest.json"
}

# upd_flip_byte: changes the byte at offset 1000 of the file $1 to a different value.
upd_flip_byte() {
  local file="$1" original new
  original="$(dd if="$file" bs=1 skip=1000 count=1 2>/dev/null | od -An -tu1 | tr -d ' ')"
  new=$((original ^ 85))
  [ "$new" -ne 0 ] || new=1
  # shellcheck disable=SC2059
  printf "$(printf '\\x%02x' "$new")" | dd of="$file" bs=1 seek=1000 conv=notrunc 2>/dev/null
}

# upd_build_bundle: one offline tarball. $1 = output path, $2 = version, $3 = signing key name ("" for
# no signature), $4 = the api tree to ship, $5 = "flip" to change one byte of the frontend archive
# AFTER it was hashed and signed. The frontend carries a marker file naming the version, so the
# release the panel serves can be read over HTTP.
upd_build_bundle() {
  local out="$1" version="$2" key="$3" api_dir="$4" tamper="${5:-}" stage files
  stage="$(mktemp -d "${UPD_WORK}/stage.XXXXXX")"
  if [ "$api_dir" = "$UPD_API_DIR" ]; then
    cp "${UPD_WORK}/base-api.tar.gz" "${stage}/api.tar.gz"
  else
    upd_tar_dir "$api_dir" "${stage}/api.tar.gz"
  fi
  cp "${UPD_WORK}/base-agent.tar.gz" "${stage}/agent.tar.gz"
  cp -a "$UPD_FRONTEND_DIR" "${stage}/fe"
  printf 'polygon-release-marker: %s\n' "$version" > "${stage}/fe/polygon-release-marker.txt"
  upd_tar_dir "${stage}/fe" "${stage}/frontend.tar.gz"
  upd_manifest "$stage" "$version"
  upd_integrity_manifest "$stage" "$api_dir" "$UPD_AGENT_DIR" "${stage}/fe" "$version"
  files="manifest.json integrity-manifest.json api.tar.gz agent.tar.gz frontend.tar.gz"
  if [ -n "$key" ]; then
    openssl dgst -sha384 -sign "${UPD_WORK}/${key}.key" -out "${stage}/manifest.json.sig" "${stage}/manifest.json" \
      && openssl dgst -sha384 -sign "${UPD_WORK}/${key}.key" -out "${stage}/integrity-manifest.json.sig" "${stage}/integrity-manifest.json" \
      || return 1
    files="${files} manifest.json.sig integrity-manifest.json.sig"
  fi
  [ "$tamper" != "flip" ] || upd_flip_byte "${stage}/frontend.tar.gz"
  # shellcheck disable=SC2086
  tar -czf "$out" -C "$stage" $files || return 1
  rm -rf -- "$stage"
}

# upd_stub_api: a copy of the api tree whose Maran.Host is the shell script given on standard input.
#   $1 = directory to create.
upd_stub_api() {
  cp -al "$UPD_API_DIR" "$1" || return 1
  rm -f -- "${1}/Maran.Host"
  cat > "${1}/Maran.Host"
  chmod 0755 "${1}/Maran.Host"
}

# upd_bundle_verifies: "verifies" when the manifest signature inside the tarball $1 verifies against
# the public key file $2, else "does-not". The same openssl call the installer and the CLI make.
upd_bundle_verifies() {
  local dir answer=does-not
  dir="$(mktemp -d "${UPD_WORK}/check.XXXXXX")"
  if tar -xzf "$1" -C "$dir" manifest.json manifest.json.sig 2>/dev/null \
    && openssl dgst -sha384 -verify "$2" -signature "${dir}/manifest.json.sig" "${dir}/manifest.json" >/dev/null 2>&1; then
    answer=verifies
  fi
  rm -rf -- "$dir"
  echo "$answer"
}

# upd_bundle_archive_matches: "matches" when the sha256 of the archive $2 inside the tarball $1 is the
# one its manifest names for it, else "differs".
upd_bundle_archive_matches() {
  local dir expected actual answer=differs
  dir="$(mktemp -d "${UPD_WORK}/check.XXXXXX")"
  tar -xzf "$1" -C "$dir" 2>/dev/null || true
  expected="$(awk -v key="\"$2-${arch}\"" '$0 ~ key { f = 1 } f && /"sha256"/ { gsub(/[",[:space:]]|sha256|:/, ""); print; exit }' "${dir}/manifest.json")"
  actual="$(sha256sum "${dir}/$2.tar.gz" | awk '{print $1}')"
  [ -n "$expected" ] && [ "$expected" = "$actual" ] && answer=matches
  rm -rf -- "$dir"
  echo "$answer"
}

# upd_build_all: every bundle of the lane, in ${UPD_WORK}/bundles. Returns 1 naming the one that failed.
upd_build_all() {
  local dir="${UPD_WORK}/bundles" broken_gate
  install -d "$dir"
  upd_make_key key1 && upd_make_key key2 || { note "update: could not make the throwaway keys"; return 1; }
  upd_collect_base || { note "update: the real component trees could not be collected (api, agent and frontend, each with its entry file), so no bundle can be built"; return 1; }
  upd_tar_dir "$UPD_API_DIR" "${UPD_WORK}/base-api.tar.gz"
  upd_tar_dir "$UPD_AGENT_DIR" "${UPD_WORK}/base-agent.tar.gz"
  broken_gate="${UPD_WORK}/api-broken-gate"
  upd_build_bundle "${dir}/release-A.tar.gz" "$UPD_VERSION_A" key1 "$UPD_API_DIR" || { note "update: building release A failed"; return 1; }
  upd_build_bundle "${dir}/release-B.tar.gz" "$UPD_VERSION_B" key1 "$UPD_API_DIR" || { note "update: building release B failed"; return 1; }
  printf '#!/bin/sh\nexit 1\n' | upd_stub_api "${UPD_WORK}/api-broken-migrate" || return 1
  printf '#!/bin/sh\ncase "$1" in\n  --migrate) echo "polygon stub: nothing to migrate"; exit 0 ;;\nesac\nexit 1\n' | upd_stub_api "$broken_gate" || return 1
  upd_build_bundle "${dir}/release-broken-migrate.tar.gz" "$UPD_VERSION_BROKEN_MIGRATE" key1 "${UPD_WORK}/api-broken-migrate" || return 1
  upd_build_bundle "${dir}/release-broken-gate.tar.gz" "$UPD_VERSION_BROKEN_GATE" key1 "$broken_gate" || return 1
  upd_build_bundle "${dir}/release-fixed.tar.gz" "$UPD_VERSION_FIXED" key1 "$UPD_API_DIR" || return 1
  upd_build_bundle "${dir}/release-flipped.tar.gz" "$UPD_VERSION_FLIPPED" key1 "$UPD_API_DIR" flip || return 1
  upd_build_bundle "${dir}/release-wrong-key.tar.gz" "$UPD_VERSION_WRONG_KEY" key2 "$UPD_API_DIR" || return 1
  upd_build_bundle "${dir}/release-unsigned.tar.gz" "$UPD_VERSION_UNSIGNED" "" "$UPD_API_DIR" || return 1
  return 0
}

# upd_check_bundles_on_host: the builder's own controls, run before any container sees a bundle. Each
# refusal later is attributable to the CLI only if the bundle differs from release B in the one way
# named here and in no other.
upd_check_bundles_on_host() {
  local dir="${UPD_WORK}/bundles" production="${root}/installer/keys/release-signing.pub"
  upd_expect "host: release A verifies against the throwaway key" "$(upd_bundle_verifies "${dir}/release-A.tar.gz" "${UPD_WORK}/key1.pub")" verifies
  upd_expect "host: release A does NOT verify against the other throwaway key (control)" "$(upd_bundle_verifies "${dir}/release-A.tar.gz" "${UPD_WORK}/key2.pub")" does-not
  upd_expect "host: release A does NOT verify against the production key (the lane trusts no production key)" "$(upd_bundle_verifies "${dir}/release-A.tar.gz" "$production")" does-not
  upd_expect "host: the wrong-key bundle verifies against ITS key" "$(upd_bundle_verifies "${dir}/release-wrong-key.tar.gz" "${UPD_WORK}/key2.pub")" verifies
  upd_expect "host: the wrong-key bundle does not verify against the key the CLI will trust" "$(upd_bundle_verifies "${dir}/release-wrong-key.tar.gz" "${UPD_WORK}/key1.pub")" does-not
  upd_expect "host: the flipped bundle's manifest still verifies, so only the artifact can refuse it" "$(upd_bundle_verifies "${dir}/release-flipped.tar.gz" "${UPD_WORK}/key1.pub")" verifies
  upd_expect "host: the flipped bundle's frontend archive no longer matches its manifest" "$(upd_bundle_archive_matches "${dir}/release-flipped.tar.gz" frontend)" differs
  upd_expect "host: release B's frontend archive matches its manifest (control)" "$(upd_bundle_archive_matches "${dir}/release-B.tar.gz" frontend)" matches
  upd_expect "host: the unsigned bundle carries no signature" "$(upd_bundle_verifies "${dir}/release-unsigned.tar.gz" "${UPD_WORK}/key1.pub")" does-not
  upd_expect "host: the manifest names the channel" \
    "$(tar -xzf "${dir}/release-B.tar.gz" -O manifest.json | grep -c "\"channel\": \"${UPD_CHANNEL}\"" || true)" 1
  upd_expect "host: the outer tarball lists its members by name (no './' member, like a real bundle)" \
    "$(tar -tzf "${dir}/release-B.tar.gz" | grep -c '^\./' || true)/$(tar -tzf "${dir}/release-B.tar.gz" | grep -c .)" "0/7"
  upd_check_channel_is_signed "${dir}/release-B.tar.gz"
}

# upd_check_channel_is_signed: the signature covers the channel. The manifest of the tarball $1
# verifies as it is (the control) and does NOT once its channel is edited to the other one.
upd_check_channel_is_signed() {
  local work answer=""
  work="$(mktemp -d "${UPD_WORK}/check.XXXXXX")"
  tar -xzf "$1" -C "$work" manifest.json manifest.json.sig
  answer="$(openssl dgst -sha384 -verify "${UPD_WORK}/key1.pub" -signature "${work}/manifest.json.sig" "${work}/manifest.json" >/dev/null 2>&1 && echo verifies || echo does-not)"
  sed -i "s/\"channel\": \"${UPD_CHANNEL}\"/\"channel\": \"stable\"/" "${work}/manifest.json"
  answer+="/$(openssl dgst -sha384 -verify "${UPD_WORK}/key1.pub" -signature "${work}/manifest.json.sig" "${work}/manifest.json" >/dev/null 2>&1 && echo verifies || echo does-not)"
  upd_expect "host: the manifest verifies as built and NOT once its channel is edited after signing (the channel is signed)" "$answer" verifies/does-not
  rm -rf -- "$work"
}

# ---- the server -------------------------------------------------------------------------------

# upd_install_release_a: the offline install of release A into container $1, log in $2. The package
# copy inside the container gets the throwaway public key (the repository's file is untouched), then
# the harness copies it over the CLI's baked path as the lane's trust anchor. The bundles are made
# root:root, directory 0755 and files 0644, so the CLI's tarball checks pass for a stated reason.
upd_install_release_a() {
  local name="$1" logfile="$2" status=0
  {
    docker exec "$name" rm -rf /root/installer \
      && docker cp "${root}/installer" "${name}:/root/installer" >/dev/null \
      && docker cp "${UPD_WORK}/key1.pub" "${name}:/root/installer/keys/release-signing.pub" >/dev/null \
      && docker exec "$name" chmod 0644 /root/installer/keys/release-signing.pub \
      && docker exec "$name" mkdir -p "$UPD_BUNDLES_IN" \
      && docker cp "${UPD_WORK}/bundles/." "${name}:${UPD_BUNDLES_IN}/" >/dev/null \
      && docker exec "$name" bash -c "chown -R root:root ${UPD_BUNDLES_IN} && chmod 0755 ${UPD_BUNDLES_IN} && chmod 0644 ${UPD_BUNDLES_IN}/*.tar.gz"
  } || note "update: could not copy the installer package and the bundles into ${name}"
  docker exec "$name" bash -c \
    "cd /root/installer && bash install.sh --offline-tarball ${UPD_BUNDLES_IN}/release-A.tar.gz --channel ${UPD_CHANNEL}" \
    >"$logfile" 2>&1 || status=$?
  upd_expect "release A installs offline (exit 0, log: ${logfile#"$root"/})" "$status" 0
  { docker cp "${UPD_WORK}/key1.pub" "${name}:/usr/local/maran/cli/keys/release-signing.pub" >/dev/null \
      && docker exec "$name" chown root:root /usr/local/maran/cli/keys/release-signing.pub \
      && docker exec "$name" chmod 0644 /usr/local/maran/cli/keys/release-signing.pub
  } || note "update: could not put the throwaway key at the CLI's baked key path"
}

# upd_assert_bundle_trust: the two facts the CLI demands of an offline tarball, asked of the container
# (not assumed from the chown above): directory and file are root-owned with no group or world write
# bit. A world-writable directory (/tmp) reads as untrusted by the same probe (the control).
upd_assert_bundle_trust() {
  local probe='o=$(stat -c %u "$1"); m=$(stat -c %a "$1"); [ "$o" = 0 ] && [ $((8#$m & 8#022)) -eq 0 ] && echo trusted || echo "untrusted:$o:$m"'
  upd_expect "the bundle directory ${UPD_BUNDLES_IN} and file release-B.tar.gz are root-owned, not group/world writable" \
    "$(upd_q bash -c "$probe" _ "$UPD_BUNDLES_IN")/$(upd_q bash -c "$probe" _ "${UPD_BUNDLES_IN}/release-B.tar.gz")" trusted/trusted
  upd_expect "a world-writable directory (/tmp) reads as untrusted (control for the probe)" \
    "$(upd_q bash -c "$probe" _ /tmp | cut -d: -f1)" untrusted
}

# upd_assert_layout: the layout a fresh install leaves and the key the CLI trusts.
upd_assert_layout() {
  local link
  upd_expect "the baked key is the throwaway key, 0644 root" \
    "$(upd_q sha256sum /usr/local/maran/cli/keys/release-signing.pub | cut -d' ' -f1)/$(upd_q stat -c '%a %U' /usr/local/maran/cli/keys/release-signing.pub)" \
    "$(sha256sum "${UPD_WORK}/key1.pub" | cut -d' ' -f1)/644 root"
  upd_expect "current points at release A" "$(upd_current_version)" "$UPD_VERSION_A"
  upd_expect "releases/A is a real directory" "$(upd_q bash -c "[ -d /usr/local/maran/releases/${UPD_VERSION_A} ] && [ ! -L /usr/local/maran/releases/${UPD_VERSION_A} ] && echo real || echo not")" real
  for link in api agent frontend; do
    upd_expect "/usr/local/maran/${link} is a symlink (into current)" "$(upd_q bash -c "[ -L /usr/local/maran/${link} ] && echo link || echo not")" link
  done
  upd_expect "release.state names VERSION=A" "$(upd_state VERSION)" "$UPD_VERSION_A"
  upd_expect "release.state names CHANNEL=${UPD_CHANNEL} (the channel the signed manifest declares)" "$(upd_state CHANNEL)" "$UPD_CHANNEL"
  upd_expect "release.state: a fresh install has an empty PREVIOUS" "$(upd_q grep -c '^PREVIOUS=$' /etc/maran/release.state)" 1
  upd_expect "release.state is 0644 root" "$(upd_q stat -c '%a %U' /etc/maran/release.state)" "644 root"
}

# upd_assert_healthy: $1 = label. Both units active, /health/ready 200 on the api socket, and the
# panel's own account of itself through nginx.
upd_assert_healthy() {
  local label="$1" unit health
  wait_for_panel || note "update: ${label}: the panel did not report a reachable database"
  for unit in maran-agent maran-api; do
    upd_expect "${label}: ${unit} is active" "$(upd_q systemctl is-active "$unit")" active
  done
  upd_expect "${label}: /health/ready answers 200 on the api socket" \
    "$(upd_q curl -s -o /dev/null -w '%{http_code}' --max-time 10 --unix-socket /run/maran-api/api.sock http://localhost/health/ready)" 200
  health="$(upd_q curl -sk --max-time 10 https://127.0.0.1:8443/health)"
  upd_expect "${label}: /health reports the agent connected and the database reachable" \
    "$(case "$health" in *'"agent":"connected"'*'"database":"reachable"'*|*'"database":"reachable"'*'"agent":"connected"'*) echo ok ;; *) echo "$health" ;; esac)" ok
}

# upd_assert_runs: $1 = label, $2 = release name (a version, or legacy-<UTC>), $3 = the version whose
# marker the frontend carries (default $2; an adopted release carries the converted release's). The
# REAL executable of each daemon (from /proc, so a swap that restarted nothing shows the old path)
# and the marker the frontend serves.
upd_assert_runs() {
  local label="$1" version="$2" marker="${3:-$2}" unit pid pair
  for pair in maran-api:api/Maran.Host maran-agent:agent/maran-agent; do
    unit="${pair%%:*}"
    pid="$(upd_q systemctl show -p MainPID --value "$unit")"
    upd_expect "${label}: ${unit} runs the binary of release ${version}" \
      "$(case "$pid" in ''|0|*[!0-9]*) echo "no-pid" ;; *) upd_q readlink "/proc/${pid}/exe" ;; esac)" \
      "/usr/local/maran/releases/${version}/${pair#*:}"
  done
  upd_expect "${label}: the frontend nginx serves is release ${version}'s" \
    "$(upd_q curl -sk --max-time 10 https://127.0.0.1:8443/polygon-release-marker.txt)" "polygon-release-marker: ${marker}"
}

# upd_site_body: what the static site answers for /polygon-site.txt under the Host header $1.
upd_site_body() { upd_q curl -s --max-time 10 -H "Host: $1" http://127.0.0.1/polygon-site.txt; }

# upd_assert_data: $1 = label. The seeded rows unchanged, the administrator's second factor still
# decrypting, the account and the site listed, the owner able to sign in, and the site still served.
upd_assert_data() {
  local label="$1" token
  upd_expect "${label}: the seeded rows are unchanged (users, accounts, sites)" "$(upd_fingerprint)" "$UPD_FINGERPRINT"
  rec_login "$REC_ADMIN_USER" "$REC_ADMIN_PASSWORD"
  upd_expect "${label}: the administrator's password alone asks for the second factor" \
    "$REC_CODE/$([ -z "$REC_TOKEN" ] && echo no-session || echo session)" "200/no-session"
  rec_second_factor "$REC_ADMIN_USER" "$REC_ADMIN_PASSWORD" "$REC_TOTP_SECRET"
  upd_expect "${label}: the code of the original secret signs the administrator in (the encrypted column still decrypts)" \
    "$REC_CODE/$([ -n "$REC_TOKEN" ] && echo session || echo no-session)" "200/session"
  token="$REC_TOKEN"
  rec_call GET /api/v1/accounts "" "$token"
  upd_expect "${label}: the administrator sees the account" \
    "$(case "$REC_BODY" in *"\"name\":\"${REC_ACCOUNT_NAME}\""*) echo listed ;; *) echo missing ;; esac)" listed
  rec_login "$REC_ACCOUNT_NAME" "$REC_OWNER_PASSWORD"
  upd_expect "${label}: the account owner signs in with the password they chose" \
    "$REC_CODE/$([ -n "$REC_TOKEN" ] && echo session || echo no-session)" "200/session"
  if [ "$UPD_SITE_OK" = "yes" ]; then
    rec_call GET /api/v1/sites "" "$token"
    upd_expect "${label}: the site is listed" \
      "$(case "$REC_BODY" in *"\"${UPD_SITE_DOMAIN}\""*) echo listed ;; *) echo missing ;; esac)" listed
    upd_expect "${label}: the site still serves its file" "$(upd_site_body "$UPD_SITE_DOMAIN")" "$UPD_SITE_MARKER"
    upd_expect "${label}: another host name does not get the site's file (control)" \
      "$([ "$(upd_site_body "other.${UPD_SITE_DOMAIN}")" = "$UPD_SITE_MARKER" ] && echo served || echo not-served)" not-served
  fi
}

# upd_seed: the data the update must keep: rec_seed_panel's plan, account, owner and second factor,
# then a static site with a file it serves. Returns 1 when the panel cannot be driven.
upd_seed() {
  local account_id root_dir
  rec_seed_panel || return 1
  account_id="$(rec_psql "select \"Id\" from accounts.\"Accounts\" where \"Name\" = '${REC_ACCOUNT_NAME}'")"
  upd_expect "the seeded account has an id the site can be created under" "$([ -n "$account_id" ] && echo found || echo missing)" found
  UPD_FINGERPRINT="$(upd_fingerprint)"
  rec_call POST /api/v1/sites "{\"accountId\":\"${account_id}\",\"domain\":\"${UPD_SITE_DOMAIN}\",\"backendType\":1}" "$REC_ADMIN_TOKEN"
  upd_expect "a static site is created through the API" "${REC_CODE:0:1}xx" 2xx
  root_dir="$(docker exec "$container" nginx -T 2>/dev/null | tr -d '\r' | awk -v d="$UPD_SITE_DOMAIN" '
    $1 == "server_name" { for (i = 2; i <= NF; i++) { n = $i; sub(/;$/, "", n); if (n == d) found = 1 } }
    found && $1 == "root" { r = $2; sub(/;$/, "", r); print r; exit }
    /^}/ { found = 0 }' || true)"
  if [ -z "$root_dir" ]; then
    note "update: nginx -T names no document root for ${UPD_SITE_DOMAIN}, so the site cannot be observed across the update"
    return 0
  fi
  UPD_SITE_MARKER="polygon-site-$(openssl rand -hex 8)"
  { printf '%s\n' "$UPD_SITE_MARKER" | rec_put_file "$container" 0644 "${root_dir}/polygon-site.txt" \
      && docker exec "$container" chgrp "$web_group" "${root_dir}/polygon-site.txt"
  } || note "update: could not write the site's file under ${root_dir}"
  if [ "$(upd_site_body "$UPD_SITE_DOMAIN")" = "$UPD_SITE_MARKER" ]; then
    UPD_SITE_OK="yes"
  else
    note "update: the new site does not serve its file before any update, so nothing about the site is observed across it"
  fi
  upd_expect "the site's file is served before the update (control for every later check)" "$UPD_SITE_OK" yes
  upd_expect "the fingerprint of the seeded rows changed when the site was added (it observes the sites table)" \
    "$([ "$(upd_fingerprint)" != "$UPD_FINGERPRINT" ] && echo differs || echo same)" differs
  UPD_FINGERPRINT="$(upd_fingerprint)"
  upd_expect "the fingerprint has the shape the comparisons rely on (at least two users, an account, a site)" \
    "$([[ "$UPD_FINGERPRINT" =~ ^([0-9]+):[0-9a-f]{32}\ ([0-9]+):[0-9a-f]{32}\ ([0-9]+):[0-9a-f]{32}$ ]] \
       && [ "${BASH_REMATCH[1]}" -ge 2 ] && [ "${BASH_REMATCH[2]}" -ge 1 ] && [ "${BASH_REMATCH[3]}" -ge 1 ] && echo ok || echo "$UPD_FINGERPRINT")" ok
  return 0
}

# ---- the verbs --------------------------------------------------------------------------------
# upd_run: `maran update --offline-tarball <bundle> <args>`; the default argument is --yes, and a
# lone "-" means no further argument at all. $1 is a bundle name under the bundle directory, or an
# absolute path. Sets UPD_STATUS, UPD_OUT.
#   $1 = bundle, the rest = arguments (default: --yes).
upd_run() {
  local path
  case "$1" in /*) path="$1" ;; *) path="${UPD_BUNDLES_IN}/$1" ;; esac
  shift
  if [ "$#" -eq 0 ]; then set -- --yes; elif [ "$1" = "-" ]; then shift; fi
  UPD_STATUS=0
  UPD_OUT="$(docker exec "$container" timeout 1500 /usr/local/sbin/maran update \
    --offline-tarball "$path" "$@" 2>&1 | tr -d '\r')" || UPD_STATUS=$?
}

# upd_says: "says-so" when the last update's (or $2's) output contains the fixed text $1, else "silent".
#   $1 = text, $2 = output (default: UPD_OUT).
upd_says() {
  if grep -qF -- "$1" <<<"${2-$UPD_OUT}"; then echo says-so; else echo silent; fi
}

# upd_show_output: prints the last command's output when $1 is not "ok", so a failure carries its reason.
upd_show_output() {
  [ "$1" = "ok" ] || printf '%s\n' "$UPD_OUT" | sed 's/^/      update: /' >&2
}

# upd_phase_check: `maran update --check` against B and against A: reports, exits 0, changes nothing.
upd_phase_check() {
  local before after text
  before="$(upd_snapshot)"
  upd_run release-B.tar.gz --check
  after="$(upd_snapshot)"
  upd_expect "--check against B exits 0 and changed nothing" "${UPD_STATUS}/$([ "$before" = "$after" ] && echo unchanged || echo changed)" 0/unchanged
  for text in "current: ${UPD_VERSION_A}" "available: ${UPD_VERSION_B}" "channel: ${UPD_CHANNEL}" \
    "Update available: ${UPD_VERSION_B} is newer than ${UPD_VERSION_A}."; do
    upd_expect "--check prints '${text}'" "$(upd_says "$text")" says-so
  done
  upd_run release-A.tar.gz --check
  upd_expect "--check against the installed version exits 0 and says there is no update" \
    "${UPD_STATUS}/$(upd_says "No update: the installed release is already ${UPD_VERSION_A}.")" 0/says-so
}

# upd_unchanged: "unchanged" when the two snapshots $1 and $2 are equal, else "changed".
upd_unchanged() { if [ "$1" = "$2" ]; then echo unchanged; else echo changed; fi; }

# upd_journal: "logged" when the maran-cli journal holds a line containing the fixed text $1.
upd_journal() { if [ "$(rec_journal_matches "$1")" -ge 1 ]; then echo logged; else echo silent; fi; }

# upd_rollback: runs `maran rollback <args>` and sets UPD_STATUS and UPD_OUT.
upd_rollback() {
  UPD_STATUS=0
  UPD_OUT="$(docker exec "$container" timeout 600 /usr/local/sbin/maran rollback "$@" 2>&1 | tr -d '\r')" || UPD_STATUS=$?
}

# upd_phase_update: the update to release B and everything it must leave behind.
upd_phase_update() {
  local dump dumps_before
  dumps_before="$(rec_lines "$(upd_dump_names)")"
  upd_run release-B.tar.gz
  upd_expect "maran update to B exits 0" "$UPD_STATUS" 0
  upd_show_output "$([ "$UPD_STATUS" -eq 0 ] && echo ok || echo failed)"
  upd_expect "the update says it succeeded and names both releases" \
    "$(upd_says "Panel updated: ${UPD_VERSION_A} -> ${UPD_VERSION_B} (${UPD_CHANNEL}); the health gate passed.")" says-so
  upd_expect "the update's output prints no panel key" "$(upd_secret_leaks "$UPD_OUT")" 0
  upd_expect "the leak search finds a planted key value (control)" \
    "$(upd_secret_leaks "planted $(upd_q sed -n 's/^Security__EncryptionKey=//p' /etc/maran/panel.env) planted")" 1
  upd_expect "current is B; release.state: VERSION=B, PREVIOUS=A, CHANNEL kept" \
    "$(upd_current_version)/$(upd_state VERSION)/$(upd_state PREVIOUS)/$(upd_state CHANNEL)" \
    "${UPD_VERSION_B}/${UPD_VERSION_B}/${UPD_VERSION_A}/${UPD_CHANNEL}"
  dump="$(upd_dump_for "$UPD_VERSION_A" "$UPD_VERSION_B")"
  upd_expect "a dump named pre-A-to-B-<UTC>.dump exists and the output names it" \
    "$([ -n "$dump" ] && echo found || echo missing)/$(upd_says "${UPD_DUMP_DIR}/${dump}")" found/says-so
  upd_expect "the dump is 0600 root and not empty, in a 0700 root directory" \
    "$(upd_q stat -c '%a %U' "${UPD_DUMP_DIR}/${dump}")/$([ "$(upd_q stat -c '%s' "${UPD_DUMP_DIR}/${dump}")" -gt 0 ] 2>/dev/null && echo nonempty || echo empty)/$(upd_q stat -c '%a %U' "$UPD_DUMP_DIR")" \
    "600 root/nonempty/700 root"
  upd_expect "exactly one dump was added" "$(rec_lines "$(upd_dump_names)")" "$((dumps_before + 1))"
  upd_expect "the journal carries a maran-cli line for the update" "$(upd_journal 'verb=update')" logged
  upd_assert_runs "after the update" "$UPD_VERSION_B"
  upd_assert_healthy "after the update"
  upd_assert_data "after the update"
}

# upd_phase_rollback: `maran rollback --yes` and a second one that must be refused.
upd_phase_rollback() {
  local before after
  upd_rollback --yes
  upd_expect "maran rollback exits 0" "$UPD_STATUS" 0
  [ "$UPD_STATUS" -eq 0 ] || printf '%s\n' "$UPD_OUT" | sed 's/^/      rollback: /' >&2
  upd_expect "the rollback says where it went and that the database was left as it is" \
    "$(upd_says "Rolled back from ${UPD_VERSION_B} to ${UPD_VERSION_A}.")/$(upd_says "The database was left as it is")" says-so/says-so
  upd_expect "current is A; release.state: VERSION=A, CHANNEL kept" \
    "$(upd_current_version)/$(upd_state VERSION)/$(upd_state CHANNEL)" "${UPD_VERSION_A}/${UPD_VERSION_A}/${UPD_CHANNEL}"
  upd_expect "release.state: PREVIOUS is written EMPTY (a second rollback must not swap the releases back)" \
    "$(upd_q grep -c '^PREVIOUS=$' /etc/maran/release.state)" 1
  upd_expect "the journal carries the rollback" "$(upd_journal 'verb=rollback outcome=rolled-back')" logged
  upd_assert_runs "after the rollback" "$UPD_VERSION_A"
  upd_assert_healthy "after the rollback"
  upd_assert_data "after the rollback"
  before="$(upd_snapshot)"
  upd_rollback --yes
  after="$(upd_snapshot)"
  upd_expect "a second rollback, with no release to return to, is refused (exit 1) and says why" \
    "${UPD_STATUS}/$(upd_says "there is no previous release to roll back to")" 1/says-so
  upd_expect "the refused rollback changed nothing" "$(upd_unchanged "$before" "$after")" unchanged
}

# upd_phase_broken: $1 = label, $2 = bundle file. The release cannot run; the update must say it rolled
# back and leave A running with the data it found.
upd_phase_broken() {
  local label="$1" dumps_before current_before state_before text
  dumps_before="$(rec_lines "$(upd_dump_names)")"
  current_before="$(upd_current_version)"
  state_before="$(upd_state VERSION)"
  upd_run "$2"
  upd_expect "${label}: maran update exits 1" "$UPD_STATUS" 1
  upd_show_output "$(upd_says "was rolled back" | sed 's/says-so/ok/')"
  for text in "was rolled back: ${current_before} is running again." "the database was NOT restored" \
    "the pre-update dump is ${UPD_DUMP_DIR}/pre-${current_before}-to-" "maran rollback --restore-database"; do
    upd_expect "${label}: it says '${text}'" "$(upd_says "$text")" says-so
  done
  upd_expect "${label}: the update got as far as its dump (so the failure is the release, not an early refusal)" \
    "$(rec_lines "$(upd_dump_names)")" "$((dumps_before + 1))"
  upd_expect "${label}: current and VERSION are the release it was; after an AUTOMATIC rollback PREVIOUS=VERSION" \
    "$(upd_current_version)/$(upd_state VERSION)/$(upd_state PREVIOUS)" "${current_before}/${state_before}/${state_before}"
  upd_expect "${label}: the journal carries update outcome=rolled-back" "$(upd_journal 'verb=update outcome=rolled-back')" logged
  upd_assert_runs "${label}" "$UPD_VERSION_A"
  upd_assert_healthy "${label}"
  upd_assert_data "${label}"
}

# upd_phase_refusal: an update the CLI must refuse (or, for the installed version, leave alone) and
# change nothing for.
#   $1 = label, $2 = bundle (name or absolute path), $3 = the exit status expected, $4 = a fixed text
#   the output must contain, the rest = arguments to `maran update` (default: --yes; "-": none).
upd_phase_refusal() {
  local label="$1" bundle="$2" expected_status="$3" text="$4" before after
  shift 4
  before="$(upd_snapshot)"
  upd_run "$bundle" "$@"
  after="$(upd_snapshot)"
  upd_expect "${label}: maran update exits ${expected_status} and says '${text}'" "${UPD_STATUS}/$(upd_says "$text")" "${expected_status}/says-so"
  upd_show_output "$(upd_says "$text" | sed 's/says-so/ok/')"
  upd_expect "${label}: current, release.state, the releases, the dumps and the data are exactly as they were" \
    "$(upd_unchanged "$before" "$after")" unchanged
  upd_assert_runs "${label}" "$UPD_VERSION_A"
  upd_assert_healthy "${label}"
}

# upd_phase_refusals: every refusal that must change NOTHING, each with the text it prints. The
# tarball in a world-writable directory is the same release B that updates cleanly from /root, so the
# directory is the only difference (an inverse control by construction).
upd_phase_refusals() {
  local before after
  upd_phase_refusal "the installed version again" release-A.tar.gz 0 "Already on ${UPD_VERSION_A}; nothing to do."
  upd_phase_refusal "an artifact with one byte flipped" release-flipped.tar.gz 1 "checksum mismatch for frontend.tar.gz"
  upd_phase_refusal "a manifest signed by another key" release-wrong-key.tar.gz 1 "manifest signature verification FAILED"
  upd_phase_refusal "a bundle with no signature" release-unsigned.tar.gz 1 "manifest.json or manifest.json.sig is missing"
  upd_t cp "${UPD_BUNDLES_IN}/release-B.tar.gz" /tmp/release-B.tar.gz || note "update: could not copy release B into /tmp"
  upd_phase_refusal "a bundle in a world-writable directory" /tmp/release-B.tar.gz 1 "holding the offline tarball must not be group or world writable"
  upd_t rm -f /tmp/release-B.tar.gz
  upd_phase_refusal "no --yes and no terminal" release-B.tar.gz 1 "this is not a terminal: pass --yes" -
  upd_phase_refusal "--offline-tarball with --channel" release-B.tar.gz 64 "cannot be combined" --channel stable --yes
  # After two automatic rollbacks release.state holds PREVIOUS=VERSION=A: a plain rollback has nothing
  # to switch to, says so, names --restore-database and changes nothing.
  before="$(upd_snapshot)"
  upd_rollback --yes
  after="$(upd_snapshot)"
  upd_expect "a plain rollback after an automatic one is refused (exit 1), says the update rolled back by itself and names --restore-database" \
    "${UPD_STATUS}/$(upd_says "the update rolled back by itself")/$(upd_says "maran rollback --restore-database")/$(upd_unchanged "$before" "$after")" 1/says-so/says-so/unchanged
}

# upd_api_sum: the sha256 of the api binary of the release $1, empty when it is absent.
upd_api_sum() { upd_q sha256sum "/usr/local/maran/releases/$1/api/Maran.Host" | cut -d' ' -f1; }

# upd_phase_retry: the update to the FIXED release, which has the version the gate-failing one had.
# The directory the failed update abandoned is replaced (its broken api is gone afterwards), the
# update succeeds, and the dump directory keeps its 3 newest dumps by the stamp in the NAME.
upd_phase_retry() {
  local abandoned real names
  abandoned="$(upd_api_sum "$UPD_VERSION_FIXED")"
  real="$(upd_api_sum "$UPD_VERSION_A")"
  upd_expect "before the retry the abandoned release holds the broken api, and 3 dumps exist (controls)" \
    "$([ -n "$abandoned" ] && [ "$abandoned" != "$real" ] && echo broken || echo "unexpected:${abandoned}")/$(rec_lines "$(upd_dump_names)")" broken/3
  upd_run release-fixed.tar.gz
  upd_expect "the retry exits 0 and says it updated A to the fixed release" \
    "${UPD_STATUS}/$(upd_says "Panel updated: ${UPD_VERSION_A} -> ${UPD_VERSION_FIXED} (${UPD_CHANNEL}); the health gate passed.")" 0/says-so
  upd_show_output "$([ "$UPD_STATUS" -eq 0 ] && echo ok || echo failed)"
  upd_expect "current is the fixed release; release.state: VERSION=fixed, PREVIOUS=A" \
    "$(upd_current_version)/$(upd_state VERSION)/$(upd_state PREVIOUS)" "${UPD_VERSION_FIXED}/${UPD_VERSION_FIXED}/${UPD_VERSION_A}"
  upd_expect "the abandoned directory was REPLACED (its api is now the working binary) and no .partial is left" \
    "$(upd_api_sum "$UPD_VERSION_FIXED")/$(upd_q find /usr/local/maran/releases -maxdepth 1 -name '*.partial' | grep -c . || true)" "${real}/0"
  upd_assert_runs "after the retry" "$UPD_VERSION_FIXED"
  upd_assert_healthy "after the retry"
  upd_assert_data "after the retry"
  names="$(upd_dump_names)"
  upd_expect "the dump directory keeps exactly its 3 newest dumps; the oldest (A to B) was pruned" \
    "$(rec_lines "$names")/$(upd_dump_for "$UPD_VERSION_A" "$UPD_VERSION_B")" "3/"
  upd_expect "both dumps of the update to the fixed version and the one of the broken-migrate release are kept" \
    "$(grep -c "^pre-${UPD_VERSION_A}-to-${UPD_VERSION_FIXED}-" <<<"$names" || true)/$(upd_dump_for "$UPD_VERSION_A" "$UPD_VERSION_BROKEN_MIGRATE" | grep -c . || true)" 2/1
}

# upd_phase_legacy: a host installed by the old layout, built by converting THIS installed tree back
# to real directories (the CLI, its key, the data and the units stay). `maran update` adopts it into
# releases/legacy-<UTC>; the dump carries that name as the release left; `maran rollback
# --restore-database --yes` returns to it and restores that dump, discarding a write made after the
# update. A restore without --yes and without a terminal is refused first and changes nothing.
upd_phase_legacy() {
  local dump before after
  docker exec "$container" bash -c 'set -e; systemctl stop maran-api maran-agent; cd /usr/local/maran
release="$(readlink current)"; release="${release#releases/}"
for c in api agent frontend; do rm -f "$c"; cp -a "releases/${release}/${c}" "$c"; done
rm -f current; rm -rf releases /etc/maran/release.state; systemctl start maran-agent maran-api' >/dev/null 2>&1 || note "update: could not convert the installed tree to the pre-layout shape"
  upd_expect "the converted host: three real component directories, no current, no releases, no release.state" \
    "$(upd_q bash -c 'n=0; for c in api agent frontend; do [ -d /usr/local/maran/$c ] && [ ! -L /usr/local/maran/$c ] && n=$((n+1)); done
      for p in /usr/local/maran/current /usr/local/maran/releases /etc/maran/release.state; do if [ -e $p ] || [ -L $p ]; then n=$((n+10)); fi; done; echo $n')" 3
  upd_assert_healthy "the converted (legacy) host"
  upd_run release-B.tar.gz
  upd_expect "maran update on a legacy host exits 0" "$UPD_STATUS" 0
  upd_show_output "$([ "$UPD_STATUS" -eq 0 ] && echo ok || echo failed)"
  UPD_LEGACY_NAME="$(upd_q ls -1 /usr/local/maran/releases | grep -E '^legacy-[0-9]{8}T[0-9]{6}Z$' | head -n 1 || true)"
  upd_expect "the legacy tree was adopted into releases/legacy-<UTC stamp>, with its three components as real directories" \
    "$([ -n "$UPD_LEGACY_NAME" ] && echo adopted || echo missing)/$(upd_q bash -c "n=0; for c in api agent frontend; do [ -d /usr/local/maran/releases/${UPD_LEGACY_NAME}/\$c ] && [ ! -L /usr/local/maran/releases/${UPD_LEGACY_NAME}/\$c ] && n=\$((n+1)); done; echo \$n")" adopted/3
  upd_expect "current is B, the static links are links again, release.state: VERSION=B, PREVIOUS=the adopted name, CHANNEL" \
    "$(upd_current_version)/$(upd_q bash -c 'n=0; for c in api agent frontend; do [ -L /usr/local/maran/$c ] && n=$((n+1)); done; echo $n')/$(upd_state VERSION)/$(upd_state PREVIOUS)/$(upd_state CHANNEL)" \
    "${UPD_VERSION_B}/3/${UPD_VERSION_B}/${UPD_LEGACY_NAME}/${UPD_CHANNEL}"
  dump="$(upd_dump_for "$UPD_LEGACY_NAME" "$UPD_VERSION_B")"
  upd_expect "the dump is named for the ADOPTED release (pre-<legacy-name>-to-B-<UTC>.dump) and the output names it" \
    "$([ -n "$dump" ] && echo found || echo missing)/$(upd_says "${UPD_DUMP_DIR}/${dump}")" found/says-so
  upd_assert_runs "after the legacy update" "$UPD_VERSION_B"
  upd_assert_healthy "after the legacy update"
  upd_assert_data "after the legacy update"
  # A write made after the update: the account's primary domain, which the fingerprint covers.
  rec_psql "update accounts.\"Accounts\" set \"PrimaryDomain\" = 'polyrestore.test' where \"Name\" = '${REC_ACCOUNT_NAME}'" >/dev/null
  upd_expect "the write made after the update is visible in the fingerprint (control for the restore)" \
    "$([ "$(upd_fingerprint)" != "$UPD_FINGERPRINT" ] && echo differs || echo same)" differs
  # What a newer release's migrations leave behind: a schema and a table in it, and a table in an
  # existing schema, owned by the service role as migrations make them. Neither is in the dump, so
  # `pg_restore --clean` alone cannot remove them and refused the restore (issue #386).
  rec_psql "set role maran; create schema zz_later_schema; create table zz_later_schema.zz_later_table (id int); create table accounts.zz_later_table (id int)" >/dev/null
  upd_expect "a schema and two tables absent from the dump exist before the restore (control: the probe finds them)" "$(upd_later_objects)" 3
  before="$(upd_snapshot)"
  upd_rollback --restore-database
  after="$(upd_snapshot)"
  upd_expect "--restore-database with no --yes and no terminal is refused (exit 1), says it needs --yes, and changed nothing" \
    "${UPD_STATUS}/$(upd_says "--restore-database needs --yes")/$(upd_unchanged "$before" "$after")" 1/says-so/unchanged
  upd_rollback --restore-database --yes
  upd_expect "maran rollback --restore-database --yes exits 0" "$UPD_STATUS" 0
  [ "$UPD_STATUS" -eq 0 ] || printf '%s\n' "$UPD_OUT" | sed 's/^/      rollback: /' >&2
  upd_expect "it warns about the dump, returns to the adopted release and says the database was restored from that dump" \
    "$(upd_says "REPLACES the panel database with ${UPD_DUMP_DIR}/${dump}")/$(upd_says "Rolled back from ${UPD_VERSION_B} to ${UPD_LEGACY_NAME}.")/$(upd_says "The database was restored from ${UPD_DUMP_DIR}/${dump}")" says-so/says-so/says-so
  upd_expect "current and VERSION are the adopted release; PREVIOUS is empty" \
    "$(upd_current_version)/$(upd_state VERSION)/$(upd_q grep -c '^PREVIOUS=$' /etc/maran/release.state)" "${UPD_LEGACY_NAME}/${UPD_LEGACY_NAME}/1"
  upd_expect "the restore discarded the post-update write: the fingerprint is the one the dump held" "$(upd_fingerprint)" "$UPD_FINGERPRINT"
  upd_expect "the restore also removed the schema and the tables a newer release added after the dump" "$(upd_later_objects)" 0
  upd_assert_runs "after the restoring rollback" "$UPD_LEGACY_NAME" "$UPD_VERSION_FIXED"
  upd_assert_healthy "after the restoring rollback"
  upd_assert_data "after the restoring rollback"
}

# upd_print_blind_spots: what this stage could not see, in its own output (rules/testing.md).
upd_print_blind_spots() {
  echo "    update-rollback: ${recovery_checks} checks so far (this stage: signed offline bundles, --check, update, rollback, two releases that cannot run, the retry of a failed version, the dump retention, seven refused updates that change nothing, and a legacy host adopted, rolled back to and restored)"
  echo "    UNOBSERVED HERE: the NETWORK fetch path. \`maran update\` and \`--check\` were never run against a release host: no TLS fetch, no manifest.json download, no channel flag, and so no refusal of a manifest signed for the other channel (an offline bundle keeps the channel it names). Every update here used --offline-tarball, because a local host cannot be given a certificate the product trusts without an override the product deliberately lacks."
  echo "    UNOBSERVED HERE: the production release key. Bundles are signed by throwaway keys whose public half replaced the package's key inside this container only; that the shipped key verifies a real release is proved by \`maran release verify\`, not here."
  echo "    UNOBSERVED HERE: a real schema migration. The artifacts are the ones already installed, so --migrate had nothing to apply; expand-then-contract against a changed schema, and a failed migration half way, were not exercised."
  echo "    UNOBSERVED HERE: the restore of the OTHER rollback shape, where an automatic rollback left PREVIOUS equal to the running release (only its refusal is observed); the restore was exercised once, on the adopted legacy release, and its proof is one changed column plus a schema and two tables added after the dump, not a full comparison of the database."
  echo "    UNOBSERVED HERE: the retention of releases (every release made here is among the 3 newest, current or previous, so nothing is pruned), a kill during an update (the interrupted-update recovery), a full disk, the lock against a concurrent panel-backup, and SELinux enforcing."
  echo "    UNOBSERVED HERE: a release built from different code. B is A's own binaries with a marker file added to the frontend; the proof of 'B runs' is the real path of each daemon's executable and the marker nginx serves."
  if [ "$UPD_SITE_OK" != "yes" ]; then
    echo "    UNOBSERVED HERE: the site across the update. It could not be created or could not be made to serve before the update (see the failure above)."
  fi
}

# stage_update_rollback: the stage, as the install polygon calls it. Failures are counted through
# `note`; it never fails the harness (the minimum-checks guard at the call site catches an early
# stop), installs a server of its own and leaves `container` pointing at the server it found.
stage_update_rollback() {
  local origin_container="$container" fresh="${container}-update" fresh_log
  fresh_log="${log_dir}/install-update-${os}-${arch}-${mode}.log"
  UPD_SITE_OK="no"
  UPD_WORK="$(mktemp -d)"
  trap 'rm -rf -- "$UPD_WORK"' EXIT
  if upd_build_all; then
    upd_check_bundles_on_host
    extra_containers+=("$fresh")
    if start_server_container "$fresh"; then
      container="$fresh"
      upd_install_release_a "$fresh" "$fresh_log"
      if wait_for_panel; then
        upd_assert_layout
        upd_assert_runs "release A" "$UPD_VERSION_A"
        upd_assert_healthy "release A"
        if upd_seed; then
          upd_assert_data "release A"
          upd_assert_bundle_trust
          upd_phase_check
          upd_phase_update
          upd_phase_rollback
          upd_phase_broken "broken release (migrate fails)" release-broken-migrate.tar.gz
          upd_phase_broken "broken release (fails the health gate)" release-broken-gate.tar.gz
          upd_phase_refusals
          upd_phase_retry
          upd_phase_legacy
        fi
      else
        note "update: the panel installed from release A never reported a reachable database; nothing below was observed (see ${fresh_log#"$root"/})"
      fi
    else
      note "update: the third server's container did not boot, so nothing could be updated"
    fi
    upd_print_blind_spots
  fi
  container="$origin_container"; rm -rf -- "$UPD_WORK"; trap - EXIT
  return 0
}
