# Threat note — licence installation (§228-229), for an endpoint that does not exist yet

Required by `rules/security.md` ("Sensitive change escalation"), which names licence verification
among the surfaces that need a second reviewer and a written threat note **before** the change. This
note continues `docs/superpowers/notes/2026-09-22-licence-verification-threat-note.md` ("the
verification note") and does not repeat its §1-§8; it covers the *other* half of §228-229 that the
verification note explicitly scoped out — the write side. Today `backend/src/Maran.Modules/Licensing`
can only read a status: `LicensingController` carries one `[HttpGet]` route
(`backend/src/Maran.Modules/Licensing/Controllers/LicensingController.cs:48-94`) and its own remarks
say so directly — *"no write endpoint exists here — installing or replacing a licence is explicitly
out of this slice's scope"* (`LicensingController.cs:13-14`). `ILicenceRawTextSource` has exactly one
implementation, `NoLicenceInstalledTextSource`, which always returns `null`
(`backend/src/Maran.Modules/Licensing/Services/NoLicenceInstalledTextSource.cs:10-17`), and its own
interface doc says why: *"This slice was explicitly scoped without EF persistence: nothing in
`Licensing/Persistence` exists yet"* (`backend/src/Maran.Modules/Licensing/Interfaces/
ILicenceRawTextSource.cs:7-9`). No code exists for the surface this note is about. **This change needs
a second reviewer, and that reviewer is OUTSTANDING** — see §7.

## 1. Where the artefact lands, and with what ownership and mode

The panel already has two directories for state a root-adjacent installer writes versus state the
panel process itself writes at runtime, and they are not interchangeable:

- `/etc/maran` — `root:<service group> 0750`, created by `install -d -o root -g "$MARAN_GROUP" -m
  0750` (`installer/lib/60-config.sh:702`) — holds `panel.env`, written **only by the installer**,
  `root:${MARAN_GROUP} 0640` (`installer/lib/60-config.sh:662-663`). The API process reads it via
  group membership; it cannot write it, by construction (owner `root`, and 0640 grants the group no
  write bit). This is the directory `rules/security.md` item 8 names for "New config values with
  secrets."
- `/var/lib/maran` — `maran:maran 0750`, created by `40-user.sh`, and explicitly **in
  `maran-api.service`'s `ReadWritePaths=`** (`installer/lib/50-artifacts.sh:12-13`). The comment on
  the sibling staging directory states the distinction directly: "*the unprivileged uid the api runs
  as owns it and can place any entry inside it*" (`installer/lib/50-artifacts.sh:13-14`) — this is
  the one directory on the host the panel process can write into itself, at runtime, without a
  privileged helper.

A licence, once installed, needs to be read at every startup by the same process that would install
it — the API — so `/etc/maran`'s root-owned, installer-only model does not fit: nothing in this
design proposes giving the API a privileged helper to shell out to (`rules/security.md` item 3: no
shell strings, no new privileged surface for a job the process can do to its own directory), and
`rules/security.md` item 10 forbids a new daemon for this. **The licence artefact belongs under
`/var/lib/maran`** — concretely `/var/lib/maran/licence.json`, `maran:maran`, mode **0640** (owner
read/write, group read, world nothing). Not 0600: `ILicenceRawTextSource`'s eventual real
implementation and any future read-only tooling running as a member of the `maran` group (there is
none named in the tree today — this is a design margin, not a cited fact) should not need to run as
the exact uid that wrote the file; 0640 is the same shape `panel.env` already uses for
"owner-writes, group-reads," just with the API itself as both owner and (trivially) reader here. Not
world-readable: a licence's `payload` carries the installed tier and module list
(`backend/src/Maran.Modules/Licensing/Domain/Entities/Licence.cs:24-34`), which is operational detail
about what a customer pays for, not a public fact, and `LicensingController` itself is `AdminOnly`
for the identical reason (`LicensingController.cs:17-22`) — the file backing that answer should not
be more open than the endpoint reporting it.

A licence is **not a secret in the password sense** — the verification note is explicit that its
public key is "not a secret to withhold" and that the artefact is a "signed public claim" any
customer is BSL-permitted to read the verifier for
(`docs/superpowers/notes/2026-09-22-licence-verification-threat-note.md` §1, §4) — but it is written
by the same process whose failure mode §229 forbids from taking the core down, and it is read at
every startup on the path `LicenceStartupCheck` already walks
(`backend/src/Maran.Modules/Licensing/Services/LicenceStartupCheck.cs:90-123`). What an operator must
be able to do: read the file directly for their own diagnosis (`0640` group-readable, not opaque) and
overwrite it through the endpoint this note is about. What nobody else may do: no other account on
the host, and no other panel module, gets write access — `/var/lib/maran` is one directory shared by
whatever else the API already keeps there (**NOT FOUND in the tree, searched for what else lives
under `/var/lib/maran` today — `grep -rn "var/lib/maran[^-]" installer/ backend/src agent/crates`
returns only the directory's own creation and `ReadWritePaths=` lines, no second consumer**), so this
design should give the licence file its own name (`licence.json`) rather than share a generic one,
and a future module reusing that directory must not be handed write access to this specific file by
accident — a decision for whoever wires the DI registration that Licensing's own `ILicenceRawTextSource`
implementation is the only writer of that one path, never a shared file-access helper.

## 2. Replacement: what happens to the previous licence

**Never delete-then-write.** A replace that unlinks the old file and then writes the new one has a
window — between the `unlink` and the new file's `rename` landing — where `ReadRawLicenceText()`
returns `null` and every caller, including `LicenceStartupCheck` on the next restart inside that
window, reports `LicenceStatus.Absent`. That is not "the previous licence, still working" and it is
not "the new licence, verified" — it is a panel that, for however long the window lasts, believes
nothing is installed, on a host where something was working a moment before. The verification note's
own §3 is exactly why this matters: `Absent` and `Refused` are different, actionable facts an
operator reads directly, and a delete-then-write replace can manufacture a false `Absent` out of
nothing more than ordinary scheduling — the process could be pre-empted, or (rarer, but this is a
threat note) `SIGKILL`ed, between the two steps by an operator restarting the service at exactly the
wrong moment.

**Also never a direct overwrite of the live file** (`File.WriteAllText` onto `licence.json` in
place): a process killed mid-write leaves a file that is neither the old JSON nor the new one — a
truncated fragment that parses as garbage. Per §3 above, `LicenceVerifier` already treats anything
that fails to parse as `Refused(Malformed)`
(`backend/src/Maran.Modules/Licensing/Services/LicenceVerifier.cs:90-100,131-138`), which is the
CORRECT state for a torn write to land in — but only if the write is contained so that "torn" is the
only failure shape a reader can ever observe. A direct overwrite can also, depending on how the
runtime flushes, momentarily produce a file whose *old* bytes are still fully present but whose
*new* bytes have started overwriting them from the front — neither a valid old licence nor a valid
new one, and unlike a clean truncation this is not guaranteed to fail `JsonDocument.Parse` at all; it
could parse as a spliced, semantically wrong document.

**The correct shape is the one `write_config` already uses for `panel.env`**: write the new bytes to
a temp file in the SAME directory, `fsync` it, `rename()` it over the live path, per the installer's
own comment for the identical hazard — *"a crash mid-write can never leave a half-written secrets
file readable by the wrong mode/owner"* (`installer/lib/60-config.sh:557-560`) and *"a rename is only
atomic within one filesystem"* (`installer/lib/60-config.sh:572-573`). Applied here: `mktemp` inside
`/var/lib/maran` (never `/tmp` — a different filesystem defeats the atomicity the whole argument rests
on, the same reasoning `60-config.sh` gives for its own temp file), write and `fsync` the new licence
JSON, `chown`/`chmod` it to `maran:maran 0640` if the runtime does not already default to that, then
`rename()` over `licence.json`. A `rename()` within one filesystem is a single atomic directory-entry
swap: any reader — including a concurrent request, including the API restarting mid-write — observes
either the complete old file or the complete new file, **never a mix, and never absence**, for as
long as the directory entry existed before the write began. `Malformed` and `Absent` stay the two
states this note's §3 argument requires them to stay: a failed replace can only ever produce
`Malformed` (a fully-written-but-rejected new file) or leave the OLD, still-valid file in place — it
can never manufacture a false `Absent` the way delete-then-write can.

## 3. The order of verification and persistence

**Verify first, in memory, against the uploaded bytes — persist only on `Valid`.** Concretely: the
endpoint receives the raw licence text in the request body, calls `LicenceVerifier.VerifyAsync` on
those bytes directly (the same verifier, the same three-state result, no new verification path), and
only if the result is `LicenceStatus.Valid` does it proceed to the atomic write in §2. A `Refused`
result is returned to the caller as the reason (`LicenceRefusalReason`'s six-member closed set,
already public — `backend/src/Maran.Modules/Licensing/Domain/Enums/LicenceRefusalReason.cs:24-59`)
and **nothing on disk changes.**

The cost of the alternative order — persist first, verify after (i.e. treat the upload as the new
`ReadRawLicenceText()` answer immediately and let the next startup or the next status read discover
whether it was good) — is exactly the failure §2 argues against from a different angle: a syntactically
plausible but wrong upload (wrong product, expired, a forged signature, or simply a customer pasting
the wrong file) would have already replaced a working licence by the time anyone finds out, and the
window is not a race-condition sliver but the entire interval until the next read. A rejected licence
must never have displaced a working one — verify-then-persist is the only order that makes that hold
unconditionally, because nothing is written until the exact predicate that decides `Valid` has
already returned `Valid`.

The cost this order still does NOT avoid — because no order can — is exactly what §5's central
sentence states: verifying before persisting proves the licence is well-formed, signed by the real
key, for the right product, and not expired. It does **not** prove the licence belongs to *this*
server, because `FingerprintMismatch` is unreachable in this codebase today (verified again for this
note; see §5). A valid-but-wrong-server licence still verifies `Valid` and still lands. That gap is
not a persistence-ordering defect — persist-after-verify is still strictly better than the
alternative — it is a gap in what the verifier can check at all, and §5 below is the sentence a
reviewer must not let this note bury as a footnote.

## 4. An interrupted write

The agent's restore-interruption note settled the equivalent question for a two-`rename()` swap with
a rule stated in its own words: *"finish forward, never a mix"* — never roll back to a state nobody
asked for, and never leave a state that is neither the old nor the new
(`docs/superpowers/notes/2026-09-09-restore-interruption-recovery-threat-note.md`, "The decision:
finish forward, not roll back"). This surface is a *single* atomic `rename()`, not a two-step swap, so
it does not need that note's marker-file/reconciliation machinery — but the same rule applies at the
one step this surface has:

- **Killed before the `rename()`.** The temp file (`.licence.json.XXXXXX` or similar, inside
  `/var/lib/maran`) is orphaned; `licence.json` — the live path — was never touched. Reading the
  status afterward reports exactly what it reported before the request was made. This is state 1 of
  the restore note's own table, restated for one file: nothing observable changed, and a leftover
  temp file is litter, not a hazard — it carries no signature material beyond what the uploaded
  licence itself already was, and it should be cleaned up on the next attempt or at startup the same
  way the restore note's scratch reaper cleans up its own leftovers
  (`docs/superpowers/notes/2026-09-09-restore-interruption-recovery-threat-note.md`, "The scratch
  directory"), though this note does not design that reaper — a future implementation's job, named
  here as a requirement rather than left implicit.
- **Killed during the `rename()` itself.** Not a real intermediate state on Linux: `rename()` within
  one filesystem is a single filesystem-journalled operation. The process is either not far enough
  along for the directory entry to have changed (previous bullet) or the swap has already completed
  in the kernel's view (next bullet) — there is no third case, which is exactly why this shape was
  chosen over the two-file swap the restore note had to reconcile after the fact.
- **Killed after the `rename()` returns.** The new licence is live; the write succeeded; nothing to
  recover. The only remaining step is the audit entry (§5) and the HTTP response — if the process
  dies before either, the caller sees a failed request or no response, but a *subsequent* status read
  (`GET api/v1/licence-status`) already reports the new, correctly-installed licence, because the file
  on disk is the fact and the audit/response are reports about it, not the other way around. The
  restore note's own concurrency argument applies here too, in miniature: this endpoint should take
  the same per-resource serialization every other mutating Licensing operation would need (there is
  only one licence file; two concurrent installs must not race two temp-file writes against one
  `rename()` target), though with a single global resource rather than the restore note's per-account
  lock table, a simple in-process lock around the whole verify-then-write sequence is sufficient —
  this endpoint has no reason to be highly concurrent.

**The equivalent rule, stated once:** an interrupted install must finish forward or not start at
all — it must never leave `licence.json` holding anything but a complete file that was fully written
before any rename made it visible. There is no rollback half to argue about, because unlike the
restore note's two renames, this design's one atomic swap has no window in which a partial result
could ever become visible to a reader in the first place.

## 5. What the response and the audit entry may NOT carry

The verification note's §4 already states the module-wide rule and this endpoint inherits it exactly,
extended to what a REQUEST (not only a read) makes newly possible:

- **The raw signed bytes and the Ed25519 signature.** Not in the audit subject (unchanged from the
  verification note), and — new for a write endpoint — **not echoed back in the success response
  either.** A response that echoes the uploaded bytes back "for confirmation" would put the exact
  signed artefact into HTTP response logs, browser history, and any proxy that logs bodies, which is
  strictly worse than the file sitting at `0640` on disk — the file at least stays off the network.
  The success response should look like the existing `LicenceStatusDto` the `GET` route already
  returns (`backend/src/Maran.Modules/Licensing/Common/LicenceStatusDto.cs`) — id, tier, expiry — not
  a copy of the request body.
- **The raw fingerprint inputs.** Still moot for the same reason §5 below restates: nothing in this
  codebase computes a fingerprint from anything, so there is nothing of that shape to leak yet — but
  the rule is recorded now, before the agent RPC that would produce one exists, so it is not an
  afterthought bolted on later.
- **The uploaded text of a *rejected* licence, verbatim, in the audit journal.** A forged or
  malformed upload attempt is itself worth an audit line — WHO attempted an install, WHEN, and what
  reason it was refused for — but not the bytes they submitted, for the identical reason logging the
  accepted licence's raw bytes is forbidden: a rejected attempt might still be a real, valid-elsewhere
  licence someone pasted at the wrong server, and logging it verbatim would put a customer's actual
  licence material into a journal that `rules/security.md` item 8 and the verification note's §4 both
  require to never hold secret-acting material.

**What an installation entry may safely record**, in the same `key=value;key=value` shape
`LicensingAuditJournal.SubjectFor` already uses
(`backend/src/Maran.Modules/Licensing/Services/LicensingAuditJournal.cs:119-129`): `action=Install`
(or `Replace`, if a prior licence's id is known — see next), the outcome (`status=Valid` with the
licence id and tier, exactly as `SubjectFor` already formats a `Valid` read, or `status=Refused;
reason=...` with the closed enum member name), and — new, and safe, because a licence id is
explicitly *not* secret (`LicenceId`'s own doc comment: "Not a secret and not derived from anything on
this server — safe to journal," `backend/src/Maran.Modules/Licensing/Domain/ValueObjects/
LicenceId.cs:5-7`) — the **previous** licence's id when one existed, so an operator reading the
journal later can reconstruct "licence A was replaced by licence B on this date" without either id's
signature or payload ever appearing. `LicensingAuditJournal.RecordFailureAsync` already exists for
exactly this shape and today is asserted, by this module's own tests, to never be called
(`LicensingAuditJournal.cs:98-102`) — a rejected install is the first real caller of it this module
would ever have. The audit journal itself is never deleted (unchanged from every other module's
journal in this panel and from the verification note's own citation of the grant-repair note's
addendum on this point).

## 6. The single most important sentence in this note

**Installing a licence does NOT bind it to this server.** `LicenceRefusalReason.FingerprintMismatch`
is unreachable in this codebase today: its own doc comment states plainly that it is "UNREACHABLE in
this slice" because nothing exposes `machine-id` or the primary network interface
(`backend/src/Maran.Modules/Licensing/Domain/Enums/LicenceRefusalReason.cs:49-58`), and this note
re-verified that claim independently rather than trusting the citation — `proto/agent/v1/
system.proto`'s one rpc, `GetAgentInfo`, returns an `AgentInfo` message carrying exactly five fields:
`version`, `distro_id`, `family`, `proto_version`, `backup_root`
(`proto/agent/v1/system.proto:24-31`, doc comments at `:33-46`); a second independent grep for the
same terms across the whole tree — `grep -rn "machine-id\|MachineId\|machine_id\|primary
interface\|PrimaryInterface" proto/ backend/src agent/crates` — returns **no matches**, confirmed
again for this note, not merely carried forward from the verification note's own citation. So a
licence that verifies `Valid` on this server today verifies `Valid`, byte-for-byte, on **any other
Maran installation running the same public key** — nothing in the signature, the payload, or the
verifier checks which physical or virtual machine is running it, because nothing supplies that input
to check against.

An operator who has just successfully installed a licence through this endpoint will reasonably
assume the opposite: that the green checkmark means *this specific server* is now licensed, and that
copying the same file to a second box would fail. Nothing in this design makes that assumption true.
Until the agent RPC surface §5 of the verification note names as missing is built, the endpoint this
note covers — and its success response — MUST say so explicitly, in the operator-facing text, rather
than let silence imply a binding that does not exist. This is not a caveat to bury in a tooltip: it is
the one fact about this feature that, left unstated, turns "I installed my licence" into a belief the
system does not actually hold.

## 6b. MEASURED GAP: atomicity is the central mechanism and NOTHING tests it

Recorded 2026-09-22 from a mutation run, not from reading. The swap in
`backend/src/Maran.SharedKernel/Utilities/IO/AtomicFileWriter.cs` —
`File.Move(tempPath, targetPath, overwrite: true)` — was replaced with the non-atomic
`File.Delete(targetPath); File.Move(tempPath, targetPath);`, which is precisely the form §2 of this
note rejected. **The whole backend suite stayed green: `20 targets: 3206 passed / 0 failed`.** The
mutant SURVIVED, and the harness withheld the verdict pending a witness, which is this section.

**Why no test noticed.** `The_previous_licence_survives_a_failed_write` reads as though it guards
this, and does not. It injects `ThrowingLicenceWriter`, a fake that throws **before touching the
filesystem**, so the real writer never runs. What that test actually proves — worth keeping — is
that the HANDLER writes no success entry for a failed install. Atomicity is a different claim and
has no guard at all.

**Why a test is not merely missing but currently impossible to write honestly.** Atomic and
non-atomic behave identically unless the DELETE succeeds and the MOVE then fails; only that ordering
loses the previous licence. `AtomicFileWriter` has no seam at the swap step, so that case cannot be
produced deterministically from outside. A concurrency-race test could observe the absence window
delete-then-move opens, but it would be probabilistic, and a flaky gate is worse than an absent one
(rules/testing.md).

**A seam at the swap was TRIED and does NOT close it — recorded so nobody spends the afternoon
again.** The step was made an injectable delegate defaulting to the real rename, and a test staged
the one ordering that distinguishes the two forms: delete succeeds, move then fails. The same mutant
was re-scored and **survived again** — `20 targets: 3207 passed / 0 failed`. The reason is obvious
in hindsight and worth writing down: a test that injects its own swap **never executes the default
one**, so mutating the default changes code the test does not reach. The seam and its test were
reverted rather than left as public API surface buying nothing.

**What would actually close it** is therefore narrower than it looked: the failing-swap ordering has
to occur INSIDE the default implementation, which means either a filesystem the platform can make
fail on rename but not on delete (not available portably), or a process killed between the two
operations of the non-atomic form — which is a real-host polygon exercise, not a unit test. Until
somebody does that, the atomicity argument in §2 and §4 rests on **reading the code**, not on a
measurement, and a reviewer should treat it as such.

**What IS measured, so the section is not read as worse than it is:** a rejected upload never
reaches the writer, and a failing write leaves the previous licence and journals no success — both
killed their mutants. The unproven claim is narrowly that the SWAP ITSELF is atomic.

## 7. What a second human reviewer must check — OUTSTANDING

No human has read this. Per `rules/security.md`, this note may not substitute for that review, and
any implementation of this endpoint may land on a branch but must not merge to `main` until a human
completes this list. Ordered by what a human's attention is worth most on:

1. **Whether verify-then-persist (§3) is actually implemented as one uninterruptible sequence from
   the caller's perspective** — that no code path between `VerifyAsync` returning `Valid` and the
   `rename()` in §2 can be reached with a `Refused` or exception result and still fall through to the
   write. This is a decision no automated test written against a mocked verifier can fully observe,
   because the real risk is a future edit reordering the two steps, not today's code getting it wrong.
2. **Whether the endpoint's success response and its operator-facing copy actually state §6's
   sentence**, and do so in language an administrator reads as a limitation of the current build, not
   as reassurance. This is a product/copy decision as much as a code one, and no test asserts prose
   reads honestly.
3. **What is missing: the atomic-write helper.** Does this codebase already have a generic
   "write-temp-fsync-rename" utility (`rules/README.md`'s "C# Utilities folder" convention would put
   shared logic like this in SharedKernel) that this endpoint should reuse rather than reimplement? A
   second, slightly different atomic-write implementation living only in Licensing is a maintenance
   and consistency risk the reviewer should catch before it is written twice.
4. **What is missing: concurrent-install serialization.** §4's last bullet asserts an in-process lock
   is sufficient for this single-file resource; a reviewer should check whether the actual
   implementation takes one, and whether the panel's multi-instance story (if any — **NOT FOUND in
   the tree, searched for evidence the API runs as more than one process/replica** — this note found
   none, but did not exhaustively rule it out) makes an in-process lock insufficient.
