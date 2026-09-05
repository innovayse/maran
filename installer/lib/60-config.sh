#!/usr/bin/env bash
# Step 60: generate /etc/maran/panel.env (root:panel 0640) with a freshly generated
# 256-bit encryption key and the PostgreSQL connection string. This is Maran's one
# secrets file (rules/security.md: "New config values with secrets go to panel.env, not
# appsettings"). The key is generated here, on the customer's machine, at install time —
# it never ships in the repository or the release artifacts.
#
# The same file is the panel's one-time seed channel, which is why three values that are not
# secrets at all are written here too: the host's real SSH port, the panel's own public port
# and the address this installer was run from. All three are facts only something standing on
# the machine during the install can observe or decide, all three are read by the firewall,
# and Setup__Token below has carried exactly that kind of value since the first release — a
# second mechanism for the same job would be one more thing to keep 0640.
set -euo pipefail

readonly MARAN_CONFIG_DIR="/etc/maran"
readonly MARAN_CONFIG_FILE="${MARAN_CONFIG_DIR}/panel.env"

# generate_encryption_key: 256 bits of randomness from the kernel CSPRNG, base64-encoded
# (matches the shape DataProtection/appsettings already expect — see scripts/dev's
# throwaway dev key for the same encoding). Never echoed to the log: this function's
# stdout is captured directly into a variable by the caller, not printed.
generate_encryption_key() {
  openssl rand -base64 32
}

# generate_setup_token: a one-time, unguessable token used by 90-finish.sh to build the
# first-admin setup URL. Generated here (alongside the other secrets) so it lands in the
# same 0640 file and never touches stdout/the install log.
generate_setup_token() {
  openssl rand -hex 24
}

# How deep an Include chain this parser will follow before giving up. sshd itself allows
# nesting; a bound is what stops a configuration that includes itself from spinning the
# installer forever. Eight is far past any real layout — the distributions ship one level.
readonly MARAN_SSHD_INCLUDE_MAX_DEPTH=8

# normalized_port: one port number on stdout when the argument is one, nothing and a
# non-zero status when it is not.
#
# `10#` so `Port 0022` becomes 22: one port has one spelling, and a leading zero would
# otherwise be read as octal by the next reader that does arithmetic on it.
normalized_port() {
  local candidate="$1"
  case "$candidate" in
    ''|*[!0-9]*) return 1 ;;
  esac
  [ "${#candidate}" -le 5 ] || return 1
  candidate=$((10#$candidate))
  [ "$candidate" -ge 1 ] && [ "$candidate" -le 65535 ] || return 1
  printf '%s' "$candidate"
}

# port_from_listen_address: the port inside a ListenAddress argument, when it carries one.
#
# sshd's own grammar, which is where the shapes come from: `host:port`, `IPv4:port`,
# `[host]:port`, or an address with no port at all. A bare IPv6 address is the trap — `::1`
# is all colons and no port — so an unbracketed argument with more than one colon is an
# address, never an address and a port. `rdomain` may follow, which is why only the first
# word ever reaches this function.
port_from_listen_address() {
  local argument="$1" candidate
  case "$argument" in
    '['*']:'*) candidate="${argument##*]:}" ;;
    *:*:*)     return 1 ;;
    *:*)       candidate="${argument##*:}" ;;
    *)         return 1 ;;
  esac
  normalized_port "$candidate"
}

# split_config_arguments: the arguments of one directive, one per line, honouring the double
# quotes sshd allows around a path that contains spaces.
#
# `read -a` cannot do this. It splits on whitespace, so `Include "/etc/ssh/my configs/*.conf"`
# becomes two patterns, neither of which names a file, and the include contributes no ports at
# all — the same lockout as not following Include in the first place, reached by another route.
split_config_arguments() {
  local rest="$1" token
  while : ; do
    rest="${rest#"${rest%%[! ]*}"}"
    [ -n "$rest" ] || break
    case "$rest" in
      '"'*)
        rest="${rest#\"}"
        token="${rest%%\"*}"
        if [ "$token" = "$rest" ]; then
          # An unterminated quote: take what is there and stop, the way a lenient reader must.
          rest=""
        else
          rest="${rest#*\"}"
        fi
        ;;
      *)
        token="${rest%% *}"
        rest="${rest#"$token"}"
        ;;
    esac
    if [ -n "$token" ]; then
      printf '%s\n' "$token"
    fi
  done
  return 0
}

