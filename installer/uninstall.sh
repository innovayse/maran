#!/usr/bin/env bash
# Maran uninstaller. Stops and removes everything the installer created —
# services, binaries, units, the nginx vhost, the system user — but never deletes
# customer data (the PostgreSQL database, /home/* hosting accounts, backups) without
# an explicit, separate confirmation for each category. "Honest" here means: default
# to keeping data, ask before destroying anything an operator cannot get back.
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
  echo "uninstall.sh: must be run as root (try: sudo bash uninstall.sh)" >&2
  exit 1
fi

# The service account and group this product creates, the names it used before the rename, and
# the PostgreSQL role (which is the account's name, because peer authentication matches them).
#
# Spelled here as well as in install.sh because the uninstaller is its own entry point and sources
# nothing from the installer — sourcing install.sh would RUN it. The two files are held together
# by an assertion in docker/polygon/assert-installer-steps.sh, which fails the image build when
# they disagree, in place of a hope that both get edited at once.
MARAN_USER=maran
MARAN_GROUP=maran
MARAN_LEGACY_USER=panel
# The GECOS install.sh stamps on the account it creates. The uninstaller removes an account of its
# own name ONLY when it carries this marker: on a host where the operator refused the rename and
# adopted their own account, or where somebody else's `maran` outlived a failed install, deleting
# it would destroy a principal this product never created.
MARAN_SERVICE_ACCOUNT_MARKER="Maran panel service account"

MARAN_ASSUME_YES=0
for arg in "$@"; do
  case "$arg" in
    --yes|-y) MARAN_ASSUME_YES=1 ;;
  esac
done

# confirm: interactive yes/no gate for destructive, data-loss actions. --yes on the
# command line is an explicit opt-out for scripted/CI teardown of disposable test
# hosts; it is never the default.
confirm() {
  local prompt="$1"
  if [ "$MARAN_ASSUME_YES" -eq 1 ]; then
    return 0
  fi
  local reply
  read -r -p "${prompt} [y/N] " reply </dev/tty || reply="n"
  [[ "$reply" =~ ^[Yy]$ ]]
}

stop_and_disable_services() {
  echo "Stopping Maran services..."
  systemctl stop maran-api.service maran-agent.service 2>/dev/null || true
  systemctl disable maran-api.service maran-agent.service 2>/dev/null || true
}

remove_systemd_units() {
  echo "Removing systemd units..."
  rm -f /etc/systemd/system/maran-api.service /etc/systemd/system/maran-agent.service
  # The api's socket directory is built by a tmpfiles snippet rather than by the unit's
  # RuntimeDirectory=, so removing the unit does not remove the directory: both go here.
  # Left behind, the snippet would go on recreating an empty 2710 directory on every boot of a
  # machine that no longer has a panel — group-owned by the web server, which is exactly the kind
  # of leftover an operator cannot explain.
  rm -f /etc/tmpfiles.d/maran-api.conf
  rm -rf /run/maran-api
  systemctl daemon-reload
}

remove_nginx_vhost() {
  echo "Removing nginx vhost..."
  rm -f /etc/nginx/conf.d/maran.conf
  # And the two working names step 80 swaps the vhost through (its MARAN_VHOST_CANDIDATE_SUFFIX
  # and MARAN_VHOST_PREVIOUS_SUFFIX). An install killed by something no trap can catch — SIGKILL,
  # a power cut — leaves one of them behind, and only the next INSTALL used to clear it. An
  # operator who interrupts an install and then uninstalls was left with a copy of the panel's
  # vhost in /etc/nginx forever, after a step whose whole promise is that it cleans up after
  # itself. Neither name ends in `.conf`, so nginx never parsed either one; this is litter, not
  # exposure — but it is litter with the panel's hostname and certificate paths in it.
  rm -f /etc/nginx/conf.d/maran.conf.candidate /etc/nginx/conf.d/maran.conf.previous \
        /etc/nginx/conf.d/maran.conf.adopted
  # NOT `maran.conf.foreign*`. Those are files the installer found under one of its own working
  # names and could not account for — an operator's own backup, most likely, or a symlink step 80
  # refuses to follow — moved aside rather than overwritten. They were never ours to write and they
  # are not ours to remove. So they outlive the uninstall, one per run that met one, which is the
  # deliberate half of the trade step 80's MARAN_VHOST_FOREIGN_SUFFIX describes: an uninstaller
  # that deleted them would be doing the one thing the rename exists to avoid, a whole install
  # later and with nobody watching. They live in `conf.d`, which both families glob as
  # `conf.d/*.conf`, and none of them ends in `.conf`, so nginx opens none of them — the ground is
  # that pairing and not "nginx globs only `*.conf`", which is false on debian, whose nginx.conf
  # also includes `sites-enabled/*` with no extension filter. The operator was told each name when
  # it was made.
  # And the include pointing at the agent's vhost directory. It goes with the panel's own
  # vhost rather than being left behind: /etc/maran is removed further down, and an
  # include naming a directory that no longer exists makes every later `nginx -t` on this
  # host fail — including ones that have nothing to do with Maran.
  rm -f /etc/nginx/conf.d/maran-sites.conf
  systemctl reload nginx 2>/dev/null || true
}

# The directory installer/lib/87-firewall.sh renders into, and the two files it renders, named
# once. Everything that deletes any of it goes through the one predicate below.
MARAN_CONFIG_DIRECTORY="/etc/maran"
MARAN_FIREWALL_RENDERED_FILES="${MARAN_CONFIG_DIRECTORY}/firewall.nft ${MARAN_CONFIG_DIRECTORY}/firewall-bans.nft"

# The files the two families' nftables.service loads at boot, plus the include directories their
# own packages ship. Where the search STARTS, not where it ends: maran_nftables_config_files
# follows every include out of them, because that is what the boot does.
#
# Measured on both polygon images: Debian has /etc/nftables.conf and nothing else, RHEL has
# /etc/sysconfig/nftables.conf plus /etc/nftables/*.nft. The shipped directories are seeds as
# well as followed files, because a distribution may drop a file in one without anything
# including it yet — and a seed that turns out to be unreachable only ever keeps a file.
MARAN_NFTABLES_CONFIG_SEEDS="/etc/nftables.conf /etc/sysconfig/nftables.conf \
/etc/nftables/*.nft /etc/nftables.d/*.nft /etc/sysconfig/nftables.d/*.nft"

