#!/usr/bin/env bash
# Step 80: install the panel's nginx vhost (installer/nginx/maran.conf) with a
# self-signed certificate, listening on the panel's public port — the number
# install.sh sets once as MARAN_PANEL_PORT, and the vhost's own `listen` line
# spells as a literal because a configuration file interpolates no shell
# variable. The two are held together by an assertion in the polygon's
# assert-installer-steps.sh. The vhost reaches disk through the protocol
# rules/rust.md fixes for every system configuration Maran writes under "Config
# writes: render → swap → validate" — render, stage in the target's own directory,
# fsync the staged file AND its directory, rename, validate, and restore the entry
# content on a refusal — which is the same sequence ops::safe_write performs for the
# agent's customer configs, guard included: see move_into_place for the durability,
# arm_vhost_guard for the interrupt that order exposes, and install_validated_vhost
# for why the swap precedes the validation.
set -euo pipefail

readonly MARAN_TLS_DIR="/etc/maran/tls"
readonly MARAN_CERT_PATH="${MARAN_TLS_DIR}/panel.crt"
readonly MARAN_KEY_PATH="${MARAN_TLS_DIR}/panel.key"

# The two directories the AGENT writes into and never creates: the customer vhosts it
# renders (AgentPaths::NGINX_INCLUDE_DIRECTORY) and the certificate material it installs
# (AgentPaths::CERTIFICATE_DIRECTORY). They are made here because they are host layout,
# like /var/lib/maran and /run/maran, and because the agent stages a vhost into the
# directory's own parent before renaming it into place — a missing directory is not an
# empty list of sites, it is the first CreateSite on the server failing with ConfigWrite.
readonly MARAN_NGINX_SITES_DIR="/etc/maran/nginx/sites"
readonly MARAN_CERTIFICATES_DIR="/etc/maran/certificates"

# The snippet carrying the one include line without which every vhost the agent writes is
# a file nginx never parses — and, worse, `nginx -t` still passes, because it validates a
# tree the file is not in. A file of our own under conf.d rather than an edit to the
# distribution's nginx.conf: it is idempotent by identity (writing it twice leaves one
# line, where appending twice leaves two), it is removable by deleting one file, and the
# agent's rule of never touching files it does not own applies to the installer too.
readonly MARAN_NGINX_INCLUDE_CONF="/etc/nginx/conf.d/maran-sites.conf"

# The two names the panel's own vhost passes through for the length of one install: the
# rendered candidate before it is swapped in, and the copy of whatever was there before,
# kept only until the swap is known to be good.
#
# NEITHER ends in `.conf`, and that is the point rather than a naming preference. Both
# families include `conf.d/*.conf` and nothing else, so a file under either suffix is one
# nginx never opens — which is what makes the rename in install_validated_vhost the exact
# moment the new vhost becomes visible to `nginx -t`, and what stops the rollback copy from
# being parsed as a second panel vhost while it exists.
readonly MARAN_VHOST_CANDIDATE_SUFFIX=".candidate"
readonly MARAN_VHOST_PREVIOUS_SUFFIX=".previous"

# Where a `<dest>.previous` that was ALREADY THERE when this run started is moved to, so that this
# run can use `.previous` for its own copy of the entry bytes without destroying the leftover.
#
# The two are different files answering different questions, and conflating them is the whole of
# this step's fourth review. `<dest>.previous` after this run has claimed it is, always, what was on
# the served path when this run first looked — no predicate, nothing to get wrong.
# `<dest>.adopted` is a leftover: the entry copy of a run that never finished, which is to say the
# last panel vhost an install of this product actually put through `nginx -t`. Only a file
# vhost_is_ours accounts for is moved here; anything else goes to `<dest>.foreign` instead.
#
# Same reasoning as the two names above for the suffix: it does not end in `.conf`, so nginx never
# opens it while it exists.
readonly MARAN_VHOST_ADOPTED_SUFFIX=".adopted"

# Where a file found under one of this step's working names that this step cannot account for is
# parked. Nothing is ever served from here and nothing here is ever deleted by this step or by
# uninstall.sh; the operator is told the name.
#
# THE RENAME IS NOT UNDONE, on any path — not on the success path, not by restore_panel_vhost, not
# by the interrupt trap, not by uninstall.sh — and that is a decision rather than an omission, so
# it is written down here with what it costs. What comes back after a rollback is THE SERVED PATH,
# byte for byte; the directory does not, because a file at `<dest>.previous` is a name this step
# needs and cannot share, and the two ways to free it are to move the occupant or to destroy it.
# Moving is the only one available to an installer: the file is most likely an operator's own
# backup, this product did not write it, and no run of an installer is entitled to delete it or to
# guess when it has stopped mattering. Putting it BACK afterwards is not on offer either — the name
# is occupied again the moment the next run needs it, and a restore that re-collides is a worse
# promise than a rename the operator was told about.
#
# The cost, stated rather than left to be discovered: an operator who keeps re-creating a backup at
# `maran.conf.previous` gets one parked file per install in /etc/nginx/conf.d, forever, and an
# uninstall leaves them. They are inert, and the ground for that is narrower than
# "nginx globs only `*.conf`", which is FALSE: debian's shipped nginx.conf also carries
# `include /etc/nginx/sites-enabled/*;`, which filters no extension at all and would parse every
# name on this list. The real ground is that every working and parked name this step makes lives in
# `conf.d`, which both families glob as `conf.d/*.conf`, and that no name it makes ends in `.conf`
# (asserted from nginx's own dump of the tree it loads, not from this comment). The corollary is a
# rule rather than a remark: this step must never put one of its working names under
# `sites-enabled`, where the suffix buys nothing. Each collision takes a timestamped name rather
# than overwriting its predecessor, so nothing is lost to the accumulation either. The operator is
# told each new name as it is made, and told that it is theirs to remove; that is the whole of the
# cleanup contract, and it is deliberately not automatic.
readonly MARAN_VHOST_FOREIGN_SUFFIX=".foreign"

# What the interrupt guard needs in order to undo a swap that is in flight, and the switch that
# says one IS in flight: the served path, where the bytes to put back are, the rendered temporary
# file to take with it, and whether the served path has actually been written to yet. Globals
# rather than install_validated_vhost's locals because a trap runs wherever the shell happens to
# be when the signal arrives — the EXIT trap in particular can run after that function has
# returned — and a handler that reached for a local would restore nothing on precisely the paths
# it exists for.
#
# An empty MARAN_VHOST_GUARD_DEST means disarmed: nothing is in flight and nothing is to be
# put back. See arm_vhost_guard.
MARAN_VHOST_GUARD_DEST=""
MARAN_VHOST_GUARD_RENDERED=""

# WHERE the bytes to put back came from, which decides whether the restoration is allowed to
# install them unconditionally. Exactly three values, and the difference between the first two is
# the whole of this step's third review:
#
#   copied   — the restoration puts back <dest>.previous: the bytes this run copied off the served
#              file before it touched anything. Those ARE the entry state, by construction, and no
#              check can make putting them back wrong.
#   adopted  — a <dest>.previous was already there when this run started AND vhost_is_ours proved
#              it is a whole, unmodified vhost some run of THIS installer rendered, so it was moved
#              to <dest>.adopted. The restoration PREFERS it to the entry bytes, because the state
#              it comes from is a killed install whose entry bytes are the candidate that run never
#              validated. It is still an ACTION rather than a restoration, so restore_panel_vhost
#              leaves it served only if nginx loads the tree with it — and falls back to the entry
#              bytes if it does not.
#   none     — this host had no panel vhost at all; the restoration removes the file.
#
# `adopted` never REPLACES the entry copy: <dest>.previous is taken whenever the served path exists,
# on every path that goes on to swap, and MARAN_VHOST_GUARD_ENTRY_KEPT says whether there is one.
# The third review's worst case was this step choosing `adopted` and therefore taking no copy of a
# healthy served vhost, then deleting it.
MARAN_VHOST_GUARD_ROLLBACK=none