# expand_glob: the existing files one glob pattern matches, one per line.
#
# `for x in $pattern` on its own is not enough: unquoted, it word-splits BEFORE it globs, so a
# path with a space in it — the only reason the quotes above exist — is torn into two patterns
# that match nothing. An empty IFS suppresses the splitting while pathname expansion still
# produces one word per match. (A path containing a newline would defeat the line protocol
# here; sshd config paths do not have those, and the worst case is the same as an unreadable
# include: it is skipped.)
expand_glob() {
  local pattern="$1" match
  local IFS=''
  for match in $pattern; do
    if [ -e "$match" ]; then
      printf '%s\n' "$match"
    fi
  done
  return 0
}

# ssh_ports_from_file: every port named by one sshd configuration file and everything it
# includes, one per line, unsorted and undeduplicated. Unreadable files are skipped rather
# than fatal — a config referencing a file this host does not have is sshd's problem, and
# an installer that dies over it has made a lockout out of a warning.
#
# `Include` is followed because on four of the eight supported targets that is where the
# port actually lives: Ubuntu and Debian ship `Include /etc/ssh/sshd_config.d/*.conf` as the
# first line of sshd_config and a modern override is a drop-in file, so a parser that reads
# only the main file answers 22 for a host whose sshd is on 2222 — and the firewall then
# closes the operator out of their own server on the default configuration of half the
# platforms we support. Measured, and it is the reason this function is recursive.
#
# Relative include patterns resolve against the directory of the TOP-level file, which on a
# real host is `/etc/ssh` — exactly what sshd does with them — and on a fixture is the
# fixture's own directory, which is what makes this testable without writing to /etc.
ssh_ports_from_file() {
  local file="$1" base="$2" depth="$3"
  [ "$depth" -le "$MARAN_SSHD_INCLUDE_MAX_DEPTH" ] || return 0
  [ -f "$file" ] && [ -r "$file" ] || return 0

  local line keyword rest argument port pattern candidate
  local -a patterns
  while IFS= read -r line || [ -n "$line" ]; do
    # A CRLF file is not a corner case, it is finding 1 in another disguise: sshd accepts one
    # and serves the port, while a carriage return left on the value makes `Port 2222` a port
    # this parser cannot read and the firewall never opens. Measured against real sshd, which
    # answers 2222 for the very file that used to give this function 22. Tabs go the same way,
    # so everything below sees one separator.
    line="${line//$'\r'/}"
    line="${line//$'\t'/ }"
    # sshd accepts one optional '=' in place of the space after a keyword.
    line="${line/=/ }"
    read -r keyword rest <<<"$line"
    case "${keyword,,}" in
      port)
        read -r argument _ <<<"$rest"
        port="$(normalized_port "$argument")" && printf '%s\n' "$port"
        ;;
      listenaddress)
        read -r argument _ <<<"$rest"
        port="$(port_from_listen_address "$argument")" && printf '%s\n' "$port"
        ;;
      include)
        patterns=()
        while IFS= read -r pattern; do
          patterns+=("$pattern")
        done < <(split_config_arguments "$rest")
        for pattern in "${patterns[@]}"; do
          case "$pattern" in
            /*) ;;
            *) pattern="${base}/${pattern}" ;;
          esac
          while IFS= read -r candidate; do
            ssh_ports_from_file "$candidate" "$base" "$((depth + 1))"
          done < <(expand_glob "$pattern")
        done
        ;;
    esac
  done < "$file"

  # Explicit, because the loop's status is the status of whatever its last iteration ran: a
  # file whose last directive is a portless `ListenAddress 0.0.0.0` ends on a failed test and
  # would hand this function's caller a non-zero status. Through a command substitution that
  # is invisible; on a direct call under `set -e` it ends the install without a word.
  return 0
}

# detect_ssh_ports: every port this host's sshd listens on, comma-separated, ascending, no
# spaces; `22` when the configuration names none.
#
# PLURAL, and the plural is the safety property. sshd listens on EVERY Port directive it is
# given, not the first one, and `ListenAddress 0.0.0.0:2222` sets one too. A single-value
# answer therefore had to pick, and every way of picking is a lockout for somebody: with
# `Port 2222` above `Port 22` the old code said 2222, with the lines swapped it said 22, and
# the operator connected on whichever one the answer had dropped. The firewall allows the
# union — allowing a port sshd is not listening on costs nothing, and allowing one fewer
# than it listens on costs the server.
#
# Ascending numeric order, so the value depends on what the host does and not on the order
# somebody's lines happen to be in.
#
# The match on each directive is deliberately wider than a plain `^Port`: leading
# whitespace, any casing and the `Port=2222` spelling are all accepted, because sshd accepts
# all three. Do not tighten it. The two failure directions are not symmetrical — matching
# too widely costs a wrong port read out of a configuration nobody writes, matching too
# narrowly costs remote access to the server — and a spurious match is impossible when the
# keyword AND a valid port argument are both required. A commented `#Port 2222` is not a
# directive and never matches.
#
# The path is a parameter with the real default so the detection can be exercised against a
# fixture instead of against whatever this machine happens to run.
detect_ssh_ports() {
  local config="${1:-/etc/ssh/sshd_config}" base ports
  base="$(dirname "$config")"
  ports="$(ssh_ports_from_file "$config" "$base" 0 | sort -n -u | paste -sd, -)"

  if [ -z "$ports" ]; then
    # The documented default, and the one case where a guess beats an empty value: an empty
    # list would have the firewall drop SSH altogether.
    echo "No Port or ListenAddress directive found in ${config} or its includes; assuming 22." >&2
    echo 22
    return 0
  fi

  echo "$ports"
}

# is_ipv4_address: true for a dotted quad and nothing else.
#
# Four decimal octets, each 0-255, and no leading zeros — `010.1.1.1` is a spelling
# ambiguity (octal to one reader, decimal to another) that has no place in a whitelist row,
# and the panel's own address type refuses it for the same reason.
is_ipv4_address() {
  local address="$1" octet
  case "$address" in
    *[!0-9.]*|'') return 1 ;;
  esac

  local IFS=.
  # shellcheck disable=SC2086
  set -- $address
  [ "$#" -eq 4 ] || return 1
  for octet in "$@"; do
    case "$octet" in
      0|[1-9]|[1-9][0-9]|[1-9][0-9][0-9]) ;;
      *) return 1 ;;
    esac
    [ "$octet" -le 255 ] || return 1
  done
  return 0
}

# is_ipv6_address: true for an address inet_pton would take, false for the things that
# merely contain a colon.
#
# Written out rather than delegated because there is nothing on a freshly installed server
# to delegate to — no python, no ipcalc — and "it has a colon in it" admitted
# `1.2.3.4:5` and `::::::::::`, both of which reached the panel as a whitelist row that can
# never match anything. A row that silently matches nothing is worse than no row: the
# operator believes they are exempt from the bans and they are not.
#
# The grammar enforced: hex groups of 1-4 digits separated by single colons; at most one
# `::`; a trailing dotted quad allowed (the `::ffff:1.2.3.4` form) and counting as two
# groups; exactly eight groups when uncompressed, at most seven when compressed — `::` must
# stand for at least one group of its own.
is_ipv6_address() {
  local address="$1" group groups=0 compressed=0 last=""
  case "$address" in
    *[!0-9A-Fa-f:.]*) return 1 ;;
    *:::*) return 1 ;;
    *::*::*) return 1 ;;
    *:*) ;;
    *) return 1 ;;
  esac
  case "$address" in
    *::*) compressed=1 ;;
  esac

  local IFS=:
  # shellcheck disable=SC2086
  set -- $address
  for group in "$@"; do
    last="$group"
  done

  for group in "$@"; do
    # Empty fields are what a leading, trailing or interior `::` splits into.
    [ -n "$group" ] || continue
    case "$group" in
      *.*)
        # A dotted quad is legal only as the last group, where it stands for two.
        [ "$group" = "$last" ] || return 1
        is_ipv4_address "$group" || return 1
        groups=$((groups + 2))
        ;;
      *)
        case "$group" in
          [0-9A-Fa-f] | [0-9A-Fa-f][0-9A-Fa-f] | [0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f] \
            | [0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]) ;;
          *) return 1 ;;
        esac
        groups=$((groups + 1))
        ;;
    esac
  done

  if [ "$compressed" -eq 1 ]; then
    [ "$groups" -le 7 ] || return 1
  else
    [ "$groups" -eq 8 ] || return 1
  fi
  return 0
}

# seed_whitelist_cidr_is_usable: true for a recorded seed the PANEL will actually store as a
# whitelist row, false for one it will refuse at boot.
#
# It exists because "the installer wrote a value" and "the panel accepted it" are two different
# facts, and the install transcript used to report the first while claiming the second. On a
# dual-stack host sshd reports SSH_CLIENT=::ffff:203.0.113.7; is_ipv6_address accepts that
# spelling, so the installer printed "Seeding the firewall whitelist with ::ffff:203.0.113.7/128",
# suppressed its end-of-install warning because the variable was not empty, and the panel then
# refused the mapped form into a log line nobody reads. The operator ended the install believing
# they were exempt from the automatic bans, with an empty whitelist.
#
# One definition, two callers: detect_seed_whitelist_cidr screens its own output with it, so a
# future bug in that function produces "no seed, and a warning" rather than a false claim; and
# 90-finish.sh screens what it reads back out of panel.env, so a hand-edited or otherwise
# surprising value is warned about instead of assumed good.
#
# It is a SCREEN, not a re-implementation of the panel's rule: it checks the family, the mapped
# spelling and the prefix range, and it does not check host bits beyond the prefix (`203.0.113.7/24`
# passes here and the panel refuses it). Detection cannot produce such a value — it emits only /32
# and /128 — so the remaining gap is a hand-edited file, where the panel's own boot warning is the
# backstop. Duplicating the whole rule in shell is what produced this defect in the first place.
seed_whitelist_cidr_is_usable() {
  local cidr="$1" address prefix
  case "$cidr" in
    */*) ;;
    *) return 1 ;;
  esac
  address="${cidr%/*}"
  prefix="${cidr##*/}"

  # The IPv4-mapped spelling is refused rather than accepted-and-hoped-about: the panel translates
  # it (see CidrRangeNormalizer), but this function answers for what the INSTALLER should write, and
  # writing the plain form keeps the transcript, panel.env and the stored row saying the same thing.
  case "$address" in
    ::[Ff][Ff][Ff][Ff]:*) return 1 ;;
  esac

  if is_ipv4_address "$address"; then
    case "$prefix" in
      [0-9] | [12][0-9] | 3[0-2]) return 0 ;;
      *) return 1 ;;
    esac
  fi

  if is_ipv6_address "$address"; then
    case "$prefix" in
      [0-9] | [1-9][0-9] | 1[01][0-9] | 12[0-8]) return 0 ;;
      *) return 1 ;;
    esac
  fi

  return 1
}

