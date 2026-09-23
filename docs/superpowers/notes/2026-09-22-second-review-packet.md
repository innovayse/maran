# Second-review packet — the 28 threat notes blocking `main`

## What this is, and what it is not

**Twenty-eight** threat notes carry an OUTSTANDING second review, measured against the tree rather
than copied from issue #28, which still says twenty-one. `rules/security.md` requires a second
human reviewer on a threat note before the work it covers reaches `main`; nothing in this
repository can satisfy that requirement on a human's behalf, and this file does not try to.
It exists to make the review short: it says where each claim is, which claims are load-bearing,
and — most usefully — which ones the author already believes are the weakest.

The author of all four notes is an AI agent working under the owner's direction. That is
precisely why the second reviewer is not optional: every argument below was written by the
same party that wrote the code it defends.

## What a machine already checked, so your hour is not spent on it

Run 2026-09-23 over all thirty-two notes. This is fact-checking, not judgement, and it is the half
a reviewer should not have to do by hand:

- **Every file path the notes cite was resolved against the tree.** 121 citations; two had rotted
  and are corrected in this pass — `SendMailRequestedHandler.cs` moved from the Monitoring module
  to Notifications, and `agent/src/shutdown.rs` became a `shutdown/` directory when the drain grew
  its own tests. Both now point at what exists.
- **Every identifier the notes name in backticks was looked for in the code**, tests included. What
  did not resolve is either an external name the note mentions in order to say it is NOT used (PAM
  entry points, SELinux types, systemd directives), a historical reference to something deliberately
  removed (`UnauthenticatedCurrentUser`, the pre-authentication stand-in), or one deliberate case:
  the group-id note names `a_group_recreated_during_a_restore_is_not_applied_from_the_stale_number`
  as **a test that should exist and does not**, and says so in its own text. That is the note being
  honest, not stale.
- **Absolute claims were re-read against the tree** — "unreachable", "cannot be built", "nothing in
  this tree does X". One had become false and was rewritten the same day: the licence-verification
  note said server binding "cannot be built today", which stopped being true when the fingerprint
  rpc landed. The rest hold.

**What the machine cannot check, and why you are here:** whether the mechanism a note DESCRIBES is
the mechanism the code IMPLEMENTS, and whether the risk each note argues is acceptable is in fact
acceptable. A path that resolves proves the file exists, not that it does what the paragraph beside
it claims.

## What this packet covers, and where the other notes are

Counted rather than asserted, because a packet that implies it covers everything is worse than one
that says what it misses:

- **14 of the 28** already have a one-line verdict with the evidence that settles it, in
  `2026-09-11-reviewer-packet.md` §7. That packet is 451 lines and remains the deep reading; it is
  twelve days old, so treat its per-note evidence as sound and its claims about "today's tree" as
  needing a glance. Its table holds 20 rows, six of which are notes no longer in this debt — the
  overlap was counted, not assumed, because 20 was the number I first wrote here and it was wrong.
- **4** are read in depth below — the newest, and the ones whose subject is money rather than
  privilege.
- **10 had nothing at all until this section.** 14 + 4 + 10 = 28, which is the arithmetic that
  matters: every outstanding note is reachable from this file. They get a triage line each: what the note covers,
  and the part of it I would look at first. A triage line is not the deep read the four below got,
  and saying so is the point.

### The ten, with the part worth opening first

| Note | Covers | Look here first |
|---|---|---|
| `cron-firewall-monitoring` (1018 lines) | the installer's privileged firewall steps and the agent's root-side cron read | The firewall steps: they open ports on a live host, and this is the longest note of the set — read its refusals before its mechanisms |
| `backups` (547 lines) | archiving a home and dumping databases as root | The restore half. A backup that fails is a lost copy; a restore that fails halfway is a live account in an unknown state |
| `grant-pattern` (221 lines) | `grant_pattern_for` and the `GRANT ALL PRIVILEGES` it feeds | Whether the escape is applied at the ONE site that composes the statement. This is the defect where one customer could reach another's database |
| `grant-repair` (353 lines) | the `RepairDatabaseGrants` rpc | That it touches only rows it recognises as its own. A repair that rewrites a row it did not create is worse than the defect |
| `home-group-repair` (212 lines) | `repair_home_groups`, run as root over customers' homes | Its six refusals. It chowns a directory a customer owns, so every path assumption it makes is a privilege question |
| `setup-token-in-a-url` (355 lines) | the one-time token that creates the first administrator, printed in a link | Where the token can be logged — a URL travels into access logs, proxies and shell history |
| `totp-sha1` (129 lines) | the algorithm in the provisioning URI | One judgement: whether SHA1 in TOTP is acceptable to you. It is the industry default and it is also the thing a customer's auditor will ask about |
| `suspension-session-cull` (280 lines) | the agent ending a customer's live sessions when an account is locked | What it signals and to whom. A cull that matches too widely kills the wrong processes as root |
| `sftp-jail-drift` (216 lines) | a read-only surface reporting whether the sshd jail block is intact | Whether "cannot read the file" is reported as drift or as unknown. Reporting a jail as intact because the check failed is the shape this must not have |
| `cron-allowance` (108 lines) | one additive proto field bounding how many cron entries an account may have | The smallest note of the set. Whether the absent field means "no limit" rather than "allow nothing" |

## How to review one note in about ten minutes

For each note, the questions that matter are the same three:

1. **Is the mechanism described the mechanism implemented?** The notes cite files and line
   numbers. Open two or three of them. A note that describes a check the code does not perform
   is the failure mode worth catching, and it is invisible from inside the note.
2. **Is every "UNOBSERVED" honest?** Each note lists what it does NOT guard. The risk is not a
   missing entry — it is an entry phrased so mildly that a reader skims past a real gap.
3. **Does a refusal fail in the safe direction?** Every one of these four touches something
   that refuses. Ask what happens when the check cannot run at all.

## The four notes, weakest claim first

### 1. `2026-09-22-licence-installation-threat-note.md` (380 lines)

The longest, and the one whose history is most worth reading. §6b was an open, measured gap for
most of its life: atomicity was the central mechanism and nothing tested it. It is now closed —
the non-atomic mutant dies — but §6c deliberately keeps BOTH failed attempts, because the
instinct that produced them (stage a failure, which means inject it, which means replace the
code under test) is the reusable lesson.

**Weakest claim:** the closing test observes ORDERING only. A machine killed mid-write is still
unobserved, and the note says so. A reviewer should decide whether that residue is acceptable
for a paid artefact, not merely whether it is disclosed.

### 2. `2026-09-22-licence-verification-threat-note.md` (238 lines)

Covers offline verification: signature, product, expiry, and — since this change — server
binding. Nine mutants killed on the open half.

**Weakest claim:** binding refuses when this host's identity cannot be READ at all. That is
deliberate and it costs an honest customer a refusal whenever their agent is down. The
argument for it is in `LicenceServerBindingPolicy`'s own remarks: the other direction hands
anyone who can stop the agent a licence valid on every machine they own. The reviewer should
weigh that trade rather than accept it — it is a product decision wearing a security argument's
clothes, and the note names the condition under which to revisit it (the day licence status
starts gating a feature; today it only reports).

### 3. `2026-09-19-release-bundle-signing-threat-note.md` (106 lines)

Covers signing and verifying the release bundle. Before this work the installer could not run
at all, so the note describes a mechanism with no deployed predecessor to compare against.

**Weakest claim:** key custody is out of scope of the note. The signing key's handling is
asserted, not argued.

### 4. `2026-09-19-code-integrity-manifest-threat-note.md` (110 lines)

Covers the manifest over shipped files.

**Weakest claim, and it is a structural one:** this panel is published under BSL 1.1, source
available to everyone and modifiable. CWP can hide its PHP because CWP does not publish it.
Integrity detection here tells an OPERATOR that files changed; it cannot stop a determined
party who has the source. The note says this. A reviewer should confirm the note does not, in
any passage, let the reader infer more protection than that.

## Two standing assumptions the owner has not ruled on

Both were recorded as AGENT assumptions, asked repeatedly, and never answered. They are listed
here because a reviewer will otherwise assume they were decided:

1. **The core stays open under BSL 1.1.** Every integrity argument above is written on that
   assumption.
2. **No decompilation resistance is attempted.** The licensing binary is not hardened against
   being read.

If either is wrong, notes 3 and 4 need rewriting rather than reviewing.

## Where to record the verdict

`rules/security.md` asks for a recorded verdict, and until now this packet asked for one without
giving anywhere to put it. Write it here, in this file, and commit it — a verdict that lives only
in a conversation is a verdict the next reader cannot find.

One line per note is enough. What it must carry: **who** read it, **when**, and **what they
decided** — accepted, accepted with a condition, or refused. "Looks fine" is not a verdict; the
condition is the useful part.

**Recorded 2026-09-23.** The repository owner, Edgar Poghosyan (edgar2031), read the notes and
accepted them. He is the second reviewer the rule asks for, and legitimately so: he did not write
them — every one was written by an agent session, which is the exact conflict the requirement
exists to break.

**Who typed this.** The reviewer does not write English, so the verdict was dictated and this table
was filled in by the agent on his instruction. That is recorded because provenance is the whole
value of a signature: a future reader must be able to tell whose judgement this is and whose
keyboard it came through, and those are not the same person here.

**Unconditional unless amended below.** No per-note condition was given. If the reviewer attaches
one to any note, it belongs in this table beside that note — a condition remembered in conversation
and not written here is a condition nobody will find.

| Note | Reviewer | Date | Verdict |
|---|---|---|---|
| `2026-09-04-cron-firewall-monitoring` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-05-backups` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-07-installer-privileged-steps` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-08-account-password-state-attestation` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-08-account-unlock` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-08-agent-shutdown` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-09-account-deletion-exclusion` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-09-agent-error-seam-and-path-claim` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-09-ftps` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-09-group-id` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-09-identity-account-deletion` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-09-restore-interruption-recovery` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-09-service-account-rename` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-09-sftp-password-suspension` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-09-site-logs` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-09-transfer-login-deletion` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-11-setup-token-in-a-url` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-11-totp-sha1` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-12-suspension-session-cull` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-13-cron-allowance` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-13-grant-pattern` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-13-grant-repair` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-19-code-integrity-manifest` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-19-home-group-repair` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-19-release-bundle-signing` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-19-sftp-jail-drift` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-22-licence-installation` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |
| `2026-09-22-licence-verification` | Edgar Poghosyan (edgar2031) | 2026-09-23 | Accepted |

A note left out of this table is a note nobody signed, and `main` is not compliant while any of the
28 is missing from it. That sentence is here so the table cannot be half-filled and read as done.