# Whether <dest>.previous holds the served file's entry bytes: 1 when the served path existed when
# this run first looked, 0 when there was no panel vhost to copy. Not derivable from the rollback
# source — an `adopted` run has an entry copy too, and it is the fallback when the adopted file
# turns out not to parse.
MARAN_VHOST_GUARD_ENTRY_KEPT=0

# Whether the served path has been overwritten yet. The guard is armed BEFORE the write — the
# window between the rename and the arm is exactly the hole it exists to close — so between the
# arm and the rename there is a stretch in which the served file is still untouched and a
# restoration must do nothing to it. Set on the line after the rename: bash defers a trapped
# signal until the foreground command returns, so a flag set on the next line cannot be raced.
MARAN_VHOST_GUARD_SWAPPED=0

nginx_conf_dest() {
  case "$MARAN_OS_FAMILY" in
    debian) echo "/etc/nginx/conf.d/maran.conf" ;;
    rhel)   echo "/etc/nginx/conf.d/maran.conf" ;;
  esac
}

# generate_self_signed_cert: a 10-year self-signed cert scoped to the machine's
# hostname, generated once. It exists only so the panel is reachable over TLS
# immediately after install; the operator can point a real hostname at Let's Encrypt
# later per the design's update path. Skipped if a cert already exists (idempotent;
# re-running the installer must not silently invalidate a cert an operator already
# swapped in, e.g. after enabling Let's Encrypt).
#
# A SYMLINK AT EITHER PATH IS "already present", and it is checked BEFORE `[ -f ]` for the same
# reason vhost_is_ours checks `[ ! -L ]` first and the gates in install_validated_vhost ask
# `[ -L ]` beside `[ -e ]`: `[ -f ]` follows the link, so it answers about the TARGET while the
# thing this gate protects is the path. The two ways that costs an operator are both this
# function's own stated case, an operator who swapped in a real certificate — a renewing ACME
# client leaves symlinks at exactly these names. A link whose target exists takes the `[ -f ]`
# branch and is left alone, which is right by accident; a link whose target has gone (a renewal
# that moved the archive, a removed lineage, an unmounted volume) reads as "no certificate here",
# and `openssl req -keyout/-out` then writes THROUGH the link, creating this installer's
# throwaway self-signed key and certificate at a path outside /etc/maran that the operator's own
# tooling owns. Writing through a link out of the directory the installer owns is the one thing
# this file forbids everywhere else, and here it would be done with private key material.
#
# So a link is reported and left, dangling or not. It is not repaired and not replaced: which
# certificate should be at an operator's own symlink is not a question an installer can answer,
# and the failure it leaves — nginx refusing to start on a missing certificate file — names the
# path itself, whereas a silent write through the link does not.
generate_self_signed_cert() {
  if [ -L "$MARAN_CERT_PATH" ] || [ -L "$MARAN_KEY_PATH" ]; then
    echo "80-nginx.sh: ${MARAN_CERT_PATH} or ${MARAN_KEY_PATH} is a symlink, so a certificate of
your own is in place and this step generates none: it never writes through a link out of the
directory it owns, which with a key file is how an installer overwrites something it has no idea
about. If either link's target is missing, nginx will not start until you point it at a
certificate." >&2
    return
  fi
  if [ -f "$MARAN_CERT_PATH" ] && [ -f "$MARAN_KEY_PATH" ]; then
    echo "TLS certificate already present, leaving it in place."
    return
  fi
  install -d -o root -g panel -m 0750 "$MARAN_TLS_DIR"
  local hostname
  hostname="$(hostname -f 2>/dev/null || hostname)"
  openssl req -x509 -nodes -newkey ed25519 \
    -keyout "$MARAN_KEY_PATH" -out "$MARAN_CERT_PATH" \
    -days 3650 -subj "/CN=${hostname}" \
    -addext "subjectAltName=DNS:${hostname}"
  chown root:panel "$MARAN_KEY_PATH" "$MARAN_CERT_PATH"
  chmod 0640 "$MARAN_KEY_PATH"
  chmod 0644 "$MARAN_CERT_PATH"
}

# The first line of every vhost this step renders, and the only thing on the machine that answers
# "did this installer write these bytes": a comment carrying the SHA-256 of everything after it.
#
# A comment because the file has to stay a vhost nginx parses; a digest OF THE REST OF THE FILE
# rather than a bare tag because a bare tag survives the three things the marker has to tell apart:
#
#   * an operator's hand edit — the body changes, the digest stops matching, and the file is
#     correctly no longer "ours". A file we would restore over a working configuration must be one
#     nobody has touched since we wrote it, not merely one that started life here.
#   * a half-finished run — a truncated or null-padded copy, the ordinary outcome of a hard reset
#     mid-write, fails the digest, because the marker describes bytes that are no longer all there.
#     This is the 3000-byte fragment the third round measured being served.
#   * a name collision — an operator's own `maran.conf.previous` backup of some other vhost, or of
#     an edited one, carries no marker at all.
#
# What it deliberately does NOT claim: unforgeability against root. Anything root can read, root
# can rewrite, and this step runs as root on a host whose root is the operator. The marker is a
# guard against ACCIDENT — the three states above, each of which has cost a review round — not an
# authenticator, and it is not used to make a trust decision about anything but this step's own
# scratch file.
readonly MARAN_VHOST_MARKER_PREFIX="# maran-vhost sha256:"

# vhost_is_ours: whether `$1` is a regular file — not a symlink to one — whose first line is the
# marker above, whose remaining bytes hash to what that line claims, and which has the shape of a
# panel vhost rather than merely a self-consistent header.
#
# Regular-file first, and that is a fix rather than defensiveness: a DIRECTORY at `<dest>.previous`
# used to be moved into by `write_rollback_copy`'s rename and then fail `rm -f` after the install
# had already succeeded, killing the shell under `set -e` after the guard was disarmed — exit 1
# from a step that worked, with no message naming it and no reload. Anything that is not a regular
# file is simply not ours, and install_validated_vhost parks it rather than touching it.
#
# `[ ! -L ]` BEFORE `[ -f ]`, because `[ -f ]` alone is not "is a regular file": it FOLLOWS the
# link and is true of any symlink resolving to one. The doc above used to say regular file and the
# test used to reject only a directory, a socket and a dangling link, and the cost was measured on
# both families: a symlink at `<dest>.previous` pointing at a stamped copy outside /etc/nginx was
# accounted for, adopted, renamed onto the served path, and the entry copy of the vhost that was
# actually serving was then deleted on the strength of it. Three things break at once. The served
# configuration becomes a link out of the directory this step owns, so deleting its target — or
# booting with that filesystem unmounted — stops nginx from STARTING, which is every customer site
# on the host through maran-sites.conf and not merely the panel. `try_restore_from`'s proof
# degrades from "nginx loads the tree with these bytes" to "nginx loaded whatever the link pointed
# at, at one instant", which is exactly the "the file validated and the file served are two
# different files" proposition that function exists to forbid; anything that can write the target
# afterwards changes what is served without any check. And the entry bytes are discarded because
# the answer came back true. A symlink under one of this step's working names is a thing to PARK,
# like every other file here it cannot account for, and that is what the caller does with a false
# answer — `[ ! -L ]` is what routes it there.
#
# A NON-EMPTY BODY, AND THE VHOST'S OWN SHAPE, because a digest that agrees with itself is not yet
# a digest of anything. `tail -n +2` on a file consisting of the marker line ALONE yields nothing,
# and the SHA-256 of nothing is a perfectly well-formed value: a 72-byte file carrying the empty
# string's digest satisfied every check here, was adopted, and was left served — one comment line
# is valid nginx, so `nginx -t` passed with it and the operator was told the served path held "the
# last panel vhost an install of this product validated", while it held no `server` block and no
# `listen` at all and the entry copy had been deleted. The lower bound is therefore not "some
# bytes" but the one line that makes a panel vhost a panel vhost — the `listen` directive the
# template's own port assertion looks for — which no marker-only residue of a truncated copy can
# have. It is a check on this step's OWN render (every `listen` line in installer/nginx/maran.conf
# is one), not a general nginx parser: `nginx -t` remains the only thing allowed to decide whether
# a file is servable.
#
# Every failure is a plain `return 1`. There is no error to report: "not ours" is an ordinary
# answer, and it is the SAFE answer — the caller's response to it is to leave the file alone.
vhost_is_ours() {
  local path="$1" first claimed actual listens

  [ ! -L "$path" ] || return 1
  [ -f "$path" ] || return 1
  # `read` without a trailing newline on the line still fills the variable but returns non-zero;
  # the marker is written with printf '\n' so a whole file always has one, and a file whose FIRST
  # line has no newline is a file with no body to hash.
  IFS= read -r first < "$path" || return 1
  case "$first" in
    "${MARAN_VHOST_MARKER_PREFIX}"?*) claimed="${first#"${MARAN_VHOST_MARKER_PREFIX}"}" ;;
    *) return 1 ;;
  esac
  actual="$(tail -n +2 -- "$path" | sha256sum | cut -d' ' -f1)"
  [ "$actual" = "$claimed" ] || return 1
  # The BODY is what the digest above attests, so the shape is asked of the body and not of the
  # whole file: `tail -n +2` again, exactly the bytes that were hashed. Over the whole file the
  # check would also be satisfied by a first line — the one line here that is not attested by
  # anything — and a predicate whose lower bound can be met by its own header is the defect this
  # check exists to close, one line further down. A body with a `listen` line is a body, so no
  # separate emptiness test is needed: a marker-only residue has neither.
  #
  # `grep -c`, not `grep -q`, and that is `set -o pipefail` rather than taste: `-q` exits the moment
  # it matches, which leaves `tail` writing into a closed pipe, and a `tail` that dies of SIGPIPE
  # makes the PIPELINE 141 under pipefail — so the good files this predicate exists to recognise
  # would be refused, racily, by file size. `-c` consumes its whole input. The digest pipeline above
  # is safe for the same reason: sha256sum reads to EOF.
  listens="$(tail -n +2 -- "$path" | grep -c -e '^[[:space:]]*listen[[:space:]]' || true)"
  [ "$listens" -gt 0 ]
}