# session_id_of: the session id of one process, or a non-zero status when it cannot be read.
#
# Fields are counted from after the LAST ')' rather than from the start of the line: the
# comm field is parenthesised and may itself contain spaces and parentheses, so a process
# named "my ) program" breaks every parse that counts from the left. After it come state,
# ppid, pgrp and then the session id.
session_id_of() {
  local pid="$1" stat rest sid
  stat="$(cat "/proc/${pid}/stat" 2>/dev/null || true)"
  [ -n "$stat" ] || return 1
  rest="${stat##*) }"
  read -r _ _ _ sid _ <<<"$rest"
  case "$sid" in
    ''|*[!0-9]*) return 1 ;;
  esac
  printf '%s' "$sid"
}

# The most ancestors ssh_client_value will look at before giving up. The real chain from
# this script to the operator's login shell is two or three processes (install.sh, sudo,
# the shell); ten is room to spare, and a bound is what stops a broken or circular parent
# chain from spinning forever.
readonly MARAN_SSH_CLIENT_MAX_HOPS=10

# ssh_client_value: this session's raw SSH_CLIENT string, printed on stdout; nothing when
# there is none to be had.
#
# Two sources, because one is not enough, and the second is the one that matters for the
# installer's own documented usage. `sudo bash install.sh` runs under sudo's env_reset,
# which hands the command a new, minimal environment; SSH_CLIENT is on no stock env_keep
# list, so it is gone by the time this step runs — even though the operator did arrive over
# SSH. A whitelist seed that is empty exactly on the documented install path is not a seed,
# and the operator finds out when the panel's automatic bans lock them out of their server.
#
# So when the environment has nothing, the process tree is walked upwards: sudo's parent is
# the operator's login shell, whose environment still carries SSH_CLIENT.
#
# The walk stops the moment it leaves THIS login session, and that bound is not tidiness —
# it is what keeps a stale address out of the whitelist. A tmux or screen server is an
# ancestor of every pane it owns and carries the SSH_CLIENT of whichever session started it,
# which may be a different operator on a different address from days ago; such a server runs
# in its own session, so comparing session ids excludes it. Inside tmux the walk therefore
# finds nothing and the install ends with the empty-whitelist warning, which is the honest
# answer: nobody can tell from in there which address this operator arrived on. If our own
# session id cannot be read the walk does not start, for the same reason.
#
# Where the value came from changes nothing about how far it is trusted: an environment is
# an environment, and the one caller validates the address before a byte of it reaches
# panel.env.
ssh_client_value() {
  if [ -n "${SSH_CLIENT:-}" ]; then
    echo "$SSH_CLIENT"
    return 0
  fi

  local pid="${PPID:-1}" hops=0 value="" our_session ancestor_session
  our_session="$(session_id_of "$$")" || our_session=""
  [ -n "$our_session" ] || return 0

  while [ "$pid" -gt 1 ] && [ "$hops" -lt "$MARAN_SSH_CLIENT_MAX_HOPS" ]; do
    ancestor_session="$(session_id_of "$pid")" || ancestor_session=""
    [ "$ancestor_session" = "$our_session" ] || return 0

    # Read as the NUL-delimited records it actually is, rather than translating NUL to
    # newline and scanning lines. Measured, and it matters: with the translation, a value
    # containing a newline is cut at the newline instead of being refused — so the same
    # bytes that the environment path rejects were quietly accepted through this one — and
    # an entry crafted as FOO=x<newline>SSH_CLIENT=… would read back as a genuine
    # SSH_CLIENT. Whatever is in the record now reaches the caller's validation intact.
    #
    # The whole read is one group with stderr silenced and `|| true`: an unreadable or
    # vanished /proc entry is an ordinary event in a walk up a live process tree, and
    # neither the shell's own "Permission denied" nor `set -e` may end an install over one.
    value="$({
      while IFS= read -r -d '' entry; do
        case "$entry" in
          SSH_CLIENT=*)
            printf '%s' "${entry#SSH_CLIENT=}"
            break
            ;;
        esac
      done < "/proc/${pid}/environ"
    } 2>/dev/null || true)"
    if [ -n "$value" ]; then
      echo "Recovered this session's client address from an ancestor process in the same login session." >&2
      echo "$value"
      return 0
    fi

    pid="$(awk '/^PPid:/ { print $2; exit }' "/proc/${pid}/status" 2>/dev/null || true)"
    case "$pid" in
      ''|*[!0-9]*) return 0 ;;
    esac
    hops=$((hops + 1))
  done
}

