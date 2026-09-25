#!/bin/sh
# A stand-in for systemd's client, installed at /usr/bin/systemctl in the polygon
# images ONLY. Production never uses Docker (spec §2) and never sees this file.
#
# Why it exists: a container has no init system, so the `systemctl reload nginx`
# that ops::sites::write_vhost runs — through DistroAdapter::service_manager(),
# which is /usr/bin/systemctl on both families — has nothing to talk to. Without
# a stand-in every config write in the polygon would fail at the reload step and
# roll back, and the thing the polygon exists to exercise (a real `nginx -t`
# against a real configuration tree) would never be reached.
#
# What it does, and deliberately no more:
#
#   reload <service>        RECORDS the request under /run/polygon-units, then
#                           becomes `nginx -s reload` when an nginx master is
#                           actually running, and succeeds silently when none is.
#   enable --now <x>.mount  performs the bind mount the unit describes, AFTER
#                           checking the unit the way systemd checks it.
#   disable --now <x>.mount unmounts it again, which the account-deletion
#                           cascade needs before `userdel` removes the home.
#   start|stop|restart <u>  records <u>'s state under /run/polygon-units and
#                           starts or stops NOTHING. See "Unit state" below.
#   is-active <u>           prints that recorded state and exits 0 or 3.
#   is-enabled <u>          prints whether <u> is enabled and exits 0 or 1.
#   list-unit-files <u>     answers whether this host HAS <u> at all — the three
#                           answers systemd has, including the one where the query
#                           itself fails. See INSTALLED_SUFFIX below.
#   show <u> --property=…   prints the four properties ops::monitor asks for,
#                           derived from the same recorded state.
#   anything else           succeeds silently. It never starts a service and
#                           never enables one at boot; the polygon suites assert
#                           what the agent WRITES and what the real tools make of
#                           it, not what an init system does with it afterwards.
#
# Unit state, and what it is honestly worth
# -----------------------------------------
# `ops::monitor` asks `systemctl show <unit>` for LoadState, ActiveState,
# SubState and TriggeredBy, and turns those four words into Running, Stopped or
# Unknown. Until this file grew the arms below, EVERY subcommand it did not
# recognise fell through to `*) exit 0` printing nothing at all — so `show`
# answered with an empty document, every unit classified as Unknown, and there
# was no invocation of this stand-in that could produce a Stopped. A monitor
# proposition written against that would have been asserting against a tool
# incapable of saying no.
#
# So there is state: one file per unit under /run/polygon-units, holding the
# unit's ActiveState word. `start`/`restart` write `active`, `stop` writes
# `inactive`, and a unit with no file at all reads `active` — the state a real
# host's units are in after the installer has run, which a container's missing
# init would otherwise contradict for every one of them.
#
# Enablement is a SECOND file, `<unit>.enabled`, written by `enable`/`disable`
# and read by `is-enabled`, defaulting the same way. It is separate because the
# two are separate on a real host: a unit can be enabled and stopped, or running
# and not enabled, and the installer's cron gate asks both questions precisely
# because passing one of them is not the state the panel needs.
#
# A reload is a THIRD file, `<unit>.reloaded`, and it is the newest of them. A
# reload changes neither of the other two — the unit stays active, the unit stays
# enabled — and it is nonetheless the single event by which a configuration file
# reaches a RUNNING server. Until this arm recorded anything, a step that reloaded
# nginx in the middle of its own validation and a step that never touched the
# service were the same picture from outside, and the installer assertion whose
# conclusion says "the service never touched" was reading the enablement file
# alone. Two mutants of installer/lib/80-nginx.sh — a bare `systemctl reload nginx`
# straight after the rename, and the step's own reload line moved inside the
# validation window — both passed it green.
#
# **What `is-enabled` here CANNOT prove, and it will outlive this plan: that a
# unit comes back after a reboot.** A container has no reboot and no init to
# ask, so "enabled" means "this stand-in was told to enable it" — never
# "systemd would start it at boot". An assertion that reads green off this arm
# has bought the gate being CALLED and the gate refusing when the answer is no;
# it has not bought the host's boot behaviour. Only a real host settles that.
#
# What a test using this CAN prove: that the agent asks the service manager the
# right question, parses the four properties, and reports Running for a unit the
# manager calls active and Stopped for one it calls inactive — including the
# socket-activation path, since `.triggeredby` and a socket's own state file are
# expressible here. What it CANNOT prove: that systemd's real vocabulary is
# these words. That is a claim about systemd, and the fixtures ops::monitor's
# unit tests parse were captured from real hosts for exactly that reason.
#
# Escape hatches for states this file's verbs cannot reach:
# `<unit>.triggeredby` sets TriggeredBy (write `ssh.socket` to model the Debian
# family's socket-activated sshd), and `<unit>.load` sets LoadState — write
# `not-found` for a unit this host does not have. `<unit>.installed` makes
# `list-unit-files` report the unit as present, `<unit>.query-broken` makes that
# query FAIL to answer, and `<unit>.refuse-disable` makes `disable` refuse. The
# last three exist because the installer's firewalld handling has three outcomes
# and the polygon could reach exactly none of them.
#
# **Unit names here are literal strings, not systemd's aliases.** A real host
# treats `firewalld` and `firewalld.service` as one unit; this file keeps one
# state file per spelling, because normalising them would rename the files the
# agent's own monitor suite writes. A fixture that drives a verb using the short
# name and a verb using the suffixed one therefore has to write both, and the
# assertion that does so says why at the point it does it.
#
# `<unit>.load` is the weaker of the two and it is worth saying why: it sets
# only the WORD, and nothing else about the unit follows it. A real unit that
# systemd cannot find has no ActiveState worth reading either, while here the
# ActiveState file (or its `active` default) answers as usual beside a
# `not-found` LoadState — a combination no systemd produces. It is enough for
# what ops::monitor does with the word, which is to stop and report "not
# installed on this host" before it looks at anything else, and it is not a
# model of an absent unit.
#
# The mount arm is not "faking systemd to make a test pass" — it is here because
# two things about ops::sftp cannot be observed anywhere else:
#
#   1. A `.mount` unit's FILE NAME must be systemd's escaping of its own
#      `Where=`, or systemd refuses to load it. `AccountJail::unit_name`
#      implements that escaping, and a mistake in it shows up on a real host as
#      an SFTP login that lands in an empty directory — never in a build. The
#      check below is made with `systemd-escape`, systemd's OWN tool, so the
#      expectation does not come from the code under test.
#   2. Without the bind mount actually happening, the account's home is not
#      inside the jail, and the SFTP suite could not tell a working jail from an
#      empty one.
#
# It needs privileges a default container does not have. Run the SFTP suite with
# `docker run --privileged`; without it the mount fails, `create_sftp_user`
# returns JailFailed, and the suite fails loudly rather than passing on a jail
# that was never filled. That is the intended behaviour: see docker/README.md.
#
# The consequence is stated rather than hidden: `reload` cannot fail here, so
# SafeWriteError::ReloadFailed and its rollback are covered by the unit tests in
# ops::safe_write and not by the polygon.
set -eu

