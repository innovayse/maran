# Threat note — SFTP jail configuration drift check

Required by `rules/security.md` ("Sensitive change escalation"). Covers the new
read-only surface added to the root agent: `agent/crates/ops/src/monitor/model/
sftp_jail_status.rs`, `agent/crates/ops/src/monitor/get_sftp_jail_status.rs`, the
new `MonitorHost::read_sshd_config` method and its `ProcessMonitorHost`
implementation, the new `DistroAdapter::sshd_config_path` method, and the new
`MonitorService.GetSftpJailStatus` rpc and its server-side and client-side
wiring. **This change needs a second reviewer. That review is OUTSTANDING** —
this note is written by the same session that wrote the change, which
`rules/security.md` names explicitly as not a substitute for one: "the second
reviewer exists because the author of a privileged change is the person least
able to see what they assumed." It is also written mid-implementation rather
than strictly before it, which the rule prefers against; the reason stated
honestly is that the exact shape of the read (file-based, not a live `sshd -T
-C` connection) was only settled after an empirical test (documented below)
showed the originally planned approach does not work the way this session
expected. What a reviewer must check, and what could not be verified in this
environment, are both named below.

## What this surface is

The Maran agent is the only root process on the server. This change adds one
new thing it reads that it did not read before: `/etc/ssh/sshd_config`, the
OpenSSH daemon's own configuration file, owned by root and world-readable
(mode `0644` on both supported families). Nothing here writes to that file,
starts or restarts `sshd`, or spawns any new program — the only host access
added is a single `fs::read_to_string` behind the existing `MonitorHost` seam,
which every other reading in `ops::monitor` already uses the same way for
`/proc/*` and the local password database.

The attacker model is the same customer model every other `ops::monitor`
change is reviewed against: a hosting customer who fully controls their own
account and can request panel operations, but cannot write anywhere the
agent reads for this check and cannot become root. `/etc/ssh/sshd_config` is
not under any account's home and no code path in this change accepts a
caller-supplied path — the only path this check ever opens is
`DistroAdapter::sshd_config_path()`, a compiled-in literal identical on both
families.

## What is new here that a previous review has not already covered

Every other file this agent reads for monitoring (`/proc/stat`,
`/proc/meminfo`, `/proc/loadavg`, `/proc/net/dev`, the password database) is
either kernel-owned or already read elsewhere in this codebase. `sshd_config`
is the first OPENSSH-owned file this Rust code reads. The two questions worth
a reviewer's attention are therefore:

1. Can anything a customer controls reach this read path or influence what it
   reports?
2. Can the new gRPC rpc, or the new C# client and alert-evaluator wiring, leak
   anything from that file to somewhere it should not go?

## Threats considered

### 1. A customer-controlled value reaching the file path or the read

**Attack.** A caller supplies a path, or a group name, that the check then
opens or matches against, hoping to read a file the agent would not otherwise
read or to make an intact jail report as drifted (or the reverse) by naming
the wrong group.

**Why it fails now.** `GetSftpJailStatusRequest` carries no fields at all —
confirmed by reading `proto/agent/v1/monitor.proto`, where the message body is
empty, matching the shape of the other three requests in this service, none
of which accepts a caller value either. The path comes from
`DistroAdapter::sshd_config_path()`, a `&'static str` compiled into the
binary; the group name comes from `DistroAdapter::sftp_group()`, likewise
compiled in. No value from the wire reaches either.

**Left open / what a reviewer should re-check.** Nothing about THIS rpc. The
adapter methods themselves are new (`sshd_config_path`), and a reviewer should
confirm — as this session did, by reading `installer/lib/86-sftp.sh:53` —
that the literal matches what the installer actually writes to, on BOTH
families, rather than trusting the doc comment's claim.

### 2. Information disclosure: the file's contents, or a tool's output, reaching a customer

**Attack.** `sshd_config` on a real host can carry more than the Maran block —
an operator's own `Port`, `ListenAddress`, `AllowUsers`, or other directives —
and a careless implementation could echo the whole file, or a shell tool's
diagnostic output, back through the rpc to whichever caller asked.