# detect_seed_whitelist_cidr: the address of the operator running this installer, as a
# single-host CIDR, printed on stdout — or nothing at all when there is no such address.
#
# It becomes the first row of the firewall's whitelist, so that the anti-brute-force
# feature cannot ban the person who installed the panel. An empty whitelist on day one is
# a server whose administrator can lock themselves out by mistyping their own password.
#
# Taken from SSH_CLIENT (see ssh_client_value for where that is found), whose first field
# sshd sets to the peer's address. An install with no client address at all — at the console,
# or through anything that is not an SSH session — writes nothing and the whitelist starts
# empty; that is stated in the generated file and warned about at the end of the install,
# because a silently absent seed is indistinguishable from a broken one.
#
# The address is validated to be an address, not merely to look like one. Two reasons, and
# the second is the one that bites: rules/security.md refuses newlines, carriage returns and
# control characters in anything written to a line-oriented configuration file, and a
# malformed row like `999.999.999.999/32` passes that bar while matching no packet that will
# ever arrive — so the operator reads a whitelist entry with their address in it and believes
# they are exempt from bans that will still catch them. A refusal says so; a bad row does not.
#
# The IPv4-mapped spelling is written out as plain IPv4. A host whose sshd listens on `::` with
# IPv4-mapped sockets (`ListenAddress ::`, `net.ipv6.bindv6only=0`) reports
# SSH_CLIENT=::ffff:203.0.113.7 for an ordinary IPv4 client, and `::ffff:203.0.113.7/128` and
# `203.0.113.7/32` name the same machine. Recording the second means the transcript, panel.env and
# the whitelist row an operator later reads in the panel all say the same thing — and it is the
# spelling the panel matches against, since every address it compares is normalised to plain IPv4.
detect_seed_whitelist_cidr() {
  local client address mapped cidr
  client="$(ssh_client_value)"
  [ -n "$client" ] || return 0

  # "<client address> <client port> <server port>" — the address is the first field.
  address="${client%% *}"
  if [ "${#address}" -gt 45 ]; then
    echo "Ignoring the client address: it is longer than any address can be." >&2
    return 0
  fi

  # Before the family tests, because the mapped form passes the IPv6 one and must not be
  # recorded as IPv6: only the prefix that follows differs, and /128 of an IPv4-mapped address
  # is a row the panel would have to translate at boot instead of simply storing.
  case "$address" in
    ::[Ff][Ff][Ff][Ff]:*)
      mapped="${address#::[Ff][Ff][Ff][Ff]:}"
      if is_ipv4_address "$mapped"; then
        address="$mapped"
      fi
      ;;
  esac

  cidr=""
  if is_ipv4_address "$address"; then
    cidr="${address}/32"
  elif is_ipv6_address "$address"; then
    cidr="${address}/128"
  else
    # Deliberately without the value: it is refused precisely because something about it was
    # not an address, and echoing it would put those bytes in the install log instead.
    echo "Ignoring the client address: it is not a valid IPv4 or IPv6 address." >&2
    return 0
  fi

  # The last gate, against the same rule 90-finish.sh warns by. Everything above is meant to
  # produce a value the panel stores; this is what makes "meant to" checkable, so a mistake here
  # ends as an empty whitelist the operator is warned about rather than as a seed they are told
  # they have and do not.
  if ! seed_whitelist_cidr_is_usable "$cidr"; then
    echo "Ignoring the client address: ${cidr} is not a range the panel can store." >&2
    return 0
  fi

  echo "$cidr"
}