5. **Whether the audit subject actually omits everything §5 forbids**, checked against the real code
   once written, not against this note's description of what it should do — the same caveat the
   verification note's own §7 item 6 states for its journal, extended here to the new write path.
6. **Whether `/var/lib/maran/licence.json` at `maran:maran 0640` is still the right choice once
   written** — this note reasoned it from `50-artifacts.sh`'s comment about that directory's
   ownership and `60-config.sh`'s atomic-write pattern for `panel.env`, but neither of those files
   was written with a licence file in mind, and a reviewer with more context on the installer's
   `maran-api.service` unit (its `ReadWritePaths=`, its `ProtectSystem=` posture — **NOT FOUND in the
   tree, searched for the unit file itself**: `find installer -iname "*.service"` was not run as part
   of this note's research and should be, before this design is trusted) should confirm the unit
   actually grants the write this design assumes.

## 8. What this does NOT achieve

Consistent with the verification note's own §8, restated for the write side rather than repeated:
**a verifier the adversary can read and modify, on hardware they control, deciding whether to accept
an artefact the same adversary can also write to disk by hand, is a deterrent and a bookkeeping
device — never a proof.** An installation endpoint changes none of that arithmetic:

- It does not prove the uploaded licence was issued by Innovayse to THIS customer rather than copied
  from someone else's — signature verification proves the bytes came from the holder of the private
  key, not who is presenting them now.
- It does not prove, and per §6 cannot currently even attempt to prove, that the licence is bound to
  the machine it was just installed on.
- It does not stop an administrator with root from bypassing the endpoint entirely and writing
  `/var/lib/maran/licence.json` directly, or from patching `LicenceVerifier` the way the verification
  note's §1 already establishes they are BSL-permitted to do. The endpoint's only value is to the
  honest majority of operators who use the panel's own UI rather than a text editor, exactly as the
  verification note's §8 already concludes for the read side.
- What it DOES add over hand-editing the file: input validation before anything touches disk (§3), an
  atomicity guarantee hand-editing does not offer for free (§2, §4), and an audit trail of who
  installed what and when (§5) — genuine value for the ordinary operator and for Innovayse's support
  process, and none of it a security boundary against the customer who has decided to defeat it.

## Two assumptions recorded as an agent's, not the owner's

Carried forward unchanged from the verification note's §6, because both still govern this half of the
design identically and the owner has now been asked, across earlier passes on this design, six times
without answering:

1. **The core stays open under BSL 1.1.** If this changes, §1's ownership/mode argument and §8's
   attacker model both need re-argument, not just re-implementation — the same fork the verification
   note names.
2. **No decompilation resistance is attempted.** Unchanged; this note's write path adds nothing that
   depends on this assumption differently than the verification note's read path already does.

Recorded here, again, as assumptions an agent has made in the owner's continued silence — not
decisions the owner took.