# maran_include_paths: every path the `include` lines of one file name, as `<line>\t<path>`,
# taken the way nft takes them.
#
# The path is the text BETWEEN the quotes, and that is the correction of a real defect rather
# than a tidy-up. The reader this replaces stripped a leading and a trailing `"` off the whole
# remainder of the line, so an operator's
#
#     include "/etc/maran/firewall.nft" # panel rules
#
# yielded the path `/etc/maran/firewall.nft" # panel rules`, which matched nothing this script
# was about to delete — while real nft read the very same line as `/etc/maran/firewall.nft` and
# found it missing at the next boot. Measured, with the real uninstaller and real nft: the file
# was deleted, the include stayed, and `nft -c -f` answered `Error: File not found`. A predicate
# that reads an include line differently from the program that will execute it is not a
# predicate about this host.
#
# Every quoted token on the line is taken, not merely the first, and a line with no quotes at
# all contributes its first bare word. Both are deliberately over-eager: nft accepts neither
# shape, so nothing that matters is being modelled — and over-matching can only ever KEEP a
# file, which is the safe direction for every caller here.
maran_include_paths() {
  awk '
    /^[[:space:]]*include[[:space:]]/ {
      rest = $0
      sub(/^[[:space:]]*include[[:space:]]+/, "", rest)
      quoted = 0
      while (match(rest, /"[^"]*"/)) {
        print FNR "\t" substr(rest, RSTART + 1, RLENGTH - 2)
        rest = substr(rest, RSTART + RLENGTH)
        quoted = 1
      }
      if (!quoted) {
        sub(/[[:space:]].*$/, "", rest)
        if (rest != "") { print FNR "\t" rest }
      }
    }
  ' "$1"
}

# maran_absolute_path: <path> made absolute against <base_directory>, with `.`, `..` and
# repeated slashes resolved.
#
# Relative because nft accepts relative includes and resolves them against the directory of the
# file doing the including — measured on both families, from three different working
# directories, all three resolving the same way. A reader that ignored that would follow the
# graph to the wrong file, or to none.
#
# Resolved TEXTUALLY, and by hand rather than through a tool: the path may name a file that is
# not there, which is the very state this script exists not to create, so a resolver that needs
# the file to exist could not answer for it. Symlinks are therefore not followed here;
# maran_path_is_kept compares device and inode as a second chance, which is what catches those.
maran_absolute_path() {
  local path="$1" base="$2" out="" part rest
  case "$path" in
    /*) ;;
    *) path="${base}/${path}" ;;
  esac
  rest="$path"
  while [ -n "$rest" ]; do
    part="${rest%%/*}"
    if [ "$part" = "$rest" ]; then
      rest=""
    else
      rest="${rest#*/}"
    fi
    case "$part" in
      '' | .) ;;
      ..) out="${out%/*}" ;;
      *) out="${out}/${part}" ;;
    esac
  done
  printf '%s\n' "${out:-/}"
}

# maran_nftables_config_files: every configuration file the next `nft -f` of this host's
# nftables service would READ, one per line — the seeds above and everything reachable from
# them through `include`.
#
# Transitive, and that is the second door this file has had to close. The fixed list of files
# it replaces could not see an operator whose /etc/nftables.conf says
#
#     include "/etc/nft-operator/maran.nft"
#
# with the two Maran includes inside THAT file: the search looked at five paths, none of them
# was it, so nothing appeared to include the rendered files and both were deleted. Reproduced
# with the real script and real nft — rc 0, nothing printed, and the resulting host answers
# `Error: File not found` with the whole load aborted. nft follows includes and so must the
# question "does anything still include this".
#
# Every file is visited once. `seen` is newline-delimited and the paths come from lines, so a
# path can never contain the delimiter; an include cycle therefore terminates rather than
# looping, which nft itself does not promise.
maran_nftables_config_files() {
  local -a pending=()
  local seen="" file directory number path match
  local newline='
'
  local tab
  tab="$(printf '\t')"

  # Unquoted on purpose: the constant is a list of paths and globs, and the words are the paths.
  # shellcheck disable=SC2086
  for file in $MARAN_NFTABLES_CONFIG_SEEDS; do
    if [ -f "$file" ]; then
      pending+=("$file")
    fi
  done

  while [ "${#pending[@]}" -gt 0 ]; do
    file="${pending[0]}"
    pending=("${pending[@]:1}")
    case "$seen" in
      *"${newline}${file}${newline}"*) continue ;;
    esac
    seen="${seen}${newline}${file}${newline}"
    printf '%s\n' "$file"

    directory="$(dirname "$file")"
    while IFS="$tab" read -r number path; do
      [ -n "$path" ] || continue
      path="$(maran_absolute_path "$path" "$directory")"
      case "$path" in
        *[*?[]*)
          # An unquoted expansion on purpose: a pattern must become the files it names before
          # anything can be read from them.
          for match in $path; do
            if [ -f "$match" ]; then
              pending+=("$match")
            fi
          done
          ;;
        *)
          if [ -f "$path" ]; then
            pending+=("$path")
          fi
          ;;
      esac
    done <<< "$(maran_include_paths "$file")"
  done
  return 0
}