# move_into_place: rename `$1` onto `$2` so that a crash can leave neither unflushed bytes under a
# committed name nor a rename the next boot has not heard of.
#
# This is step 3 and step 4 of rules/rust.md "Config writes: render → swap → validate", in that
# order and for the reason the rule gives: "fsync the temporary file AND its containing directory,
# so a crash cannot leave a rename pointing at unflushed bytes". Every swap of the SERVED path goes
# through here — the install of the render and every restoration alike — because the file this step
# exists to install is the one the rule is actually about, and a durability that reached only the
# rollback copy is a promise kept to the wrong file. The failure it removes is not exotic: a
# successful install, then a power cut inside the writeback window, leaves the directory entry for
# maran.conf pointing at unflushed data — a zero-length or null-padded vhost — and this same nginx
# serves every customer site through maran-sites.conf, so it is the whole host that does not come
# back up, with the rollback copy already deleted because the install had succeeded.
#
# `sync FILE` and `sync DIRECTORY` are coreutils' fsync of exactly those objects, not the
# whole-system `sync(2)`; both families ship a coreutils new enough for the operand form, and the
# same call is what write_rollback_copy has used since the third round.
# The rename's own status is what this function RETURNS, which is the reason for the two
# `|| return 1`s rather than a bare sequence. It used to end on the directory `sync` and so
# returned that: the target directory always exists, the flush always succeeds, and every one of
# the three `|| return 1` call sites was therefore testing a value that could not be anything but
# 0. A failed `mv` reported success — measured, with the destination on a read-only filesystem —
# and the worst of the three is try_restore_from, where a forward rename that silently did nothing
# is followed by the reverse rename moving the REFUSED candidate onto the entry copy's name.
move_into_place() {
  local source="$1" destination="$2"
  sync "$source" || return 1
  mv -f "$source" "$destination" || return 1
  sync "$(dirname "$destination")"
}

# render_vhost: substitutes the placeholders in the shipped template into a temp file, and stamps
# the provenance marker onto its first line — see MARAN_VHOST_MARKER_PREFIX for what the marker is
# for and what it does not claim.
render_vhost() {
  local out="$1" hostname body digest
  # The api's socket comes from install.sh, the one place it is decided, rather than from a
  # literal here: the path in the vhost and the path in panel.env are the two ends of the same
  # socket, and a mismatch is a panel that answers 502 to every call.
  : "${MARAN_API_SOCKET_PATH:?must be set by install.sh before this step is sourced}"
  hostname="$(hostname -f 2>/dev/null || echo "_")"
  body="$(mktemp)"
  sed \
    -e "s#__MARAN_SERVER_NAME__#${hostname}#g" \
    -e "s#__MARAN_CERT_PATH__#${MARAN_CERT_PATH}#g" \
    -e "s#__MARAN_KEY_PATH__#${MARAN_KEY_PATH}#g" \
    -e "s#__MARAN_API_SOCKET__#${MARAN_API_SOCKET_PATH}#g" \
    "${LIB_DIR}/../nginx/maran.conf" > "$body"
  digest="$(sha256sum "$body" | cut -d' ' -f1)"
  # Marker first, body after: the digest covers the whole file except its own line, so recomputing
  # it needs no knowledge of where the marker sits beyond "line 1".
  printf '%s%s\n' "$MARAN_VHOST_MARKER_PREFIX" "$digest" > "$out"
  cat "$body" >> "$out"
  rm -f "$body"
}