# Where the agent writes unit files, matching DistroAdapter::systemd_unit_directory
# on both families.
UNIT_DIRECTORY=/etc/systemd/system

# Where this stand-in keeps one file per unit it has been asked to start or stop.
# Under /run because nothing here may survive the container, and because a test
# that wants a clean slate deletes the directory.
STATE_DIRECTORY=/run/polygon-units

# The ActiveState a unit with no state file reads as. See "Unit state" above:
# `active` is the state a real host's installed units are in, and a container's
# missing init must not turn every one of them into a reported outage.
DEFAULT_ACTIVE_STATE=active

# systemd's exit status for `is-active` on a unit that is not active.
INACTIVE_STATUS=3

# The suffix of the file recording whether a unit is enabled at boot.
ENABLED_SUFFIX=.enabled

# What a unit with no enablement file reads as. `enabled`, for the same reason
# the ActiveState default is `active`: it is the state a real host's units are
# in once the installer has run them, and a container's missing init must not
# turn every one of them into a reported failure.
DEFAULT_ENABLEMENT=enabled

# systemd's exit status for `is-enabled` on a unit that is not enabled.
DISABLED_STATUS=1

# The suffix of the file recording that a unit was asked to RELOAD. Written by the
# `reload` arm and by nothing else, and never read back by this file: it exists to
# be observed from outside, by a test asking whether the service was touched.
RELOADED_SUFFIX=.reloaded

# The suffix of the file saying that this host HAS the unit at all, which is what
# `list-unit-files` answers. Absent by default, and that is the honest default here:
# neither polygon image installs firewalld, so a stand-in that reported it present
# would be inventing a host.
INSTALLED_SUFFIX=.installed

# The suffix of the file that makes `list-unit-files` fail to ANSWER — it writes the
# file's first line to stderr and exits 1. It exists because "the query broke" and
# "the unit is not here" are two different answers that an installer must not confuse,
# and until this arm existed there was no way to produce the first one: the catch-all
# `*) exit 0` printed nothing at all, which is indistinguishable from "not here".
QUERY_BROKEN_SUFFIX=.query-broken