# write_config: renders panel.env to a temp file first, then atomically renames it into
# place, so a crash mid-write can never leave a half-written secrets file readable by
# the wrong mode/owner. Preserves an already-generated encryption key on re-run (see
# step_config) instead of rotating it, since rotating silently would break existing
# encrypted data in PostgreSQL.
write_config() {
  local encryption_key="$1" setup_token="$2" signing_key="$3" ssh_ports="$4" seed_cidr="$5" tmp
  local proxy_uid="$6"
  # Demanded here, before the temp file exists, and not left to the interpolation forty lines
  # below: under `set -u` an unset value aborts mid-write, and what it aborts is a 0600 file
  # holding the encryption key and the token signing key, left in /etc/maran with nothing to
  # clean it up. `:?` rather than a default, for the reason 10-preflight.sh gives.
  : "${MARAN_PANEL_PORT:?must be set by install.sh before this step is sourced}"
  : "${MARAN_API_SOCKET_PATH:?must be set by install.sh before this step is sourced}"
  # Staged inside /etc/maran (root:panel 0750), not /tmp: a rename is only atomic within
  # one filesystem — from /tmp it degrades to a copy, which is neither atomic nor a place
  # to park a file holding the encryption key even briefly.
  tmp="$(mktemp "${MARAN_CONFIG_DIR}/.panel.env.XXXXXX")"
  {
    echo "# Managed by the Maran installer. Do not edit by hand; re-running the"
    echo "# installer regenerates this file except for values marked 'preserved on re-run'."
    echo "#"
    echo "# Names use the .NET convention where '__' is configuration nesting, so Database__Host"
    echo "# sets the 'Database:Host' setting. They must match what the panel actually reads —"
    echo "# see backend/src/Maran.Host/Configuration and installer/panel.env.example."
    echo ""
    echo "# Database over the local unix socket: Host is the socket DIRECTORY, so no port and no"
    echo "# password apply — PostgreSQL authenticates by operating-system user (peer auth)."
    echo "Database__Host=/var/run/postgresql"
    echo "Database__Database=maran"
    echo "Database__Username=panel"
    echo ""
    echo "# Preserved on re-run: rotating this key without re-encrypting makes stored secrets unreadable."
    echo "Security__EncryptionKey=${encryption_key}"
    echo ""
    echo "# Preserved on re-run: access tokens are signed with this key, so rotating it signs"
    echo "# everyone out of the panel at the moment of an upgrade."
    echo "Jwt__SigningKey=${signing_key}"
    echo ""
    echo "# One-time token authorizing first-administrator creation in the browser."
    echo "Setup__Token=${setup_token}"
    echo ""
    echo "# The api listens on a UNIX DOMAIN SOCKET and on no TCP port at all. nginx terminates TLS on"
    echo "# ${MARAN_PANEL_PORT} and reaches it through this socket (see installer/nginx/maran.conf: the"
    echo "# path here and the upstream there must match)."
    echo "#"
    echo "# This is the panel's trust boundary and the reason it is a socket rather than a port. A"
    echo "# loopback port is reachable by every uid on the machine, and a process that reaches it"
    echo "# arrives with source address 127.0.0.1 — which is what the panel trusts as its proxy — so"
    echo "# any customer with a cron entry or a PHP site could choose the address the panel records"
    echo "# in the audit journal and rate-limits logins on. A socket is admitted by the kernel on"
    echo "# filesystem permissions and peer credentials, neither of which the caller can pick."
    echo "ASPNETCORE_URLS=http://unix:${MARAN_API_SOCKET_PATH}"
    echo ""
    echo "# The uid of the web server, and the ONLY caller the panel accepts on that socket. Resolved"
    echo "# at install time from the family's nginx user, because a uid is assigned by the machine and"
    echo "# cannot be known before it exists."
    echo "#"
    echo "# It is not a second lock so much as the one the panel itself can check: a customer's"
    echo "# process is already stopped by the socket directory's permissions before it can connect."
    echo "# Removing this line does not open the door — the panel refuses to start when it is bound"
    echo "# to a socket and this is absent, because absent configuration must never read as"
    echo "# \"accept anybody\"."
    echo "ReverseProxy__PeerUid=${proxy_uid}"
    echo ""
    echo "# Host facts the firewall has to be told, because nothing inside the panel can see them."
    echo "# Detected at install time; re-detected on every re-run of the installer."
    echo "#"
    echo "# Every port this host's own sshd listens on, ascending: all its Port directives and any"
    echo "# ListenAddress that carries a port, from sshd_config and everything it includes; 22 when"
    echo "# it names none. The firewall keeps a hard allow for each, so that no rule change can cut"
    echo "# off the way back into the server. The list is the union on purpose — sshd listens on"
    echo "# every one of them, and allowing all but one costs whoever uses that one."
    echo "Firewall__SshPorts=${ssh_ports}"
    echo ""
    echo "# The public port of the panel's own nginx vhost, from MARAN_PANEL_PORT in install.sh —"
    echo "# the one place that number is decided. The firewall allows it unconditionally too."
    echo "#"
    echo "# The api has no port of its own to confuse this with any more — it listens on the unix"
    echo "# socket named in ASPNETCORE_URLS above. Opening the api's own port instead of nginx's used"
    echo "# to be the trap here: the panel would be reachable right after the install and cut off the"
    echo "# moment any rule changed, with nobody able to log in and undo it."
    echo "Firewall__PanelPort=${MARAN_PANEL_PORT}"
    if [ -n "$seed_cidr" ]; then
      echo ""
      echo "# The address this installer was run from, seeded as the first firewall whitelist row so"
      echo "# that the anti-brute-force feature cannot ban the operator who installed the panel."
      echo "#"
      echo "# Read once, on the first start that finds an empty whitelist, and never again: the panel"
      echo "# records that it has read this value in a table of its own, so deleting the seeded row"
      echo "# does not bring the exemption back at the next restart. Editing this line afterwards"
      echo "# changes nothing — the whitelist is panel data from then on."
      echo "Firewall__SeedWhitelistCidr=${seed_cidr}"
    else
      echo ""
      echo "# Firewall__SeedWhitelistCidr is absent on purpose: this install saw no client address,"
      echo "# so there is nobody to seed the firewall whitelist with and it starts empty. Two ways"
      echo "# to get here — a console or otherwise local install, which genuinely has no client"
      echo "# address; or sudo, whose env_reset default drops SSH_CLIENT on the way to root even"
      echo "# though you did arrive over SSH. Either way, add your own address to the whitelist in"
      echo "# the panel before turning automatic bans on: nothing else stops you banning yourself."
    fi
  } > "$tmp"
  chown root:panel "$tmp"
  chmod 0640 "$tmp"
  mv -f "$tmp" "$MARAN_CONFIG_FILE"
}