# maran_firewall_includers: every `include` line naming something under /etc/maran, anywhere in
# the graph above, printed as `<file>:<line>:<text>`. Nothing printed means nothing includes it.
#
# It is the EVIDENCE, and maran_firewall_kept_paths below is the ANSWER; both read the same
# graph through the same reader, so the lines an operator is told to remove are exactly the
# lines that kept the files.
#
# Why any of it matters: `nft -f` on an include whose target is missing is not a warning. It is
# `Error: File not found`, rc 1, and the ENTIRE load aborts — so the operator's own tables in
# the same file do not load either. A host left that way has nftables.service FAILED at its
# next boot and no firewall whatsoever, from a script whose whole promise is to leave the
# machine working.
maran_firewall_includers() {
  local file number path reported=""
  local newline='
'
  local tab
  tab="$(printf '\t')"
  while IFS= read -r file; do
    [ -n "$file" ] || continue
    while IFS="$tab" read -r number path; do
      [ -n "$path" ] || continue
      path="$(maran_absolute_path "$path" "$(dirname "$file")")"
      case "$path" in
        "${MARAN_CONFIG_DIRECTORY}"/*) ;;
        *) continue ;;
      esac
      # One line of evidence, however many paths that line names.
      case "$reported" in
        *"${newline}${file}:${number}${newline}"*) continue ;;
      esac
      reported="${reported}${newline}${file}:${number}${newline}"
      printf '%s:%s:%s\n' "$file" "$number" "$(sed -n "${number}p" "$file")"
    done <<< "$(maran_include_paths "$file")"
  done <<< "$(maran_nftables_config_files)"
  return 0
}

# maran_firewall_kept_paths: the paths under /etc/maran that this uninstall MUST leave behind,
# one per line, because an `include` line somewhere in this host's nftables configuration still
# names them. Empty output means every one of them may go.
#
# It answers with PATHS rather than with yes/no, and that is deliberate. A predicate that said
# only "something is still included" cannot say WHAT, so its callers would have to name the
# files they keep — and a hand-written list of names beside a search that matches more than
# those names is the same divergence in a smaller box. Measured, with the real script and real
# nft: a host whose only surviving line was `include "/etc/maran/mine.nft"` kept mine.nft, which
# something included, and deleted firewall.nft and firewall-bans.nft, which nothing did.
#
# NOTHING IN THIS SCRIPT DELETES ANYTHING UNDER /etc/maran WITHOUT ASKING THIS FIRST, and it is
# asked of the host rather than remembered — immediately before each deletion, which is the
# whole point of it. The defect that shape replaces was not a missing case, it was a decision
# that did not survive its own function: remove_firewall computed a `local left_wired`, printed
# "Keeping /etc/maran/firewall*.nft" on the strength of it, and four functions later
# remove_config_and_state ran `rm -rf /etc/maran` knowing nothing about it. The script stated
# the invariant in prose and broke it in the same run, at exit status 0. A flag one function
# owns is not a decision the program made; a question both deletions ask of the same host, at
# the moment they act, cannot answer them differently.
#
# It asks about INCLUDE LINES, not about our markers, and that is the other half of the same
# lesson. The marker-based gate it replaces skipped any target that did not carry
# `# BEGIN Maran firewall` — so a host whose operator had followed 87-firewall.sh's own advice
# ("remove both markers and everything between them") and left the two include lines behind was
# not seen at all, and both files were deleted at rc 0 with nothing printed.
#
# A glob is expanded here rather than kept as a pattern, because the callers compare paths: an
# `include "/etc/maran/*.nft"` names every .nft file that exists at this moment, and those are
# the ones a boot would miss. A pattern matching nothing stays itself and is dropped by the
# existence test, which is right — nft does not fail a wildcard include that matches no file,
# only a literal one. Measured on both families: rc 0 for the wildcard, rc 1 for the literal.
maran_firewall_kept_paths() {
  local file number path match
  local tab
  tab="$(printf '\t')"
  while IFS= read -r file; do
    [ -n "$file" ] || continue
    while IFS="$tab" read -r number path; do
      [ -n "$path" ] || continue
      path="$(maran_absolute_path "$path" "$(dirname "$file")")"
      case "$path" in
        "${MARAN_CONFIG_DIRECTORY}"/*) ;;
        *) continue ;;
      esac
      case "$path" in
        *[*?[]*)
          # An unquoted expansion on purpose: this is the one place a pattern must become the
          # files it names.
          for match in $path; do
            if [ -e "$match" ]; then
              printf '%s\n' "$match"
            fi
          done
          ;;
        *)
          printf '%s\n' "$path"
          ;;
      esac
    done <<< "$(maran_include_paths "$file")"
  done <<< "$(maran_nftables_config_files)"
  return 0
}

# maran_path_is_kept: 0 when <path>, or anything beneath it, is in the kept list. The
# beneath-it half matters for a directory: an include naming /etc/maran/nft.d/extra.nft keeps
# /etc/maran/nft.d, which the top-level sweep below would otherwise remove whole.
#
# The second comparison is by device and inode, and it is there for the spellings text cannot
# settle: a symlinked /etc/maran, or an include reaching the same file by another route. Both
# sides must exist for it to mean anything, which is exactly the case that can do harm — a
# path that is not there cannot be deleted.
maran_path_is_kept() {
  local path="$1" kept_paths="$2" kept
  [ -n "$kept_paths" ] || return 1
  while IFS= read -r kept; do
    [ -n "$kept" ] || continue
    case "$kept" in
      "$path" | "$path"/*) return 0 ;;
    esac
    if [ -e "$kept" ] && [ -e "$path" ] && [ "$kept" -ef "$path" ]; then
      return 0
    fi
  done <<< "$kept_paths"
  return 1
}

# remove_firewall_rendered_files: the two rendered files, each deleted unless the host says
# something still includes THAT file — asked here, now, not told by a caller.
remove_firewall_rendered_files() {
  local kept_paths path
  local -a keeping=()
  kept_paths="$(maran_firewall_kept_paths)"
  # Unquoted on purpose: the constant is a LIST of paths and the words are the paths.
  # shellcheck disable=SC2086
  for path in $MARAN_FIREWALL_RENDERED_FILES; do
    if maran_path_is_kept "$path" "$kept_paths"; then
      keeping+=("$path")
    else
      rm -f "$path"
    fi
  done
  [ "${#keeping[@]}" -gt 0 ] || return 0

  echo "Keeping ${keeping[*]}: a configuration file on this host still includes them, and"
  echo "deleting them would leave nftables unable to load ANYTHING at the next boot — the"
  echo "operator's own tables in the same file included. Remove these lines first, then"
  echo "delete the files:"
  maran_firewall_includers | sed 's/^/    /'
}

# remove_firewall: takes back everything installer/lib/87-firewall.sh put on the host,
# in the order that never leaves the machine less protected than it found it.
#
# 1. The include lines first, matched by the markers 87-firewall.sh wrote, from whichever
#    of the two targets this family has. Both are checked because this script does not
#    detect the OS family and does not need to: only our own block is ever removed, so a
#    file belonging to the other family is untouched by construction.
#
#    First, and that ordering is the one thing here worth arguing about. Every step below
#    can be interrupted safely once the include is gone: the next boot loads the
#    distribution's own configuration and simply does not have our tables. Delete the files
#    or the tables first and an interrupted uninstall leaves an include naming files that
#    are no longer there — which is not "no Maran firewall", it is nftables.service FAILED
#    and no firewall at all, from a script whose whole promise is to leave the host working.
# 2. The rendered files, which are ours entirely — but only once nothing on the host still
#    includes them, which remove_firewall_rendered_files asks rather than infers. The unwiring
#    above can refuse (a damaged marker pair, a candidate nft rejects), and a host may carry
#    the include lines without our markers at all, so "we tried to unwire" is not the same
#    fact as "nothing includes them now".
# 3. The live tables, so the kernel stops enforcing a policy that no longer has anything
#    managing it. Deleting them cannot lock anybody out: removing a table from a host whose
#    remaining rules are the distribution's own can only allow more.
# 4. The service, and ONLY if step 87 was the thing that enabled it. That is what the
#    marker records, and it is the difference between putting the host back as we found it
#    and taking away a firewall the operator had before Maran arrived.
#
# Runs before remove_config_and_state, which deletes /etc/maran — both the rendered files
# and the marker live there, and reading the marker after that directory is gone would
# make every uninstall decide "we did not enable it".
remove_firewall() {
  local nft="/usr/sbin/nft" target candidate
  local marker="/etc/maran/firewall-service-enabled-by-maran"
  local begin_marker="# BEGIN Maran firewall"
  local end_marker="# END Maran firewall"

  local unwired=0
  for target in /etc/nftables.conf /etc/sysconfig/nftables.conf; do
    [ -f "$target" ] || continue
    grep -q "^${begin_marker}" "$target" || continue
    echo "Removing the Maran include block from ${target}..."

    # Staged in the target's own directory: `mv` across filesystems is a copy, and a copy is
    # not atomic. Both are root-owned, so nothing is exposed by staging there.
    candidate="$(mktemp "$(dirname "$target")/.maran-nftables.XXXXXX")"

    # A state machine, not `sed '/BEGIN/,/END/d'`. A sed range whose end marker is missing
    # deletes to the END OF FILE — measured, that removed an operator's own `table inet mine`
    # written below a half-deleted block. An uninstaller destroying rules it never wrote is
    # worse than one that leaves its own block behind and says so.
    local strip_status=0
    awk -v begin="$begin_marker" -v end="$end_marker" '
      index($0, begin) == 1 { if (inside) { exit 3 } inside = 1; next }
      index($0, end) == 1   { if (!inside) { exit 4 } inside = 0; next }
      !inside               { print }
      END                   { if (inside) { exit 5 } }
    ' "$target" > "$candidate" || strip_status=$?
    if [ "$strip_status" -ne 0 ]; then
      rm -f "$candidate"
      echo "WARNING: the Maran markers in ${target} are not a matched pair, so the block was left alone."
      echo "         Removing from the opening marker to the end of the file could delete rules this"
      echo "         installer never wrote. Remove the block by hand, between the markers."
      continue
    fi

    # Validated before it replaces the live file, for the reason remove_sftp_sshd_block
    # validates its own edit: a host left with a configuration its daemon refuses is a host
    # somebody has to fix by other means, and "I uninstalled the panel" is a bad reason to
    # boot without a firewall. Capability complaints are not a verdict on the file — an
    # uninstall inside an unprivileged container still gets a clean removal.
    if [ -x "$nft" ]; then
      local check_output check_residue
      if ! check_output="$("$nft" -c -f "$candidate" 2>&1)"; then
        check_residue="$(printf '%s\n' "$check_output" | grep 'Error:' | grep -v 'Operation not permitted' || true)"
        if [ -n "$check_residue" ]; then
          rm -f "$candidate"
          echo "WARNING: removing the Maran block would leave a ${target} that nft rejects:"
          printf '%s\n' "$check_residue" | sed 's/^/         /'
          echo "         Left the file alone; remove the block between the markers by hand."
          continue
        fi
      fi
    fi

    chmod --reference="$target" "$candidate" 2>/dev/null || chmod 0644 "$candidate"
    chown --reference="$target" "$candidate" 2>/dev/null || true
    mv -f "$candidate" "$target"
    unwired=1
  done

  # Only once nothing includes them any more — asked of the host, here, after the unwiring
  # above has had its go at it. Both refusals above leave the block wired and both are
  # therefore visible to the question, and so is a host that never had our markers at all.
  remove_firewall_rendered_files

  if [ -x "$nft" ]; then
    echo "Removing the Maran nftables tables..."
    "$nft" delete table inet maran 2>/dev/null || true
    "$nft" delete table inet maran_bans 2>/dev/null || true
  fi

  if [ -f "$marker" ]; then
    echo "Disabling nftables: this installer enabled it."
    systemctl disable --now nftables 2>/dev/null || true
    rm -f "$marker"
  elif [ "$unwired" -eq 1 ]; then
    # Left enabled because it was enabled before us. Reloaded so the live ruleset matches
    # the file we just edited, rather than still holding our tables until the next boot.
    echo "Leaving nftables enabled: it was already enabled before Maran was installed."
    systemctl reload nftables 2>/dev/null || true
  fi
}

# remove_sftp_sshd_block: takes back the one edit the installer made to a file it
# does not own. Delimited by the same markers installer/lib/86-sftp.sh writes, so
# the removal is exactly the inverse of the install and cannot eat an operator's
# own configuration around it.
#
# Validated before it replaces the live file for the same reason it was validated
# on the way in: a host left with an sshd_config that sshd refuses is a host
# nobody can log in to fix, and "I uninstalled the panel" is a bad reason to lose
# a server.
remove_sftp_sshd_block() {
  local config="/etc/ssh/sshd_config"
  if [ ! -f "$config" ] || ! grep -q "^# BEGIN Maran SFTP" "$config"; then
    return
  fi
  echo "Removing the Maran SFTP block from ${config}..."
  local candidate sshd_bin
  candidate="$(mktemp)"
  sed '/^# BEGIN Maran SFTP/,/^# END Maran SFTP$/d' "$config" > "$candidate"
  chmod --reference="$config" "$candidate" 2>/dev/null || chmod 0600 "$candidate"
  chown --reference="$config" "$candidate" 2>/dev/null || true
  sshd_bin="$(command -v sshd || echo /usr/sbin/sshd)"
  if "$sshd_bin" -t -f "$candidate" >/dev/null 2>&1; then
    mv -f "$candidate" "$config"
    systemctl reload sshd 2>/dev/null || systemctl reload ssh 2>/dev/null || true
  else
    rm -f "$candidate"
    echo "WARNING: removing the Maran SFTP block would leave an sshd_config that 'sshd -t' rejects."
    echo "         Left the file alone; remove the block between the '# BEGIN Maran SFTP' and"
    echo "         '# END Maran SFTP' markers by hand."
  fi
}

# release_sftp_jails: stops and disables the per-account bind-mount units, then
# confirms nothing is still mounted under the jail root.
#
# This runs BEFORE anything deletes /var/lib/maran, and it is the most dangerous
# thing in this script if it is skipped: each jail has the account's REAL home
# bind-mounted at <jail>/home, so an `rm -rf /var/lib/maran` over a live mount
# deletes the customer's files through it. Unmounting first — and refusing to
# delete while any mount remains — is what keeps this uninstaller's promise that
# it never touches /home.
release_sftp_jails() {
  # The glob carries a LITERAL backslash, and it is not decoration. A `.mount`
  # unit's file name is systemd's escaping of its own mount point, and `-` is not
  # a path separator: `/var/lib/maran-sftp/<account>/home` escapes to
  # `var-lib-maran\x2dsftp-<account>-home.mount`, not to `var-lib-maran-sftp-…`.
  # Verified against `systemd-escape -p --suffix=mount` rather than derived by
  # hand. A glob written the readable way matches nothing, every unit survives the
  # uninstall, and each one is a bind mount of a customer home left behind on a
  # host the operator believes is clean.
  local unit
  for unit in /etc/systemd/system/'var-lib-maran\x2dsftp-'*.mount; do
    [ -e "$unit" ] || continue
    unit="$(basename "$unit")"
    echo "Stopping SFTP jail mount ${unit}..."
    systemctl disable --now "$unit" 2>/dev/null || true
    rm -f "/etc/systemd/system/${unit}"
  done
  systemctl daemon-reload 2>/dev/null || true
  # Belt and braces: a mount that systemd does not own (a hand-run `mount --bind`)
  # is still a route into a customer's home.
  local mount_point
  while read -r mount_point; do
    [ -n "$mount_point" ] || continue
    echo "Unmounting ${mount_point}..."
    umount "$mount_point" 2>/dev/null || true
  done < <(awk '$2 ~ "^/var/lib/maran-sftp/" { print $2 }' /proc/self/mounts 2>/dev/null | sort -r)
}

# sftp_jails_still_mounted: 0 when something under the jail root is still a mount
# point. Consulted by remove_var_lib, which must not recurse through one.
sftp_jails_still_mounted() {
  awk '$2 ~ "^/var/lib/maran-sftp/" { found = 1 } END { exit found ? 0 : 1 }' \
    /proc/self/mounts 2>/dev/null
}

# remove_sftp_group: the group installer/lib/86-sftp.sh created. Removed only when
# it is empty — a member left in it is an SFTP login this script did not create
# and has no business deleting, and `groupdel` on a group that is some user's
# primary group would fail anyway.
remove_sftp_group() {
  if ! getent group maran-sftp >/dev/null 2>&1; then
    return
  fi
  local members
  members="$(getent group maran-sftp | cut -d: -f4)"
  if [ -n "$members" ]; then
    echo "Keeping the 'maran-sftp' group: it still has members (${members})."
    echo "Those SFTP logins and their jails under /var/lib/maran-sftp are customer accounts;"
    echo "remove them through the panel before uninstalling, or by hand afterwards."
    return
  fi
  groupdel maran-sftp 2>/dev/null || true
  echo "'maran-sftp' group removed."
}

remove_binaries() {
  echo "Removing Maran binaries..."
  rm -rf /usr/local/maran
}

# remove_config_and_state: /etc/maran holds panel.env (the encryption key), agent.env and
# the panel's TLS key/cert; /run/maran is the agent's socket directory.
#
# Runs AFTER drop_database on purpose: the encryption key in panel.env is the only thing
# that can decrypt the secrets stored in the database, so an operator who chose to KEEP
# the database must be told, before the choice is irreversible, that the key is about to
# go with this directory.
remove_config_and_state() {
  if [ "$MARAN_DATABASE_KEPT" -eq 1 ]; then
    echo "WARNING: you kept the Maran database, but /etc/maran/panel.env holds the encryption key"
    echo "         for every secret stored in it. Back up /etc/maran/panel.env NOW if you intend to"
    echo "         reattach that database to a future install; without the key its secrets are lost."
    if ! confirm "Delete /etc/maran anyway (including the encryption key)?"; then
      echo "Keeping /etc/maran. Remove it yourself once the key is backed up."
      rm -rf /run/maran
      return
    fi
  fi
  echo "Removing config and runtime state (/etc/maran, /run/maran)..."
  remove_maran_config_directory
  rm -rf /run/maran
}

# remove_maran_config_directory: the ONLY place /etc/maran is deleted, and it deletes each
# entry through maran_path_is_kept — the same predicate, over the same host, that
# remove_firewall_rendered_files consulted a few calls earlier.
#
# It exists because the answer and the action had drifted apart: remove_firewall decided to
# keep /etc/maran/firewall*.nft, said so, and this function then deleted the whole directory
# four calls later at exit status 0, leaving the host's include block naming two files that
# were gone — nftables.service FAILED at the next boot, no firewall at all. The fix is not a
# flag passed from there to here, which would leave the NEXT function free to make the same
# mistake; it is that the deletion consults the predicate itself. There is nothing to keep in
# sync, because there is nothing remembered — and nothing to get wrong twice, because the
# entries this keeps are the entries the predicate named rather than a list written beside it.
#
# Whatever is still included stays and everything else in /etc/maran goes — panel.env above
# all, which holds the encryption key and must never be left on a host the panel has been
# removed from. What is left behind is the minimum the machine needs to boot, and the operator
# is told exactly which lines to remove to be rid of it.
remove_maran_config_directory() {
  [ -e "$MARAN_CONFIG_DIRECTORY" ] || return 0

  local kept_paths entry
  local -a keeping=()
  kept_paths="$(maran_firewall_kept_paths)"
  while IFS= read -r entry; do
    [ -n "$entry" ] || continue
    if maran_path_is_kept "$entry" "$kept_paths"; then
      keeping+=("$entry")
    else
      rm -rf "$entry"
    fi
  done <<< "$(find "$MARAN_CONFIG_DIRECTORY" -mindepth 1 -maxdepth 1)"

  if [ "${#keeping[@]}" -eq 0 ]; then
    # Nothing under it is named by an include any more, so the directory goes with its
    # contents. Reached through the same sweep as every other outcome: there is no branch here
    # that deletes without having asked.
    rm -rf "$MARAN_CONFIG_DIRECTORY"
    return 0
  fi

  echo "Kept ${keeping[*]} and removed the rest of ${MARAN_CONFIG_DIRECTORY}: an include on"
  echo "this host still names them, and nftables loads NOTHING at the next boot if an include"
  echo "names a file that is not there. Remove these lines, then the files, then the directory:"
  maran_firewall_includers | sed 's/^/    /'
}

# remove_ftps: take back everything installer/lib/89-ftps.sh added to this host, and
# nothing else.
#
# What goes: Maran's own unit (stopped and disabled first), the PAM service that is this
# daemon's entire authorization boundary, the rotation policy and the agent's config
# directory. The distribution's own vsftpd.service is UNMASKED, because the mask is
# Maran's edit to a unit Maran does not own — leaving a /dev/null symlink behind would
# leave the operator with a package they cannot start and no clue why.
#
# What deliberately STAYS, and why each is a decision rather than an omission:
#
#   /var/lib/maran-ftps  the per-account jails. Each one has a customer's real home
#                        bind-mounted at <account>/home, and an `rm -rf` across a live
#                        bind mount deletes the home it points at. This uninstaller has
#                        no unmount for them — the mount units are the agent's, named
#                        per account — so it says what is left and how to finish by hand,
#                        exactly as installer/lib/86-sftp.sh's report_legacy_jails does.
#   the FTPS logins      they are rows in /etc/passwd carrying the ACCOUNT's uid
#                        (`useradd --non-unique`), and the accounts under /home are
#                        customer data this script never touches. Deleting a login here
#                        would also be deleting a uid that still owns files.
#   the maran-ftps group those logins are members of it. A group removed while passwd
#                        rows still name its gid leaves dangling numeric ownership, and
#                        the group is worthless on its own: with the PAM service gone,
#                        membership authorizes nothing.
#
# The vsftpd PACKAGE is not removed either. It may have been on the host before Maran,
# and uninstalling a package an operator may be using elsewhere is not this script's
# call to make.
remove_ftps() {
  echo "Removing FTPS (installer/lib/89-ftps.sh)..."
  systemctl stop maran-ftps.service 2>/dev/null || true
  systemctl disable maran-ftps.service 2>/dev/null || true
  rm -f /etc/systemd/system/maran-ftps.service
  rm -f /etc/pam.d/maran-ftps
  # And the staging name install_ftps_pam_service renders into before the rename. A PAM stack is
  # staged and `mv -f`'d so no authenticator can read it half written, which means an install that
  # dies between those two lines leaves /etc/pam.d/maran-ftps.maran-staging on the host — and this
  # uninstaller would have left it there for good. It is inert (libpam resolves a service by its
  # NAME, and nothing names that one), so nothing would ever have reported it. The same choice the
  # nginx half already makes for its own .candidate/.previous/.adopted staging names.
  rm -f /etc/pam.d/maran-ftps.maran-staging
  rm -f /etc/logrotate.d/maran-ftps
  # Removed by name as well as by the /etc/maran sweep further down, so that an
  # uninstall which KEEPS /etc/maran (an nftables include still names a file in it)
  # does not leave this daemon's configuration directory behind.
  rm -rf /etc/maran/vsftpd
  # The mask is Maran's, so it goes with Maran. `systemctl unmask` on both families,
  # then the link itself if no working systemd client answered — the mask is a symbolic
  # link to /dev/null and nothing else, so this removes exactly what was added.
  systemctl unmask vsftpd.service 2>/dev/null || true
  if [ "$(readlink -- /etc/systemd/system/vsftpd.service 2>/dev/null || true)" = "/dev/null" ]; then
    rm -f /etc/systemd/system/vsftpd.service
  fi
  systemctl daemon-reload 2>/dev/null || true

  if [ -d /var/lib/maran-ftps ]; then
    echo "NOTE: the FTPS jails under /var/lib/maran-ftps are NOT removed, and neither are the"
    echo "      FTPS logins in /etc/passwd or the 'maran-ftps' group."
    echo "      Each jail has a hosting account's real home BIND-MOUNTED at <account>/home:"
    echo "      an rm -rf across one deletes the customer's files, not a copy of them."
    echo "      To finish by hand, per account:"
    echo "        1. systemctl disable --now <that account's var-lib-maran\\x2dftps-*.mount unit>"
    echo "        2. rm -f /etc/systemd/system/<that unit>  &&  systemctl daemon-reload"
    echo "        3. confirm nothing under /var/lib/maran-ftps appears in /proc/self/mounts"
    echo "        4. userdel each FTPS login (they share the account's uid, so the account's"
    echo "           own files are untouched), then groupdel maran-ftps"
    echo "      Only when step 3 reports nothing mounted: rm -rf /var/lib/maran-ftps"
  fi
}

# remove_var_lib: the api's own state directory, created by installer/lib/40-user.sh.
# Everything under it is derivable and rebuildable (rules/architecture.md: "Truth lives in
# PostgreSQL"), so it is removed unconditionally like the binaries — leaving it behind is
# what made a previous uninstall incomplete.
#
# The one exception to "everything under it is derivable": /var/lib/maran-sftp
# holds the per-account jails, and each jail has the account's real home
# bind-mounted inside it. release_sftp_jails has already unmounted them; if
# anything is somehow still mounted, this refuses rather than deleting a
# customer's files through a mount point.
remove_var_lib() {
  if sftp_jails_still_mounted; then
    echo "WARNING: something is still mounted under /var/lib/maran-sftp."
    echo "         NOT deleting /var/lib/maran: an rm -rf across a bind mount would delete the"
    echo "         customer home it points at. Unmount them and remove the directory by hand."
    return
  fi
  echo "Removing api state directory (/var/lib/maran)..."
  rm -rf /var/lib/maran
  # AgentPaths::SFTP_JAIL_ROOT, a SIBLING of the above since the chroot-ownership
  # defect (OpenSSH refuses a chroot under a panel-owned parent), so it needs a
  # removal of its own — while it was a child, deleting /var/lib/maran took it
  # with it. Guarded by the same still-mounted refusal above, for the same reason:
  # each jail has a customer's real home bind-mounted at <account>/home.
  echo "Removing SFTP jail base directory (/var/lib/maran-sftp)..."
  rm -rf /var/lib/maran-sftp
  # AgentPaths::BULK_SCRATCH_ROOT, a sibling of the above rather than a child of it
  # since the escalation in docs/superpowers/notes/2026-09-05-backups-threat-note.md.
  # It holds nothing but a killed run's leftovers — which are plaintext copies of a
  # customer's databases, so leaving them on an uninstalled host is the worst residue
  # this uninstaller could leave.
  echo "Removing agent scratch directory (/var/lib/maran-scratch)..."
  rm -rf /var/lib/maran-scratch
}

remove_logs() {
  # The rotation policies go unconditionally, before the prompt. They are Maran's files, not the
  # operator's data, and a policy left behind for a directory that may be about to disappear is
  # a daily cron error on a host that no longer runs this product. `missingok` in the policy
  # means it would not actually fail, which is exactly why leaving it would go unnoticed for
  # years.
  rm -f /etc/logrotate.d/maran-sites
  # The panel's own vhost logs — /var/log/maran/nginx-access.log and nginx-error.log, the pair
  # the prompt below names. Removed by the same literal that installed it
  # (MARAN_PANEL_LOGROTATE_DEST in installer/lib/80-nginx.sh), so this deletes our own payload
  # and nothing an operator put there under another name. It carries `missingok` exactly like
  # maran-sites, so leaving it would be the same silent daily rule pointing at nothing.
  rm -f /etc/logrotate.d/maran-panel

  # One prompt for the whole tree, all three parts of it: the root-owned parent (install.log and
  # the panel vhost's nginx logs), the panel-owned /var/log/maran/panel the API may have been
  # configured to write into, and the root-owned /var/log/maran/sites holding one directory per
  # hosting account of the logs nginx wrote for that account's sites. The split is an ownership
  # boundary, not three separate things an operator decides about separately.
  #
  # The site logs are a customer-facing record — what each of their domains served — so the
  # prompt names them: an operator decommissioning a server may well want to keep them, and
  # they are the one part of this tree that is not about the install itself.
  if confirm "Delete install, panel and per-account site logs under /var/log/maran?"; then
    rm -rf /var/log/maran
    echo "Logs removed."
  else
    echo "Keeping /var/log/maran (including per-account site logs under sites/)."
  fi
}

# drop_database: the panel's own PostgreSQL database and role. Asked separately from
# everything else because it is the one thing that cannot be re-downloaded — it is the
# customer's actual data (rules/architecture.md: "Truth lives in PostgreSQL").
# MARAN_DATABASE_KEPT: 1 once the operator has declined to drop the database, so
# remove_config_and_state knows the encryption key still has data to protect. Defaults to
# "kept" because a psql-less host reaches neither branch below and never lost the data.
MARAN_DATABASE_KEPT=1
# database_role_to_drop: the role that owns this host's Maran database, which is the account's
# name after the rename and `panel` on a host that was never upgraded. Asked of PostgreSQL rather
# than assumed, so an uninstall on a pre-rename host drops the role it actually has and an
# uninstall on a shared server never drops a role that owns nothing of ours. Empty when the
# database is absent, in which case there is nothing to drop.
database_role_to_drop() {
  runuser -u postgres -- psql -tAc \
    "SELECT pg_catalog.pg_get_userbyid(datdba) FROM pg_database WHERE datname='maran'" 2>/dev/null \
    | tr -d '[:space:]'
}
drop_database() {
  if ! command -v psql >/dev/null 2>&1; then
    return
  fi
  local MARAN_ROLE_TO_DROP
  MARAN_ROLE_TO_DROP="$(database_role_to_drop)"
  if [ -z "$MARAN_ROLE_TO_DROP" ]; then
    echo "No 'maran' database on this host; nothing to drop."
    return
  fi
  if confirm "DROP the Maran PostgreSQL database and role? This deletes all panel data permanently."; then
    # `runuser` (util-linux, always present), not `sudo` (a package a minimal Debian or RHEL
    # image does not have). And the failures are reported, not swallowed: `|| true` on both
    # DROPs followed by an unconditional "Database and role dropped." is a completion message
    # for work that may not have happened — the operator would delete the encryption key
    # believing the data behind it was gone. MARAN_DATABASE_KEPT stays 1 unless both DROPs
    # actually succeeded, so remove_config_and_state keeps treating the key as protecting data.
    if runuser -u postgres -- psql -c "DROP DATABASE IF EXISTS maran;" &&
       runuser -u postgres -- psql -c "DROP ROLE IF EXISTS ${MARAN_ROLE_TO_DROP};"; then
      MARAN_DATABASE_KEPT=0
      echo "Database and role dropped."
    else
      echo "WARNING: dropping the Maran database or role FAILED. Nothing was removed, or only" >&2
      echo "         part of it was. The panel data is still on this host; drop it by hand with" >&2
      echo "         psql as the postgres user once you have seen the error above." >&2
    fi
  else
    echo "Keeping the Maran PostgreSQL database and role."
  fi
}

# remove_service_user: the system account Maran created for the api. Never touches
# customer hosting accounts under /home — those are a separate, unrelated namespace
# this uninstaller does not enumerate or manage.
#
# Two names, because a host installed before the rename and never upgraded still carries the old
# one: the current account and, separately, the legacy one. Neither is removed on its name alone.
# The current account must carry the marker install.sh stamps — an account of that name this
# product did not create is somebody else's principal, and an uninstaller that deletes it does
# more damage than the install ever did. The legacy account has no marker to carry (the release
# that made it stamped none), so the evidence there is that it OWNED this product's state
# directory, which is the same evidence step 15 migrates on. That directory is deleted earlier in
# main(), so the answer is taken before anything is removed — see MARAN_LEGACY_USER_IS_OURS.
remove_service_user() {
  local gecos
  if id -u "$MARAN_USER" >/dev/null 2>&1; then
    gecos="$(getent passwd "$MARAN_USER" | cut -d: -f5)"
    if [ "$gecos" != "$MARAN_SERVICE_ACCOUNT_MARKER" ]; then
      echo "Keeping the '${MARAN_USER}' system user: it carries no marker of this installer, so"
      echo "  this product did not create it."
    elif confirm "Remove the '${MARAN_USER}' system user (its own home/state, not customer accounts)?"; then
      userdel "$MARAN_USER" 2>/dev/null || true
      echo "'${MARAN_USER}' system user removed."
    else
      echo "Keeping the '${MARAN_USER}' system user."
    fi
  fi
  if [ "${MARAN_LEGACY_USER_IS_OURS:-0}" -eq 1 ] && id -u "$MARAN_LEGACY_USER" >/dev/null 2>&1; then
    if confirm "Remove the legacy '${MARAN_LEGACY_USER}' system user this product created before it was renamed?"; then
      userdel "$MARAN_LEGACY_USER" 2>/dev/null || true
      echo "'${MARAN_LEGACY_USER}' system user removed."
    else
      echo "Keeping the '${MARAN_LEGACY_USER}' system user."
    fi
  fi
}

# MARAN_LEGACY_USER_IS_OURS: whether the pre-rename account exists AND owns /var/lib/maran, which
# is the only evidence available that it is this product's (that release stamped no marker).
# Recorded at the START of main, because the directory that carries the evidence is deleted
# halfway through it — reading it later would answer "no" for every host and quietly leave the
# account behind on exactly the hosts that have one.
MARAN_LEGACY_USER_IS_OURS=0
record_legacy_service_user() {
  local state="/var/lib/maran"
  id -u "$MARAN_LEGACY_USER" >/dev/null 2>&1 || return 0
  [ -d "$state" ] && [ ! -L "$state" ] || return 0
  [ "$(stat -c '%u' "$state")" = "$(id -u "$MARAN_LEGACY_USER")" ] || return 0
  MARAN_LEGACY_USER_IS_OURS=1
}

# MARAN_BACKUP_ROOT: the local backup root, the one directory in this file that is named in
# order NOT to be deleted. It is the same literal installer/lib/40-user.sh creates and
# LocalBackupRoot::resolve() canonicalizes; a spelling that drifted from those would make the
# report below describe a directory nobody writes to.
readonly MARAN_BACKUP_ROOT=/var/backups/maran

# note_backups_kept: prints the backup root, how many artifacts are under it, and the fact that
# they were left alone. It deletes nothing, and it is not behind `confirm`: there is no question
# here to ask.
#
# Why this exists as a function rather than a line in the block below. `remove_var_lib` deletes
# three sibling directories by name, and a fourth `rm -rf` beside them — of the one directory
# holding a customer's only copy of their data — would be one line and would look exactly like
# its neighbours. Nothing in this repository would have gone red for it. So the promise is
# stated here in a function the polygon can run and assert against a planted artifact
# (docker/polygon/assert-installer-steps.sh, assert_the_uninstaller_keeps_the_backup_root),
# which is what makes it a checked promise instead of a comment.
#
# The count and not the names: entries are <account>.<backup id>, and an uninstall transcript
# that enumerated them would say which customers have backups, on a terminal and in whatever
# the operator pasted it into. `-maxdepth 2 -type f` matches the layout the agent writes —
# <root>/<account>/<id> plus its sidecar — and `find` here does not follow symlinks, so a
# planted link cannot make this count or stat something outside the root.
note_backups_kept() {
  if [ ! -d "$MARAN_BACKUP_ROOT" ]; then
    echo "No local backup directory at ${MARAN_BACKUP_ROOT}; nothing to keep."
    return
  fi
  local artifacts
  artifacts="$(find "$MARAN_BACKUP_ROOT" -maxdepth 2 -type f 2>/dev/null | wc -l)"
  echo "KEEPING ${MARAN_BACKUP_ROOT} (${artifacts} file(s)). Backups are customer data and this"
  echo "        uninstaller never deletes them. Remove that directory by hand if you intend to"
  echo "        decommission this server."
}

# note_customer_data_untouched: explicit statement of what this script never touches,
# printed unconditionally so an operator never has to guess.
note_customer_data_untouched() {
  cat <<'EOF'

This uninstaller never touches:
  - Customer hosting accounts under /home/*
  - Customer sites, files, or per-account databases
  - MariaDB itself, or any database in it
  - Backups under /var/backups/maran (counted above)

Remove those yourself if you intend to decommission the server entirely.
EOF
}

main() {
  echo "Uninstalling Maran..."
  # Before anything is deleted: /var/lib/maran's owner is the only evidence that a pre-rename
  # account belongs to this product, and it does not survive remove_var_lib.
  record_legacy_service_user
  stop_and_disable_services
  remove_systemd_units
  remove_nginx_vhost
  # Before /etc/maran goes: the marker that says whether we enabled nftables lives there.
  remove_firewall
  # SFTP first, and in this order: take back the sshd edit, then unmount the jails
  # so that nothing further down can recurse through a bind mount into a home.
  remove_sftp_sshd_block
  release_sftp_jails
  # FTPS next, and before the binaries: it unmasks the distribution's vsftpd.service,
  # removes the PAM service that authorizes an FTPS login and takes Maran's own unit
  # away — while deliberately leaving the jails, the logins and the group, each of
  # which still names customer data. See remove_ftps for the argument.
  remove_ftps
  remove_binaries
  # The database question comes before /etc/maran is deleted: keeping the data while
  # silently destroying the key that decrypts it is the one unrecoverable mistake this
  # script could make on its own.
  drop_database
  remove_config_and_state
  remove_var_lib
  remove_logs
  remove_sftp_group
  remove_service_user
  note_backups_kept
  note_customer_data_untouched
  echo "Maran uninstall complete."
}

# main runs when this file is EXECUTED and not when it is SOURCED, which is what lets
# docker/polygon/assert-installer-steps.sh drive remove_firewall and the /etc/maran removal
# against real files, a real include target and a real `nft`. Nothing in this repository
# exercised the uninstaller at all before that, so its copy of the marker state machine — the
# one whose `sed '/BEGIN/,/END/d'` predecessor deleted an operator's own `table inet mine` —
# could be broken by the same mutation that fails the installer's copy and every gate here
# would still be green.
if [ "${BASH_SOURCE[0]}" = "${0}" ]; then
  main "$@"
fi