# The suffix of the file that makes `disable` REFUSE — its first line to stderr, exit 1,
# and the unit's recorded state left exactly as it was. A disable that is refused is the
# state in which an installer's `|| true` finishes an install with another firewall still
# in charge of the ruleset, and it was reachable nowhere in this repository.
REFUSE_DISABLE_SUFFIX=.refuse-disable

# systemd's exit status for `list-unit-files <pattern>` that matched no unit file.
# Measured against systemd 255: the header line and `0 unit files listed.` on stdout,
# NOTHING on stderr, exit 1. So a non-zero status there is an answer, not a failure —
# which is why the arm below reports a broken query on stderr and never by status alone.
NO_UNIT_FILES_STATUS=1

# fail: say what systemd would have said, on stderr, and refuse.
fail() {
    echo "systemctl-stand-in: $1" >&2
    exit 1
}

# value_of: the value of `Key=` in a unit file, first occurrence only.
value_of() {
    sed -n "s/^$1=//p" "$2" | head -1
}

# mount_unit: the `enable --now <name>.mount` arm.
#
# Checked before mounted, in the order systemd checks: a unit that is not there
# is an error, and a unit whose name is not the escaping of its own Where= is
# refused at load time. Both are answered with a non-zero status, which the
# agent's config-write protocol turns into a rolled-back write and a typed
# JailFailed — the same outcome a real host would produce.
mount_unit() {
    unit="$1"
    path="${UNIT_DIRECTORY}/${unit}"

    [ -f "$path" ] || fail "unit ${unit} not found in ${UNIT_DIRECTORY}"

    what="$(value_of What "$path")"
    where="$(value_of Where "$path")"
    [ -n "$what" ] || fail "unit ${unit} has no What="
    [ -n "$where" ] || fail "unit ${unit} has no Where="

    # systemd's own escaping, from systemd's own tool. Deriving the expectation
    # any other way would let a bug in the agent's escaping agree with a bug in
    # the check.
    expected="$(systemd-escape --path --suffix=mount "$where")"
    [ "$expected" = "$unit" ] || \
        fail "unit ${unit} does not match the escaping of its own Where=${where} (${expected}); systemd would refuse to load it"

    [ -d "$what" ] || fail "What=${what} is not a directory"
    [ -d "$where" ] || fail "Where=${where} is not a directory"

    # Already mounted is success: the unit is enabled on every SFTP user this
    # account gets, and the second one must not fail on the first one's work.
    if grep -qE "[[:space:]]${where}[[:space:]]" /proc/self/mountinfo; then
        exit 0
    fi

    exec mount --bind "$what" "$where"
}

# unmount_unit: the `disable --now <name>.mount` arm.
#
# The mirror of mount_unit, and here for the same reason: the account-deletion
# cascade takes an account's jail down before `userdel` removes the home that is
# bind-mounted into it, and the unmount is NOT best-effort. Without this arm the
# stand-in would fall through to "succeed silently", the mount would survive, and
# the cascade's next step — a plain `rmdir` of the mount point, which is what
# stops a still-mounted jail from being deleted recursively — would fail with
# EBUSY. The polygon suite would then go red for a reason that has nothing to do
# with the agent.
#
# systemd refuses to disable a unit it has no file for, and so does this. Not
# mounted is success: the cascade is idempotent, and a second deletion must
# converge rather than fail on its own previous work.
unmount_unit() {
    unit="$1"
    path="${UNIT_DIRECTORY}/${unit}"

    [ -f "$path" ] || fail "unit ${unit} not found in ${UNIT_DIRECTORY}"

    where="$(value_of Where "$path")"
    [ -n "$where" ] || fail "unit ${unit} has no Where="

    grep -qE "[[:space:]]${where}[[:space:]]" /proc/self/mountinfo || exit 0

    exec umount "$where"
}