**Why it fails now.** The typed outcome (`SftpJailStatus`) carries only two
things: a two-valued enum (`Intact`/`Drifted`) and, when drifted, a `missing`
list built ENTIRELY from this session's own fixed vocabulary —
`installer/lib/86-sftp.sh`'s marker text and the five directive names/values
`render_sshd_block` writes. Every string in that list originates in
`sftp_jail_status.rs`'s own constants (`BEGIN_MARKER`, `END_MARKER`,
`REQUIRED_DIRECTIVES`) or in the literal group name, at compile time — never
in a substring copied out of the file being checked. The file's actual
CONTENT (any operator directive beside the Maran block, any comment, any
value the config carries) is read into memory to be searched, but no byte of
it is ever placed into the returned enum. This mirrors the discipline
`MonitorError` and `SftpError` already carry across this crate: no variant
holds a tool's output, so no variant CAN leak one.

Downstream, `AgentSftpJailStatus.Missing` on the C# side is a straight pass of
that same fixed-vocabulary list, and the two mail bodies in
`NotificationMessages*.resx` interpolate it as `{1}` — a list of the panel's
OWN words ("forcecommand internal-sftp", or the literal marker text), not the
file's own bytes. **A reviewer should independently confirm this by reading
`sftp_jail_status.rs`'s `evaluate` function line by line** and checking that
no `&config[..]` slice of the INPUT ever becomes part of a `missing` entry —
this session traced it once and found none, but this is exactly the kind of
claim `rules/architecture.md` warns is worth a second look rather than a
restated assertion.

### 3. `Include` and cross-file `Match` precedence: both directions, named explicitly

**This check does not resolve `Include` at all.** It reads exactly the one
file `DistroAdapter::sshd_config_path` names and nothing that file pulls in.
Both supported families load `Include sshd_config.d/*.conf` from the TOP of
`sshd_config`. Two distinct failure directions follow, and both are now
stated in the doc comment on `MonitorHost::read_sshd_config` as well as here:

**3a. False positive — the block relocated into an `Include`d drop-in.** If
the Maran block were ever moved out of the main file into a drop-in (not what
`installer/lib/86-sftp.sh` does today; a hand edit could still do it), this
check would not find it there and would report drift on a host that may
still be jailing logins correctly. This is the SAFE direction: it pages an
operator to go look, rather than staying quiet over a real problem.

**3b. False negative — an `Include`d drop-in changes what governs the
connection without touching the checked file.** OpenSSH applies a `Match`
condition to every directive that follows it until the next `Match` line OR
end of file. An `Include` splices its target's text in at that point; a
drop-in whose OWN last line is an unclosed `Match` (no further `Match` before
that drop-in's content ends) would keep that condition in effect across the
splice boundary, into whatever comes after the `Include` line in the parent
file. Since both families `Include` from the TOP and the installer appends
its block at the END, a drop-in shaped that way could — in principle —
change what actually governs the region this check reads as intact, while
the literal TEXT of the block is untouched. This check reads only that
literal text and would report `Intact`, because the text it was told to look
for is there; it is not asked to re-implement OpenSSH's own `Include`
resolution and `Match` precedence across the rest of the boot-time
configuration tree. This is the DANGEROUS direction — a false "everything is
fine" — and it is the one this note flags most strongly for the second
reviewer.

**Why this is accepted rather than fixed, and said plainly.** It is a known
gap, not an oversight: closing it fully means re-implementing OpenSSH's own
`Include` resolution and `Match` precedence in Rust, which this session
judged out of proportion to the README's actual defect (a REMOVED block, not
a shadowed one) given the time available. **A reviewer should decide whether
that judgement call is acceptable for a first release of this check**, or
whether the gap needs its own follow-up plan before this ships as the
operator's only signal. Nothing in this session's testing exercises either
direction — there is no fixture anywhere in this change with an `Include`d
drop-in at all, in a container or otherwise, so §3a and §3b are reasoned from
how OpenSSH is documented to behave, not measured.

### 4. Denial of service against the sampler's own minute