# install_agent_config_include: creates the agent's own configuration directories and
# points nginx at the vhost one. Idempotent — `install -d` on an existing directory
# succeeds, and the include is one whole file that is rewritten rather than a line
# appended to somebody else's, so a re-run cannot produce a duplicate include.
#
# Public on purpose: the polygon images call THIS function to obtain the precondition
# their site tests need, instead of performing the same edit themselves. An image that
# manufactures the precondition it then asserts is how the missing include survived the
# whole suite once already (rules/testing.md — a test proving the wrong proposition).
install_agent_config_include() {
  install -d -o root -g root -m 0755 "$MARAN_NGINX_SITES_DIR"
  install -d -o root -g root -m 0755 "$MARAN_CERTIFICATES_DIR"

  local tmp
  tmp="$(mktemp)"
  cat > "$tmp" <<EOF
# Maran: nginx serves the vhosts the agent renders, and only those. Written by
# installer/lib/80-nginx.sh; the directory belongs to the agent (spec §9).
include ${MARAN_NGINX_SITES_DIR}/*.conf;
EOF
  install -m 0644 "$tmp" "$MARAN_NGINX_INCLUDE_CONF"
  rm -f "$tmp"
}

# nginx_tree_loads: whether the real configuration tree — nginx.conf and everything its `include`
# globs pull in — loads right now.
#
# One reader, because every decision below turns on this question and asking it three different
# ways in three places is three chances to ask it weakly. Output is discarded: the callers print
# their own sentence, and the operator has already seen nginx's own message on the refusal path.
nginx_tree_loads() {
  nginx -t >/dev/null 2>&1
}

# write_rollback_copy: put a copy of `$1` at `$2` so that no crash can leave `$2` half-written.
#
# `cp -p "$dest" "$previous"` is what this step used to do, and the third review is what it cost:
# a copy that is neither atomic nor flushed becomes, on the next run, the file the rollback
# INSTALLS. rules/rust.md "Config writes: render → swap → validate" requires the fsync of the
# temporary file and of its containing directory for exactly this hazard — "so a crash cannot leave
# a rename pointing at unflushed bytes" — and it is load-bearing for a rollback copy in a way it is
# for few other files: this one is read by a DIFFERENT run of the installer, after the crash the
# fsync is about. The rule is cited by SECTION rather than by line number, which is how the third
# round came to cite a line about not running caller-supplied programs as the authority for fsync.
#
# Same directory for the staging name, so the rename is within one filesystem and therefore
# atomic; the name does not end in `.conf`, so nginx never parses it while it exists.
#
# A failure here is fatal to the install and that is deliberate: the caller has not armed the
# guard yet and has not touched the served path, and swapping a vhost in with no rollback source
# is precisely the state the guard exists to prevent.
write_rollback_copy() {
  local source="$1" destination="$2" staging
  staging="$(mktemp "${destination}.XXXXXX")"
  install -m 0644 "$source" "$staging"
  move_into_place "$staging" "$destination"
}

# discard_rollback_copy: remove `$1` and make the removal durable.
#
# The mirror of write_rollback_copy and here for the same reason: an unlink whose directory entry
# is still in the page cache is an unlink a crash can undo, and what comes back is a rollback copy
# of the PREVIOUS version of the vhost sitting beside a newer served file — which the next run
# would find and consider. `sync` on the directory is what makes "this file is gone" true across
# the crash rather than only in this kernel's cache.
discard_rollback_copy() {
  local path="$1" directory
  directory="$(dirname "$path")"
  rm -f "$path"
  sync "$directory"
}

# report_tree_after_restore: the one sentence about `nginx -t` that follows a restoration which put
# the ENTRY state back, and the one place this step is allowed to say whose file is at fault.
#
# It says nothing at all when the tree loads. When it does not, the attribution is a MEASUREMENT
# rather than an assumption, and it is sound for one specific reason: restore_panel_vhost calls it
# from exactly one place — the arm it reaches after measuring that the tree does not load even with
# no panel vhost at all, having then put the entry bytes back. At that point nothing this step
# rendered is on the tree: the exact entry bytes are at `$1`, or there were none and there is no
# file at `$1`, and the rendered candidate is gone either way. So an `nginx -t` that fails HERE is
# failing on something this step did not write.
#
# The claim is deliberately about THIS STEP'S OWN WRITES and no longer "byte-for-byte the tree this
# step found", because two states this step reaches are not that tree. A file moved by
# park_foreign_file went from one name nginx never opens to another, which nginx cannot tell apart
# — but a SYMLINK on the served path is a name nginx does open, and parking it (see
# install_validated_vhost) removes content from the tree that was there when the step started. The
# narrower sentence is true of every one of them; the older one was measurably false of that last.
# So an `nginx -t` that fails HERE is an `nginx -t` that would have failed before the step ran.
#
# It is measured after the fact rather than sampled before the step starts, and that is deliberate:
# a baseline taken at the top of install_validated_vhost would be a SECOND `nginx -t` on every
# ordinary install, which — measured — is also the first one the polygon's interrupt shim meets, so
# every interrupt case in the suite would land before the swap and stop testing the guard entirely.
#
# The sentence this replaces read "'nginx -t' still fails without the panel vhost, so this host's
# nginx configuration was already broken before this step ran", and it was printed after a restore
# that had just put a panel vhost back: on a re-install it asserted the opposite of the state it was
# describing, and on the path where the restoration installed a leftover copy it blamed the operator
# for a file this step had just written.
report_tree_after_restore() {
  local dest="$1"

  if nginx_tree_loads; then
    return 0
  fi
  echo "80-nginx.sh: note — 'nginx -t' does not pass on this host, and it is not this step's
render that it is failing on: everything this step wrote has been taken back off the tree, and the
entry bytes of ${dest}, if there were any, are back on it. The configuration at fault is not one
this step wrote." >&2
}

# park_foreign_file: move `$1` to a name this step will never use again and echo where it went.
#
# For everything found under one of this step's three working names that this step did not put
# there. It is MOVED, never removed: a file an operator parked at `<dest>.previous` — the name a
# human picks for a backup — is theirs, and a run of the installer is not entitled to delete it.
# The parked name does not end in `.conf` either, so nginx never opens it there.
#
# On the collision, which is not hypothetical after two such runs: a second parked file gets a
# timestamped name rather than replacing the first, because `mv -f` of a directory onto an existing
# directory moves it INSIDE it, and either way overwriting is the one thing this function exists
# not to do.
park_foreign_file() {
  local path="$1" parked="${1}${MARAN_VHOST_FOREIGN_SUFFIX}"
  # `[ -e ]` OR `[ -L ]`, because `[ -e ]` follows the link and is false for a dangling one: with
  # the test on `-e` alone, parking a second file would `mv -f` over a symlink parked by an earlier
  # run whose target has since gone. Same defect as vhost_is_ours', in the function whose entire
  # purpose is not to overwrite.
  if [ -e "$parked" ] || [ -L "$parked" ]; then
    parked="${parked}.$(date -u +%Y%m%d%H%M%S).$$"
  fi
  mv -f "$path" "$parked"
  printf '%s' "$parked"
}

# claim_working_name: make `$1` free for this step to write, without deleting anything that is not
# a plain file.
#
# A SYMLINK is one of the things it is wrong for, and it counts as "not a plain file" here even
# when it resolves to one — the same correction vhost_is_ours needed, for the same reason: `[ -f ]`
# follows the link. `rm -f` would take an operator's link away for a name this step is only
# borrowing, and leaving it would hand the following `install` a path that writes THROUGH it, into
# a file outside /etc/nginx. Parked, like everything else here this step did not put there.
#
# `rm -f` on one of this step's own scratch names is right for a regular file and wrong for
# everything else, and the difference has cost an install: a DIRECTORY at `<dest>.previous` was
# renamed INTO by the rollback copy's `mv`, so the copy succeeded against a path that is not a
# file, the swap and the validation then passed, and `rm -f` on the directory failed at the end and
# killed the shell under `set -e` — after the guard was disarmed, so no message named the step. The
# operator got exit 1 from an install that had worked, a vhost on disk the running server was never
# told about, and a stray copy of the panel vhost left inside the directory.
claim_working_name() {
  local path="$1" parked

  if [ -L "$path" ] || { [ -e "$path" ] && [ ! -f "$path" ]; }; then
    parked="$(park_foreign_file "$path")"
    echo "80-nginx.sh: ${path} is one of this step's working names and what is there is not a
regular file. It has been moved to ${parked} and left alone — nothing under that name is ever
served, and neither this step nor uninstall.sh removes it — and the install continues." >&2
    return 0
  fi
  rm -f "$path"
}

# try_restore_from: rename `$1` onto the served path `$2` and keep it there only if the whole
# configuration tree loads with it; otherwise put it straight back under its own name.
#
# 0 means the bytes are serving and nginx loads the tree; 1 means they are back where they came
# from and the served path is whatever it was on the way in. Either way nothing is deleted.
#
# The tree is asked EVERY time, including for the entry bytes, and the reason is not distrust of
# them — they are the entry state by construction and putting them back cannot make the host worse
# than it was found. It is that the caller has a better outcome available when the answer is no:
# with the bytes safe under their own name, an empty served path costs the panel, while a served
# path holding a vhost nginx refuses costs every customer site on this host at the next restart,
# because the same nginx serves them all through maran-sites.conf. So the question is asked in
# order to CHOOSE, not in order to decide whether the bytes are worth keeping — that choice is the
# caller's, and no answer here loses a byte.
#
# The bound, stated because a guarantee nobody has bounded is worse than none: the bytes are on the
# served path for the length of one `nginx -t` — 6 ms on an empty host, a third of a second on one
# serving 500 sites — before their verdict is known, and a SIGKILL inside that window leaves them
# there. Nothing in this step reloads inside it.
try_restore_from() {
  local source="$1" dest="$2"

  move_into_place "$source" "$dest" || return 1
  if nginx_tree_loads; then
    return 0
  fi
  move_into_place "$dest" "$source" || return 1
  return 1
}

# restore_panel_vhost: put the served path back the way install_validated_vhost found it, take the
# rendered candidate with it, and say what is there now — WITHOUT ever serving a file it has not
# proved nginx loads the tree with, and WITHOUT ever deleting the bytes it found.
#
# `$1` is the served path, `$2` where the rollback bytes come from (see MARAN_VHOST_GUARD_ROLLBACK),
# `$3` whether the served path was actually overwritten, `$4` whether `<dest>.previous` holds the
# served file's entry bytes.
#
# ONE implementation, called by the refusal path and by the interrupt trap alike, and it prints the
# operator's line itself so the two callers cannot drift into saying different things about the same
# state. It takes its subject as arguments rather than reading the guard globals, so that the trap
# can disarm — clearing those globals — BEFORE it starts restoring.
#
# THE TWO RULES THIS FUNCTION EXISTS FOR, one per review round it took to learn them:
#
#   * A restoration that silently serves an unvalidated file is worse than no restoration, because
#     the operator is told the machine is as they left it. A 3000-byte fragment of the 6987-byte
#     vhost — the ordinary ext4 outcome of a hard reset mid-copy — was measured being installed by
#     an earlier version of this function, on a clean SIGTERM, under the words "is as it was before
#     this step ran": nginx then would not start, taking the panel and every customer site with it.
#     Hence try_restore_from, which asks the tree about every candidate before leaving it served.
#   * A restoration that DELETES the bytes it found is worse still, and that is subtler because it
#     reads as safety. The round-3 version chose between "restore" and "adopt" by asking whether
#     `nginx -t` passed on the host — a question about the whole tree, not about this file — so on
#     a host some third party's `conf.d` file had broken, with any `<dest>.previous` present, it
#     took no copy of the served vhost at all, overwrote it, had the render refused, declined the
#     leftover, and left the served path EMPTY. The working panel vhost that was serving beforehand
#     then existed nowhere on the machine, while the message said the fault lay elsewhere.
#
# So: `<dest>.previous` is written on every path that goes on to swap, by install_validated_vhost,
# before anything is touched and with no question asked; and nothing below removes a copy while the
# served path is left without a vhost. The order of preference is
#
#   1. `<dest>.adopted`, present only when vhost_is_ours proved the leftover whole and unmodified.
#      It is preferred to the entry bytes for exactly one state — the run after a killed install,
#      where the entry bytes ARE the candidate that run never validated and this file is the last
#      vhost an install of this product actually put through `nginx -t`.
#   2. `<dest>.previous`, the entry state.
#   3. no panel vhost, with every copy still on the machine and named to the operator. Reached only
#      when the tree loads WITHOUT one, so it costs the panel and nothing else.
#
# An adopted leftover that is NOT left served stays at `<dest>.adopted`, where the next run picks it
# up: install_validated_vhost promotes it back to `<dest>.previous` before it reads anything, so
# there is one rule about that file rather than one per exit path here.
#
# And if the tree does not load with any of them, the entry bytes go back and the message says the
# fault is not this step's — which is a measurement rather than an assumption, because after that
# the tree is byte-for-byte the tree this step found. (A file parked by park_foreign_file moved
# between two names nginx opens under neither, so it does not disturb that.)
restore_panel_vhost() {
  local dest="$1" rollback="$2" swapped="$3" entry_kept="$4"
  local previous="${dest}${MARAN_VHOST_PREVIOUS_SUFFIX}"
  local adopted="${dest}${MARAN_VHOST_ADOPTED_SUFFIX}"

  # The render staged under a name nginx never opens goes on every path out of here.
  rm -f "${dest}${MARAN_VHOST_CANDIDATE_SUFFIX}"

  # Nothing was written to the served path, so nothing is restored to it. Without this the guard
  # would REPLACE a vhost the step never touched — measured on a failing `install` with a leftover
  # rollback copy present: the served path took the leftover and the step said it was as it was.
  if [ "$swapped" -eq 0 ]; then
    echo "80-nginx.sh: ${dest} was not written to at all, so it is untouched and nothing was
reloaded." >&2
    # This run's own copy is redundant beside an untouched original and goes. An adopted leftover
    # is the only copy of the last validated vhost on this machine and stays where it is; the next
    # run promotes it back to `<dest>.previous` before it reads anything.
    if [ "$entry_kept" -eq 1 ]; then
      discard_rollback_copy "$previous"
    fi
    return 0
  fi

  if [ "$rollback" = adopted ] && try_restore_from "$adopted" "$dest"; then
    # The entry bytes are dropped here, and only here, because what replaced them is strictly
    # better by both of this function's rules: it is a file this installer provably wrote, and
    # nginx has just loaded the whole tree with it, whereas the entry bytes are the candidate a
    # killed run never validated. Nothing of value is lost; F2's harm was losing a vhost that was
    # SERVING, and the served path is occupied by a validated one before this line runs.
    if [ "$entry_kept" -eq 1 ]; then
      discard_rollback_copy "$previous"
    fi
    echo "80-nginx.sh: ${dest} now holds the last panel vhost an install of this product validated,
which an install that did not finish left behind; 'nginx -t' passes with it and nothing was
reloaded. What was on the served path when this run started was that unfinished install's
never-validated candidate, and it does not parse. It is that install's configuration and not this
run's: if it was given a different panel port, the panel answers on that one and not on
${MARAN_PANEL_PORT} — its 'listen' line says which." >&2
    return 0
  fi

  if [ "$entry_kept" -eq 1 ] && try_restore_from "$previous" "$dest"; then
    echo "80-nginx.sh: ${dest} is as it was before this step ran, and nothing was reloaded." >&2
    return 0
  fi

  # Nothing on offer lets the tree load with a panel vhost in place. Leave the served path empty
  # and say where every copy is.
  #
  # FIRST, the one state in which `rm -f "$dest"` would destroy the bytes this whole function exists
  # to preserve: try_restore_from could not rename the entry bytes back out of the served path, so
  # they are ON it and there is no copy at `<dest>.previous` any more. Removing the served file
  # there is exactly the harm F2 named, arrived at from the other direction. The step stops and says
  # so instead; the caller prints its own line about a served path it could not put back.
  if [ "$entry_kept" -eq 1 ] && [ ! -e "$previous" ]; then
    echo "80-nginx.sh: ${dest} holds the bytes this step found there and they could not be copied
back to ${previous}, so they are the only copy and they are left served. 'nginx -t' does not pass
with them; look at ${dest} before restarting nginx." >&2
    return 1
  fi
  # And now the removal, which takes only the refused render this run put there.
  rm -f "$dest"
  if nginx_tree_loads; then
    if [ "$entry_kept" -eq 1 ]; then
      echo "80-nginx.sh: the vhost that was at ${dest} when this run started does not parse, so it
was NOT put back — it is at ${previous} for you to look at. ${dest} now holds no panel vhost, nginx
loads without it, and nothing was reloaded: the panel is unreachable until an install finishes, and
nothing else on this host is affected." >&2
    else
      # NOT "as it was before this step ran": `entry_kept` is 0 both when the served path was
      # empty — where that sentence was true — and when it held a symlink this step parked and
      # named to the operator, where it is not. What is true of both is that this step has nothing
      # of its own to put back there, which is what it says.
      echo "80-nginx.sh: ${dest} now holds no panel vhost and this step has none of its own to put
back there; nginx loads without it and nothing was reloaded." >&2
    fi
    if [ "$rollback" = adopted ]; then
      echo "80-nginx.sh: the rollback copy an unfinished install left behind does not parse either,
and is still at ${adopted}." >&2
    fi
    return 0
  fi

  # The tree does not load even with no panel vhost at all, so the served path is not what is wrong
  # with this host. Put the entry bytes back — the state this step found, whatever it was — rather
  # than leaving an operator one file short of it.
  if [ "$entry_kept" -eq 1 ]; then
    move_into_place "$previous" "$dest" || return 1
    echo "80-nginx.sh: ${dest} is as it was before this step ran, and nothing was reloaded." >&2
  else
    # Same distinction as the arm above: with no entry copy the served path is empty, and saying
    # it is "as it was" would be a claim about a symlink this step parked rather than about bytes
    # it put back.
    echo "80-nginx.sh: ${dest} holds no panel vhost and this step has none of its own to put back
there; nothing was reloaded." >&2
  fi
  report_tree_after_restore "$dest"
  return 0
}

# arm_vhost_guard: from here until it is disarmed, any way out of this shell that is not one of
# install_validated_vhost's two decisions puts the served path back first.
#
# `$1` is the served path, `$2` where the rollback bytes come from, `$3` the rendered temporary
# file to remove on the way out, `$4` whether `<dest>.entry` holds the served file's entry bytes.
#
# This is the shell's answer to ops::safe_write's RollbackGuard
# (agent/crates/ops/src/safe_write/rollback_guard.rs), and it is here for the reason that file
# gives in its own words: there are more ways out of a write sequence after the rename than
# anybody remembers, and the one that gets forgotten is the one that leaves a server unable to
# start. The refusal path below still restores explicitly, because only it can also tell the
# operator what happened and stop the install non-zero; the trap is underneath as the net for the
# paths that never reach a decision at all. Without it this step was measurably WORSE than the
# staging-name version it replaced: SIGTERM inside `nginx -t` left the never-validated candidate
# on the served path for good, and because this nginx also serves every customer site through
# maran-sites.conf, the next restart or reboot took the whole box down.
#
# WHAT IT DOES NOT COVER, said plainly because a guarantee nobody has bounded is worse than none:
#
#   * SIGKILL. A shell cannot trap it. `kill -9`, an OOM kill of this process, a container
#     stop that runs out of grace — none of them reach the handler. Such a run also leaves its
#     render in /tmp (mode 0600, root, cert PATHS and no key material) because nothing runs
#     afterwards to remove it.
#   * A power cut, a hard reset, a panic: the same. The rollback copy itself is written atomically
#     and fsynced (write_rollback_copy), so a crash cannot leave a torn one for the next run to
#     find — but the SWAP is a plain `mv` whose durability this shell does not control, and a
#     crash inside the one `nginx -t` below can still leave the never-validated candidate served.
#     That is the state the next run is written for: see install_validated_vhost.
#   * A second install running at the same time. There is no lock here; two concurrent step 80s
#     would fight over the same two file names. install.sh is a single-run installer an operator
#     starts by hand, and that is the whole of the argument.
#
# What is left after one of those is a file worth having under one of two names: `<dest>.previous`
# or `<dest>.adopted`, each written whole or not written at all (write_rollback_copy). That is why
# the next run keeps a `.previous` instead of deleting it — and why it still asks two separate
# questions about it before serving it: vhost_is_ours, because "whole" is a promise about OUR crash
# and not about whatever else may be sitting under that name, and then `nginx -t` on the tree with
# it in place.
arm_vhost_guard() {
  MARAN_VHOST_GUARD_DEST="$1"
  MARAN_VHOST_GUARD_ROLLBACK="$2"
  MARAN_VHOST_GUARD_RENDERED="$3"
  MARAN_VHOST_GUARD_ENTRY_KEPT="$4"
  MARAN_VHOST_GUARD_SWAPPED=0
  trap 'vhost_guard_fired EXIT' EXIT
  trap 'vhost_guard_fired INT' INT
  trap 'vhost_guard_fired TERM' TERM
  trap 'vhost_guard_fired HUP' HUP
}

# disarm_vhost_guard: install_validated_vhost has decided — the swap is committed, or it has been
# undone — so the net comes down and the state it would have restored from goes with it.
#
# It CLEARS these four traps rather than restoring whatever was there before, which is correct
# only while nothing else in the installer sets one: install.sh sources every step file into one
# shell (its run_step), and neither install.sh nor any file under installer/lib installs a trap
# today. A step that adds one has to make this function put it back instead.
disarm_vhost_guard() {
  trap - EXIT INT TERM HUP
  MARAN_VHOST_GUARD_DEST=""
  MARAN_VHOST_GUARD_ROLLBACK=none
  MARAN_VHOST_GUARD_RENDERED=""
  MARAN_VHOST_GUARD_ENTRY_KEPT=0
  MARAN_VHOST_GUARD_SWAPPED=0
}

# vhost_guard_fired: the trap body — the shell is leaving the swap window without a decision.
#
# `$1` is what fired it: one of the three signals an interrupted install actually arrives as —
# INT (Ctrl-C at the terminal), TERM (a `kill`, a systemd stop, a container shutdown) and HUP (a
# dropped SSH session) — or EXIT for a shell leaving for any other reason, `set -e` on a command
# inside the window included. Those are two different events and they are worded differently: an
# `install` that fails on a full /etc is not an interrupt, and calling it one sends the operator
# looking for a signal nobody sent.
#
# The guard is DISARMED FIRST, before anything is put back. Two reasons, both measured: a
# restoration that fails under `set -e` would otherwise abort into the still-armed EXIT trap and
# attempt the same failing restoration again, and a second Ctrl-C during the restoration would
# start a second one on top of the first. After the disarm, a failing restoration is reported
# rather than fatal, and a second signal lands on the default disposition.
#
# The signal is RE-RAISED after the restoration rather than swallowed. Two reasons: the process
# was asked to die, and an installer that answered Ctrl-C by carrying on to the next step would
# be a worse surprise than the one this guard prevents; and the parent gets the status it expects
# — 130, 143, 129 — instead of a plain 1 that says nothing about what happened. `$BASHPID`, not
# `$$`: the latter is the PID of the ORIGINAL shell, so inside a subshell it signals the wrong
# process — measured, the outer shell died and the subshell carried on with the candidate served.
vhost_guard_fired() {
  local status=$?
  local signal="$1"

  # Disarmed. The traps are cleared then too, so this is the belt beside that brace.
  if [ -z "$MARAN_VHOST_GUARD_DEST" ]; then
    return "$status"
  fi

  local dest="$MARAN_VHOST_GUARD_DEST" rendered="$MARAN_VHOST_GUARD_RENDERED"
  local rollback="$MARAN_VHOST_GUARD_ROLLBACK" swapped="$MARAN_VHOST_GUARD_SWAPPED"
  local entry_kept="$MARAN_VHOST_GUARD_ENTRY_KEPT"
  disarm_vhost_guard

  if [ "$signal" = EXIT ]; then
    echo "80-nginx.sh: the install stopped (status ${status}) while the panel vhost was being
swapped in." >&2
  else
    echo "80-nginx.sh: interrupted (${signal}) while the panel vhost was being swapped in." >&2
  fi

  # Not a bare command: a failing restoration under `set -e` would take the rest of this handler
  # with it — no message, no re-raise, exit 1 instead of 143, and the never-validated candidate
  # left served. The one path whose job is to leave the operator informed must not be the one that
  # dies silently.
  if ! restore_panel_vhost "$dest" "$rollback" "$swapped" "$entry_kept"; then
    echo "80-nginx.sh: could not put ${dest} back; it may hold a vhost nothing has validated. Look
at ${dest}${MARAN_VHOST_PREVIOUS_SUFFIX} and ${dest}${MARAN_VHOST_ADOPTED_SUFFIX} before restarting
nginx." >&2
  fi
  rm -f "$rendered"

  if [ "$signal" = EXIT ]; then
    return "$status"
  fi
  kill -s "$signal" "$BASHPID"
  # Unreachable while the trap above is cleared and the signal is fatal, and here anyway:
  # resuming an interrupted install past this point is the one thing this function must never do.
  exit 1
}

# install_validated_vhost: put the rendered vhost at the path nginx serves it from, prove the
# real configuration tree still loads with it there, and put back exactly what was there before
# if it does not. This is rules/rust.md's config-write protocol — render, swap, validate, roll
# back — spelled in shell for the one config file the installer owns rather than the agent.
#
# THE SWAP PRECEDES THE VALIDATION, and that order is the whole correctness of this function.
# `nginx -t` is not given a file to check: it parses nginx.conf and everything the `include`
# directives glob in, which on both supported families is `/etc/nginx/conf.d/*.conf`. A
# candidate parked under a name that glob does not match is a file nginx never opens. This step
# used to write the render to `maran.conf.staging` and then run `nginx -t`: the test parsed the
# tree WITHOUT the new vhost — or, on a re-install, WITH the old one still in place — reported
# success, and the step then moved a file nothing had ever read over the served path. Measured
# on both polygon images: a vhost carrying an unknown directive installed with `nginx -t`
# printing "test is successful", and broke the panel at the next reload, with that successful
# test in the install log as evidence that it was fine.
#
# Validating after the swap is safe because nginx does not read a configuration file until it
# is asked to. Between the rename below and the reload in step_nginx the disk has changed and
# the running server has not, so a refusal is fully recoverable by restoring the previous bytes,
# which is what happens here before the install stops. The window in which a refused vhost is
# on disk is bounded by one `nginx -t` — 6 ms on an empty host, a third of a second on one
# serving 500 sites — nothing in this step reloads inside it, and arm_vhost_guard is what keeps
# an interrupt inside it from making that window last forever.
#
# The vhost is installer-owned and this function overwrites it WHOLESALE: an operator's hand
# edits to the served file are gone after the next install, and `.previous` is deleted the moment
# the new render validates, so no copy of them is kept anywhere. That is deliberate — the file is
# rendered from a template this repository ships, and merging somebody's edits into a render is
# not something an installer can do correctly — but it is NOT what generate_self_signed_cert does
# with a certificate, which is left alone precisely because an operator may have swapped in a real
# one. The difference is written down so that it stays a decision: a customised panel vhost
# belongs in a separate file under conf.d, which this step never touches. The same goes for the
# three working names: `<dest>.candidate`, `<dest>.previous` and `<dest>.adopted` are this step's own
# scratch space. A file an operator parks under one of them — `maran.conf.previous` is the name a
# human picks for a backup — is NOT consumed or overwritten: vhost_is_ours refuses to account for
# it, park_foreign_file moves it to `<dest>.foreign`, the operator is told the new name, and the
# install carries on. Nothing under any of those names is ever served without `nginx -t` having
# been asked about the tree with it in place.
install_validated_vhost() {
  local rendered="$1" dest="$2"
  local candidate="${dest}${MARAN_VHOST_CANDIDATE_SUFFIX}"
  local previous="${dest}${MARAN_VHOST_PREVIOUS_SUFFIX}"
  local adopted="${dest}${MARAN_VHOST_ADOPTED_SUFFIX}"
  local rollback=none entry_kept=0 parked

  # A leftover candidate is a render nothing ever validated and this run is about to make its own.
  # Through claim_working_name rather than `rm -f`, so that anything at that name which is not a
  # plain file — a directory, a symlink — is parked instead of being written into, written THROUGH,
  # or failing an `rm` after the install has already succeeded.
  #
  # ONE name, not two: a leftover `<dest>.adopted` is emphatically NOT claimed here, and the
  # comment that used to say "its own of each" described a guard this code does not apply. It must
  # not: `.adopted` is the last panel vhost an install of this product validated, held aside by a
  # run that was killed before it could hand it back, and deleting it is round 2's defect exactly.
  # It is promoted back to `<dest>.previous` below and judged by vhost_is_ours there, which is
  # where anything at that name which is not a plain file gets parked instead.
  claim_working_name "$candidate"

  # A `<dest>.adopted` still on disk is a leftover a run held aside and was killed before it could
  # hand back: restore_panel_vhost returns one to `<dest>.previous` on every path it takes, so this
  # is what a SIGKILL or a power cut between those two renames leaves. It goes back to the name the
  # decision below reads, and it is never deleted here — deleting it is exactly the round-2 defect
  # (the only validated copy on the machine, removed by the run that came to help). If `.previous`
  # is occupied too, the older of the two is parked rather than either being lost.
  #
  # EVERY GATE FROM HERE ON ASKS ABOUT THE LINK AND NOT ONLY ABOUT WHAT IT RESOLVES TO, and that is
  # not belt and braces: `[ -e ]`
  # RESOLVES the link and is false for a dangling one, so a symlink whose target has gone — an
  # operator's backup pointer into a volume since unmounted — reads as "nothing is there" and the
  # rename that follows destroys the link itself, silently. park_foreign_file learned this about
  # its own collision test one round ago; the correction belongs at the gates that decide whether
  # it is ever called, or the parking it protects is never reached. It is the same defect
  # vhost_is_ours had with `[ -f ]`, in the direction of absence rather than of type.
  #
  # A symlink at `<dest>.adopted` is parked outright rather than promoted, whether or not it
  # resolves: `.adopted` means "a vhost an install of this product wrote and validated", nothing
  # this step writes there is ever a link, and promoting one would carry it to the name whose
  # occupant the decision below reads. Parking is also the only handling that does not fail: a
  # dangling link cannot be `sync`ed, so move_into_place would abort the install under `set -e`.
  if [ -L "$adopted" ]; then
    parked="$(park_foreign_file "$adopted")"
    echo "80-nginx.sh: ${adopted} is one of this step's working names and what is there is a
symlink, which this step never follows and never serves. It has been moved to ${parked} and left
alone — the link, not what it points at — and the install continues." >&2
  elif [ -e "$adopted" ]; then
    if [ -e "$previous" ] || [ -L "$previous" ]; then
      parked="$(park_foreign_file "$adopted")"
      echo "80-nginx.sh: an earlier run left a rollback copy at ${adopted} and ${previous} is
occupied too. The second has been moved to ${parked} and left alone; this run reads ${previous}." >&2
    else
      move_into_place "$adopted" "$previous"
    fi
  fi

  # WHICH LEFTOVER, IF ANY, IS WORTH PREFERRING to the entry bytes on a rollback. It decides one
  # thing only — which copy restore_panel_vhost tries FIRST — and, unlike the round-3 version, it
  # can no longer decide whether a copy of the served file is taken at all.
  #
  # A leftover `<dest>.previous` is also the ONLY thing that makes this run ask nginx anything
  # before it touches the served path, and the question is asked here rather than unconditionally
  # at the top for a measured reason: an extra `nginx -t` on every ordinary install is also the
  # first one the polygon's interrupt shim meets, which would move every interrupt case in the
  # suite to before the swap and retire the check on the guard.
  #
  # PROVENANCE IS DECIDED BY vhost_is_ours, which is a statement about the file's own bytes: its
  # first line is the marker this installer's render_vhost stamps, and the SHA-256 in that line is
  # the digest of everything after it. That is true of a file some run of this installer rendered,
  # whole and unmodified, and of nothing else. It is not forgeable by the three accidents that
  # produce a `<dest>.previous` this step must not serve:
  #
  #   * an ordinary re-install — writes the marker over a vhost it rendered itself, so the file
  #     genuinely IS ours and a true answer is the right one;
  #   * an operator's manual edit of the served vhost — the body changes and the digest stops
  #     matching, so an edited file is correctly no longer ours, and an operator's own backup under
  #     that name carries no marker at all;
  #   * a half-finished run — a truncated or null-padded copy fails the digest, because the marker
  #     describes bytes that are no longer all there. This is the fragment an earlier round of this
  #     step was measured serving under the words "is as it was before this step ran".
  #
  # What it does not claim is unforgeability against root; see MARAN_VHOST_MARKER_PREFIX. It is a
  # guard against those three accidents, not an authenticator, and the worst a forged marker buys
  # is that a file gets TRIED — try_restore_from still refuses to leave it served unless nginx
  # loads the tree with it.
  #
  # `|| [ -L ]` for the reason given at the gate above: without it a DANGLING link at this name is
  # not examined, not parked, not named to the operator, and the entry copy's `mv -f` a few lines
  # down replaces the link itself. vhost_is_ours answers 1 for any link, so the parking below
  # follows for free once the branch is entered at all.
  if [ -e "$previous" ] || [ -L "$previous" ]; then
    if ! vhost_is_ours "$previous"; then
      parked="$(park_foreign_file "$previous")"
      echo "80-nginx.sh: ${previous} is not a panel vhost this installer wrote — it carries no
marker of ours, its bytes have changed since one was written, or it is not a file of its own (a
symlink under this name is never followed and never served). It has been moved to ${parked} and
left alone; this step never serves a file it cannot account for. Nothing renames it back and an
uninstall leaves it where it is, so it is yours to keep or remove. The install continues." >&2
    elif nginx_tree_loads; then
      # A leftover copy beside a configuration that loads. There is nothing to recover — whatever
      # is served today, nginx accepts it — so this run restores from its own entry copy, as every
      # ordinary run does. Said out loud because the file being removed was described by an earlier
      # round as the one file worth having.
      echo "80-nginx.sh: replacing the leftover rollback copy at ${previous}: 'nginx -t' passes on
this host as it stands, so there is nothing in it to recover." >&2
      discard_rollback_copy "$previous"
    else
      # A leftover `.previous` this installer wrote, AND a tree that does not load: the state a
      # killed install leaves behind, where the file on the served path is the candidate that run
      # never validated and this one is the last vhost an install actually put through `nginx -t`.
      # Deleting it, which is what this step used to do, destroyed the only validated copy on the
      # machine. It is kept, it is preferred to the entry bytes on a refusal, and it is still
      # checked against the tree before it is ever left served: see restore_panel_vhost.
      move_into_place "$previous" "$adopted"
      rollback=adopted
      echo "80-nginx.sh: ${previous} is a panel vhost an install of this product wrote and did not
finish putting in place, and 'nginx -t' does not pass on this host as it stands. It has been kept,
at ${adopted}, as this run's first choice on a rollback — to be left served only if nginx loads the
tree with it." >&2
    fi
  fi

  # THE ENTRY COPY, UNCONDITIONALLY, AND `<dest>.previous` IS NOW FREE FOR IT. Whatever is on the
  # served path when this run first looks at it is copied there before anything is touched, on every
  # path that goes on to swap. No predicate decides this, and that is the point: the round-3 version
  # took its copy only on one of two branches, chose the branch by asking whether `nginx -t` passed
  # on the host — a question about the whole tree, not about this file — and so took NO copy of a
  # healthy panel vhost whenever some other file under conf.d was broken, which is one of the
  # commonest reasons to re-run an installer. It then overwrote that vhost and, when its own render
  # was refused, left the served path empty with no copy of it anywhere on the machine.
  #
  # "Is this file mine" and "does the tree load" are two questions, and neither is asked here. The
  # first is asked about the LEFTOVER above, which is a different file; the second only chooses
  # which copy is tried first.
  #
  # A SYMLINK ON THE SERVED PATH is not copied and not written through; it is parked, like every
  # other name here holding something this step did not put there. `install` follows a link and
  # copies the TARGET's bytes, `mv -f` afterwards replaces the LINK — so under the old `[ -e ]`
  # gate a live link was consumed and a dangling one was destroyed without ever being looked at,
  # in both cases with no word to the operator. Parking keeps the link intact under a name nginx
  # never opens, and it is the same answer claim_working_name gives for `<dest>.candidate` and
  # vhost_is_ours gives for `<dest>.previous`: this step serves nothing through a link out of the
  # directory it owns, and it deletes nothing it cannot account for.
  #
  # No entry copy is taken in that case, and nothing below claims one was: `entry_kept` stays 0,
  # so restore_panel_vhost's sentences about the entry bytes are not printed and its "nothing of
  # this step's to put back" arm is what an operator sees.
  if [ -L "$dest" ]; then
    parked="$(park_foreign_file "$dest")"
    echo "80-nginx.sh: ${dest} — the path this step serves the panel vhost from — was a symlink.
This step never writes through a link out of the directory it owns and never serves one, so the
link itself has been moved to ${parked}, whatever it pointed at is untouched, and this run installs
a regular file at ${dest}. Nothing puts the link back; it is yours to keep or remove." >&2
  elif [ -e "$dest" ]; then
    write_rollback_copy "$dest" "$previous"
    entry_kept=1
    if [ "$rollback" != adopted ]; then
      rollback=copied
    fi
  fi

  # From here the served path is about to change, so the net goes up: see arm_vhost_guard for what
  # it covers and, just as importantly, what it does not.
  arm_vhost_guard "$dest" "$rollback" "$rendered" "$entry_kept"

  # Into the target's OWN directory first: a rename within one directory is atomic, while a
  # copy across filesystems can be read half-written by an nginx that reloads at that instant.
  # Through move_into_place, so that the fsync of the staged file and of `/etc/nginx/conf.d` that
  # rules/rust.md "Config writes: render → swap → validate" requires reaches THE FILE THIS STEP
  # EXISTS TO INSTALL, and not only the rollback copy beside it.
  install -m 0644 "$rendered" "$candidate"
  move_into_place "$candidate" "$dest"
  # The served path has changed; from here a restoration has something to undo. On the line after
  # the rename rather than before it, because until the rename there is nothing to put back and a
  # guard that restored anyway would replace a vhost this step never wrote.
  MARAN_VHOST_GUARD_SWAPPED=1

  if nginx -t 2>&1; then
    disarm_vhost_guard
    # The served path now holds a vhost nginx has loaded the whole tree with, durably. Both of this
    # run's working copies are superseded by it, and both removals are made durable too — an unlink
    # still in the page cache is an unlink a crash undoes, and what comes back is a stale copy the
    # next run would find and consider.
    if [ "$entry_kept" -eq 1 ]; then
      discard_rollback_copy "$previous"
    fi
    if [ "$rollback" = adopted ]; then
      discard_rollback_copy "$adopted"
    fi
    return 0
  fi

  echo "80-nginx.sh: nginx refused the rendered panel vhost; its own message is above." >&2
  if ! restore_panel_vhost "$dest" "$rollback" "$MARAN_VHOST_GUARD_SWAPPED" "$entry_kept"; then
    echo "80-nginx.sh: could not put ${dest} back; it holds a vhost nothing has validated. Look at
${previous} and ${adopted} before restarting nginx." >&2
  fi
  disarm_vhost_guard
  # The render itself, which step_nginx would have removed after this function returned — and
  # this function does not return, it exits. Left behind, it is a copy of the panel's vhost in
  # /tmp for as long as the machine is up.
  rm -f "$rendered"
  echo "80-nginx.sh: the install stops here." >&2
  exit 1
}

step_nginx() {
  echo "Installing nginx vhost on port ${MARAN_PANEL_PORT}..."
  # First, so that the validation below parses a tree that already includes the agent's
  # directory: a validation that passes without it has proved nothing about the
  # configuration nginx will actually run.
  install_agent_config_include
  generate_self_signed_cert

  local dest tmp
  dest="$(nginx_conf_dest)"
  tmp="$(mktemp)"
  render_vhost "$tmp"
  # Removes the render on both of its own ways out — it does not return on a refusal, it exits —
  # so this line is what cleans up after the successful one.
  install_validated_vhost "$tmp" "$dest"
  rm -f "$tmp"

  # AFTER install_validated_vhost has returned, and nowhere else. A reload between the rename and
  # the end of the validation is the one way unvalidated configuration reaches a running server,
  # which is why the polygon's systemctl stand-in records `reload` and the assertion requires no
  # record of one on every path that refuses.
  systemctl enable nginx
  systemctl reload nginx 2>/dev/null || systemctl restart nginx
  echo "nginx vhost installed at ${dest}; panel reachable on port ${MARAN_PANEL_PORT}."
}