# require_unit_name: refuse a unit name that would not be one.
#
# The name is used as a file name, so one carrying a `/` would escape the state
# directory. Nothing in the agent can produce one — unit names come from the
# closed set on the DistroAdapter and from AccountJail's own escaping — but this
# file is a root-run program in a container that also hosts the privilege-drop
# suite, and a refusal costs one line.
#
# Called from each arm DIRECTLY and never from inside a `$( … )`: `fail` exits,
# and an exit inside a command substitution ends only the subshell, so the
# caller would carry on with the refusal printed and nothing refused.
require_unit_name() {
    case "$1" in
        */* | '' | .. | .) fail "unit name '$1' is not a unit name" ;;
    esac
}

# unit_from: the unit name among the arguments that follow a verb.
#
# systemctl takes its options in any position, and `ops::monitor` puts all four
# of its `--property=` flags BEFORE the unit and separates them from it with a
# bare `--` — so that a unit name beginning with a dash could never be read as
# an option. Taking `$2` as the unit would read `--property=LoadState` as the
# unit name, answer about a unit nothing asked for, and leave every status in
# its default state: a stand-in that agrees with whatever it is handed, which is
# the one thing this file exists not to be.
#
# So the rules are systemctl's own: everything after a bare `--` is a name, and
# before it the first argument that does not begin with a dash is the name.
unit_from() {
    after_separator=""
    for argument in "$@"; do
        if [ -n "$after_separator" ]; then
            printf '%s' "$argument"
            return 0
        fi
        case "$argument" in
            --) after_separator=yes ;;
            -*) ;;
            *)
                printf '%s' "$argument"
                return 0
                ;;
        esac
    done

    printf ''
}

# state_file: the path holding one aspect of a unit's state. Composition only —
# the name is checked by `require_unit_name` before any arm gets here.
state_file() {
    printf '%s/%s%s' "$STATE_DIRECTORY" "$1" "${2:-}"
}

# record_state: write <unit>'s ActiveState word.
record_state() {
    mkdir -p "$STATE_DIRECTORY"
    printf '%s\n' "$2" > "$(state_file "$1")"
}

# active_state_of: <unit>'s recorded ActiveState, or the default.
active_state_of() {
    path="$(state_file "$1")"
    if [ -f "$path" ]; then
        head -1 "$path"
    else
        printf '%s' "$DEFAULT_ACTIVE_STATE"
    fi
}

# record_enablement: write whether <unit> is enabled at boot.
record_enablement() {
    mkdir -p "$STATE_DIRECTORY"
    printf '%s\n' "$2" > "$(state_file "$1" "$ENABLED_SUFFIX")"
}

# record_reload: note that <unit> was asked to reload.
#
# One file whose presence is the whole answer, rather than a counter: the question
# a caller has is "was this service touched at all", and a count would invite a
# check that tolerates one reload.
record_reload() {
    mkdir -p "$STATE_DIRECTORY"
    printf '%s\n' reloaded > "$(state_file "$1" "$RELOADED_SUFFIX")"
}

# enablement_of: <unit>'s recorded enablement, or the default.
enablement_of() {
    path="$(state_file "$1" "$ENABLED_SUFFIX")"
    if [ -f "$path" ]; then
        head -1 "$path"
    else
        printf '%s' "$DEFAULT_ENABLEMENT"
    fi
}

# property_of: the contents of one of a unit's override files, or a fallback.
property_of() {
    path="$(state_file "$1" "$2")"
    if [ -f "$path" ]; then
        head -1 "$path"
    else
        printf '%s' "$3"
    fi
}

# sub_state_for: systemd's unit-type-specific word behind an ActiveState.
#
# Mapped rather than recorded, because nothing in ops::monitor DECIDES anything
# from SubState — it is carried into the status's `detail` for an operator to
# read. A word that did not match the state would make that detail lie.
sub_state_for() {
    case "$1" in
        active) printf 'running' ;;
        inactive) printf 'dead' ;;
        failed) printf 'failed' ;;
        *) printf '%s' "$1" ;;
    esac
}

# show_unit: the `show <unit> --property=…` arm.
#
# Prints all four properties ops::monitor asks for, whichever subset was
# requested: systemctl prints only what was asked, and a caller that asked for
# fewer simply ignores the rest. Always exits 0, including for a unit reported
# as not-found — which is precisely what lets that area tell "not installed on
# this host" from "the service manager could not be reached".
show_unit() {
    unit="$1"
    state="$(active_state_of "$unit")"

    printf 'LoadState=%s\n' "$(property_of "$unit" .load loaded)"
    printf 'ActiveState=%s\n' "$state"
    printf 'SubState=%s\n' "$(sub_state_for "$state")"
    printf 'TriggeredBy=%s\n' "$(property_of "$unit" .triggeredby '')"
    exit 0
}

# The verb, taken off the front so every arm below sees only its own arguments.
verb="${1:-}"
if [ "$#" -gt 0 ]; then
    shift
fi

case "$verb" in
    start | restart)
        # Records the state and starts nothing. The polygon's real daemons are
        # started by the suites' own fixtures, as real processes.
        unit="$(unit_from "$@")"
        require_unit_name "$unit"
        record_state "$unit" active
        exit 0
        ;;
    stop)
        unit="$(unit_from "$@")"
        require_unit_name "$unit"
        record_state "$unit" inactive
        exit 0
        ;;
    is-active)
        unit="$(unit_from "$@")"
        require_unit_name "$unit"
        state="$(active_state_of "$unit")"
        printf '%s\n' "$state"
        [ "$state" = active ] || exit "$INACTIVE_STATUS"
        exit 0
        ;;
    is-enabled)
        unit="$(unit_from "$@")"
        require_unit_name "$unit"
        enablement="$(enablement_of "$unit")"
        printf '%s\n' "$enablement"
        [ "$enablement" = enabled ] || exit "$DISABLED_STATUS"
        exit 0
        ;;
    show)
        unit="$(unit_from "$@")"
        require_unit_name "$unit"
        show_unit "$unit"
        ;;
    enable)
        # `enable --now <unit>`: the only form the agent and the installer use.
        # The verb has already been shifted off above, so `--now` is what is in
        # front now — and `--now` is what makes this record BOTH files: systemd's
        # `--now` means "enable at boot and start it right away", so a stand-in
        # that recorded only the enablement would leave the installer's own gate
        # asking `is-active` about a unit it had just been told to start.
        started=""
        if [ "${1:-}" = "--now" ]; then
            started=yes
            shift
        fi
        case "${1:-}" in
            *.mount) mount_unit "$1" ;;
            '') exit 0 ;;
            *)
                require_unit_name "$1"
                record_enablement "$1" enabled
                [ -n "$started" ] && record_state "$1" active
                exit 0
                ;;
        esac
        ;;
    disable)
        # `disable --now <unit>`: the mirror, and the half that lets a test or a
        # build step ask whether a gate really refuses.
        stopped=""
        if [ "${1:-}" = "--now" ]; then
            stopped=yes
            shift
        fi
        case "${1:-}" in
            *.mount) unmount_unit "$1" ;;
            '') exit 0 ;;
            *)
                require_unit_name "$1"
                # A disable that is REFUSED, which is a state a real host reaches (a unit
                # systemd calls static, a transient dbus failure) and which nothing here
                # could produce before. Nothing is recorded, so `is-enabled` afterwards
                # still answers `enabled` — the whole point of the case.
                refusal="$(state_file "$1" "$REFUSE_DISABLE_SUFFIX")"
                if [ -f "$refusal" ]; then
                    printf 'Failed to disable unit: %s\n' "$(head -1 "$refusal")" >&2
                    exit 1
                fi
                record_enablement "$1" disabled
                [ -n "$stopped" ] && record_state "$1" inactive
                exit 0
                ;;
        esac
        ;;
    list-unit-files)
        # The three answers a real `list-unit-files <pattern>` has, and the reason this
        # arm exists at all: until it did, `list-unit-files` fell through to `*) exit 0`
        # printing nothing, so the polygon could only ever see one of them.
        #
        # The header is printed in BOTH the matched and the unmatched case, because
        # systemd 255 prints it in both — measured. A stand-in that omitted it for the
        # unmatched case would let a caller distinguish the two by emptiness, which no
        # real host allows.
        unit="$(unit_from "$@")"
        require_unit_name "$unit"
        broken="$(state_file "$unit" "$QUERY_BROKEN_SUFFIX")"
        if [ -f "$broken" ]; then
            printf 'Failed to list unit files: %s\n' "$(head -1 "$broken")" >&2
            exit 1
        fi
        if [ -f "$(state_file "$unit" "$INSTALLED_SUFFIX")" ]; then
            printf 'UNIT FILE STATE PRESET\n'
            printf '%s %s enabled\n' "$unit" "$(enablement_of "$unit")"
            printf '\n1 unit files listed.\n'
            exit 0
        fi
        printf 'UNIT FILE STATE PRESET\n'
        printf '\n0 unit files listed.\n'
        exit "$NO_UNIT_FILES_STATUS"
        ;;
    reload)
        # Recorded first, then handled below. The recording is not bookkeeping: it
        # is the only evidence a container can offer that a running service was
        # asked to re-read its configuration.
        unit="$(unit_from "$@")"
        require_unit_name "$unit"
        record_reload "$unit"
        ;;
    *) exit 0 ;;
esac

# The pid FILE is not evidence of a running master: both families' packages ship
# an empty /run/nginx.pid, and `nginx -s reload` against it fails with "invalid
# PID number" — which the agent would correctly report as a failed reload, and
# which would then roll back every write the polygon makes.
pid=$(cat /run/nginx.pid 2>/dev/null || true)
case "$pid" in
    '' | *[!0-9]*) exit 0 ;;
esac

if [ ! -d "/proc/$pid" ]; then
    exit 0
fi

exec /usr/sbin/nginx -s reload