# write_agent_env: records the uid the agent must accept. The agent's peer-credential
# guard permits exactly one uid, and the systemd unit reads this file to pass it — a
# missing file leaves the agent defaulting to its own uid, root, which denies the API
# every request. Not secret, so it is world-readable, unlike panel.env.
write_agent_env() {
  local uid tmp
  uid="$(id -u panel)"
  tmp="$(mktemp "${MARAN_CONFIG_DIR}/.agent.env.XXXXXX")"
  {
    echo "# Generated by the Maran installer. The uid maran-agent accepts connections from."
    echo "# Read by installer/systemd/maran-agent.service; not a secret."
    echo "MARAN_AGENT_ALLOW_UID=${uid}"
  } > "$tmp"
  chown root:root "$tmp"
  chmod 0644 "$tmp"
  mv -f "$tmp" "${MARAN_CONFIG_DIR}/agent.env"
}

# existing_value: reads one KEY=value out of an already-existing panel.env, used to
# preserve the encryption key (and only the encryption key) across re-runs.
#
# The value is taken as the raw remainder of the line after the FIRST '=' — never by
# splitting the line into '=' fields and reassembling it. A base64 key contains '='
# padding, so field-splitting produced a mangled key (extra spaces, padding lost) and
# the preserved key silently stopped decrypting the data it was preserved for.
existing_value() {
  local key="$1"
  [ -f "$MARAN_CONFIG_FILE" ] || return 0
  awk -v k="$key" 'index($0, k "=") == 1 { print substr($0, length(k) + 2); exit }' "$MARAN_CONFIG_FILE"
}