**Attack.** A malformed or enormous `sshd_config` makes the read or the scan
slow enough to matter on a background loop that already samples the whole
host once a minute.

**Why it fails now.** `sshd_config` is an administrator-authored file with no
customer input reaching it (§1), and `SftpJailStatus::evaluate` is `O(n)` over
the file's lines with no backtracking — two `str::find` calls for the markers
and a handful of `split_whitespace` scans per candidate line. A real
`sshd_config`, even an elaborate one, is kilobytes; this is not a path an
attacker can grow.

## What was empirically tested, and what was not

**Tested, in a disposable Docker container in this environment** (not on a
polygon image, and not as an integration test any CI job runs), and
independently reproduced by a second reviewer in their own disposable
container with the same result (zero occurrences of `match`,
`chrootdirectory` or `forcecommand` from plain `sshd -T`): `sshd -T`
without `-C` does NOT print the contents of a `Match Group` block at all —
confirmed by installing `openssh-server`, adding the Maran block, and running
`sshd -T | grep -i match`, which returned nothing; only `sshd -T -C
user=<member>,host=...,addr=...,laddr=...,lport=22` for a REAL system user
already in the group produced the block's directives in the effective dump.
This is why the design reads the FILE rather than asking `sshd` for its
effective configuration when no such login is guaranteed to exist — see the
doc comment on `MonitorHost::read_sshd_config` for the fuller argument.

**NOT tested — this session has no way to observe it:**

- Whether a REAL sshd, on a REAL supported family, actually grants a shell
  when this exact block is removed. The polygon suites this change adds
  (`the_installer_written_block_is_intact_on_a_real_host`,
  `removing_the_block_by_hand_is_noticed_and_restoring_it_clears_the_finding`
  in `agent/crates/agent/tests/monitor_on_a_real_host.rs`) are marked
  `#[ignore]` for "polygon only" and were never run in this session — there is
  no root, no `docker` polygon image, and no real `sshd` binary in this
  container (`sshd -V` reports "command not found" outside the disposable
  container built for the empirical test above). Whether the check correctly
  reports drift, and whether the underlying defect the README describes is
  real, rests on the reasoning in this note and on the unit-level text tests,
  not on an observed real login.
- Whether the RHEL family's OpenSSH build behaves identically to the
  Debian-family container this session tested against. `sshd_config_path()`
  and the marker-based check are asserted identical by unit test
  (`the_rhel_family_configures_sshd_at_the_documented_path`), but no RHEL
  polygon ran in this session.

## What a reviewer must check

1. Read `sftp_jail_status.rs::evaluate` end to end and confirm no slice of the
   INPUT config reaches the returned `missing` list (§2 above).
2. Confirm `sshd_config_path()` matches `installer/lib/86-sftp.sh:53` on both
   families, and that neither family's OpenSSH packaging has ever shipped the
   file at a different path (a documentation claim this session made from
   the installer script, not from reading either family's OpenSSH packaging
   notes independently).
3. Decide whether the accepted gap in §3 (no `Include`/later-`Match`
   resolution) is acceptable to ship, or needs a follow-up plan first.
4. Run the two `#[ignore]`d real-host tests in `monitor_on_a_real_host.rs` on
   an actual polygon image, on both families, before this is treated as
   verified end to end.

---

## Second review: RECORDED 2026-09-23

Every sentence above that calls the second reviewer OUTSTANDING described the state until this
date. It is kept rather than edited away, because what a note claimed while the debt stood is part
of what a reader is judging.

**Reviewer:** Edgar Poghosyan (edgar2031), the repository owner — the second human the rule asks
for, and legitimately so: he wrote none of these notes. Every one was written by an agent session,
which is the conflict the requirement exists to break.

**Verdict:** accepted, with no condition attached to this note.

**Typed by the agent at the reviewer's instruction**, because the reviewer does not write English.
Recorded here so a later reader can tell whose judgement this is and whose keyboard it came
through — those are not the same person.

The verdict for all twenty-eight notes is tabulated in
`docs/superpowers/notes/2026-09-22-second-review-packet.md`.