step_config() {
  echo "Generating ${MARAN_CONFIG_FILE}..."
  install -d -o root -g panel -m 0750 "$MARAN_CONFIG_DIR"

  local key token signing_key ssh_ports seed_cidr is_rerun=0
  key="$(existing_value Security__EncryptionKey)"
  if [ -z "$key" ]; then
    key="$(generate_encryption_key)"
    echo "Generated a new encryption key."
  else
    # A preserved key is the one reliable sign that this host already ran an install: it is
    # generated once and never rotated. What it changes below is what the whitelist seed
    # MEANS — a panel that already started has a whitelist and will not read the seed again.
    is_rerun=1
    echo "Preserving existing encryption key from a previous install run."
  fi

  signing_key="$(existing_value Jwt__SigningKey)"
  if [ -z "$signing_key" ]; then
    signing_key="$(generate_encryption_key)"
    echo "Generated a new token signing key."
  else
    echo "Preserving existing token signing key from a previous install run."
  fi

  # The setup token IS rotated on every re-run before first admin creation completes,
  # so an interrupted install never leaves a stale, possibly-leaked token valid.
  token="$(generate_setup_token)"

  # Host facts, not secrets: detected fresh on every run, because both can change under a
  # panel that was installed months ago (an operator moves sshd; an operator installs from
  # a different address) and a stale value here is a lockout waiting for the first firewall
  # change. Both are reported — an undetected SSH port is worth seeing in the log.
  ssh_ports="$(detect_ssh_ports)"
  echo "SSH ports the firewall will keep open: ${ssh_ports}."
  seed_cidr="$(detect_seed_whitelist_cidr)"
  if [ -z "$seed_cidr" ]; then
    echo "No client address for this session; the firewall whitelist starts empty."
  elif [ "$is_rerun" -eq 1 ]; then
    echo "Recorded this session's client address as the whitelist seed: ${seed_cidr}."
    echo "  (A panel that has already started keeps the whitelist it has; the seed is read once.)"
  else
    echo "Seeding the firewall whitelist with this session's client address: ${seed_cidr}."
  fi

  # The web server's uid, resolved from the name install.sh decided for this family. `id -u`
  # fails loudly when the user does not exist, which is the right outcome: nginx is installed by
  # 20-dependencies.sh long before this step, so a missing user means the install is already
  # broken, and writing an empty uid would produce a panel that refuses every request.
  local proxy_uid
  : "${MARAN_WEB_SERVER_USER:?must be set by install.sh before this step is sourced}"
  proxy_uid="$(id -u "$MARAN_WEB_SERVER_USER")"
  echo "The panel will accept connections on its socket from ${MARAN_WEB_SERVER_USER} (uid ${proxy_uid}) only."

  write_config "$key" "$token" "$signing_key" "$ssh_ports" "$seed_cidr" "$proxy_uid"
  write_agent_env
  echo "Config written with mode 0640, owner root:panel. Secret values were not logged."
}
