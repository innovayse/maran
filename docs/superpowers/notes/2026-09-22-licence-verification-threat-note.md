# Threat note — licence verification (§228-232)

Required by `rules/security.md` ("Sensitive change escalation"), which names licence verification
explicitly among the surfaces that need a second reviewer and a written threat note **before** the
change. This note is written first, in the order the rule asks for and the home-group repair note
had to name as the order it got backwards (`docs/superpowers/notes/2026-09-13-grant-repair-threat-
note.md`, "This note is LATE"): no `Licensing/` code exists yet (`find backend/src/Maran.Modules/
Licensing -type f` returns only `.gitkeep` files in every subfolder), no proto message carries a
fingerprint, and nothing here has been implemented, run, or tested. **This change needs a second
reviewer, and that reviewer is OUTSTANDING** — see "What a second reviewer must check" below.

## 1. The attacker model, plainly

The verifier that will decide `Valid`/`Absent`/`Refused` ships as ordinary C# in
`backend/src/Maran.Modules/Licensing/`, inside a repository whose root `README.md` states, in its
own words: "Business Source License 1.1 — free to self-host and modify, source available to
everyone" (`README.md:367`, badge at `README.md:7`). That is not a hypothetical adversary reading a
leaked binary — it is the licence terms of this exact repository, granting every customer the right
to read this verifier's source and to modify their own copy of it.

So the adversary this mechanism defends against is **the same person the panel is licensed to**: an
administrator who legitimately holds root on the machine the panel runs on, who is permitted by BSL
to open `LicenceVerifier.cs`, and who needs no exploit to patch it — `sed` and a restart suffice.
They control the CPU that runs the comparison, the clock the expiry check reads, and the disk the
signed licence file and the public key both sit on. Given that, no property this verifier reports can
be a proof to anyone who already has that access; it can only be a fact a *different* party (Innovayse
support, a sales process, an audit trail two reviewers agree to trust) relies on because the honest
majority of customers will not bother to patch it. Every claim this note makes below is scoped to
that honest majority — the deterrent case — and never to the customer who has decided to defeat it,
because that customer already has won by definition (§8 develops this).

## 2. What §229 forbids, and why it is a security property here

Spec text, verbatim: *"деградируют только платные модули; ядро не умирает никогда"* — only paid
modules degrade, the core never dies (`docs/superpowers/specs/2026-08-29-maran-design.md:229`).

Read as a security rule rather than a product preference: **the dangerous failure direction for this
mechanism is failing CLOSED on the core**, which inverts the instinct every other checklist item in
`rules/security.md` teaches. Items 2, 3, 6 and 12 all say the same thing in different shapes — when
in doubt, refuse the operation, 404 instead of 403, reject the ambiguous input. That instinct is
correct for an operation that *does* something (write a file, grant a privilege, run a command).
Licence verification is not that: its answer feeds a gating decision about whether an *add-on
module* loads, and if a bug in that answer — a clock skew, a corrupted signature check, a phone-home
outage — is allowed to propagate into refusing to start the Host, the licensing subsystem becomes a
single point of failure for a customer's production server that a purely product-shaped bug (wrong
tier displayed, wrong expiry date) would never be. A verifier that fails closed on the whole panel
converts every future defect in itself into an outage, which is a strictly worse security posture
than a defect that degrades one paid module. This is why the design ("3. How §229 is honoured
structurally", from a working design note not cited as authority but whose substance is carried
here) types `LicenceVerifier`'s return as a non-throwing `Task<LicenceStatus>` with no
exception arm, and states as a structural promise that no caller in this slice sits on a path that
can prevent the Host from composing or starting.

## 3. The three states, and why `Absent` must never collapse into `Refused`

`LicenceStatus` has three cases, not two and not a bool-plus-reason:

- **`Valid(Licence)`** — a licence is installed and verified; the operator sees tier, modules, expiry.
- **`Absent`** — nothing is installed. This is the ordinary first-run state.
- **`Refused(LicenceRefusalReason)`** — something *was* installed and it did not verify, with one of
  six reasons: `Malformed`, `SignatureInvalid`, `Expired`, `ProductMismatch`, `FingerprintMismatch`,
  `Absent`'s siblings above stop at five active refusal reasons — the sixth entry in the design's own
  table is `Absent` itself, listed there specifically to make the point this section is about: it
  travels through the same `GetLicenceStatusQuery` but is represented as its own top-level case, never
  as `Refused(reason: Absent)`.

Why the distinction is load-bearing rather than cosmetic: each case tells the operator a different,
actionable thing.

- `Absent` says "install a licence" — nothing is broken, there is nothing to undo, and no timestamp
  or file to investigate.
- `Refused(SignatureInvalid)` says "this file was edited, truncated, or forged — get a fresh copy,
  do not hand-edit it."
- `Refused(Expired)` says "renew, and here is the exact date it lapsed."
- `Refused(FingerprintMismatch)` says "this was issued for a different server — use the cabinet's
  self-service transfer" (the remedy §228 itself names).

Collapsing `Absent` into `Refused` would cost the operator the ability to tell "I never installed
anything" from "something is actively wrong with what I installed" — the first is the default state
of every fresh install and every customer on a free tier who has no licence at all, and reporting it
with the same shape as a forged or corrupted file would train operators to treat the ordinary case as
an alarm, and would make an actual forgery indistinguishable from a machine that was never licensed
in the first place. That is the exact trap the code-integrity lane already named for its own three-
state result (`CodeIntegrityOutcome`: `Clean`/`Drifted`/`Unavailable`, never a bool) and the same
argument applies here verbatim: collapsing "nothing to check" into "checked and it failed" is the
shape that looks healthiest while telling the operator the least.

## 4. What must never be logged or journalled

`rules/security.md` item 8: secrets never in logs, error messages, URLs, or git, including anything
that *acts* as a secret. The audit journal this module would write through is never deleted
(established for the grant-repair journal, `docs/superpowers/notes/2026-09-13-grant-repair-threat-
note.md`, "the panel's own audit entry" addendum — the same constraint applies to every module's
journal, this one included, because nothing in `rules/security.md` or `rules/csharp.md` exempts
Licensing's journal from that retention).

What that rules out for this mechanism:

- **The licence's raw signed bytes and its Ed25519 signature.** These are the artefact Innovayse
  issued; logging them verbatim turns a log line into a copy of the licence file itself, defeating
  whatever the cabinet's transfer rate-limit (§228) is trying to bound, and handing a forger a known-
  good signature to study against whatever bytes get logged alongside it.
- **The raw fingerprint inputs — `machine-id` and the primary interface's identifier — un-normalized
  and un-hashed.** `machine-id` is not itself a secret on the host it belongs to, but it is a stable
  identifier of a specific physical or virtual machine; logging it into a journal that lives forever
  and that is visible to Innovayse support tooling creates a permanent cross-reference between "this
  log entry" and "this exact server" for every future line in that journal, which is more than the
  licensing question requires. The design's own value-object note for `ServerFingerprint` says this
  directly: "NEVER logged raw — only its own equality-check outcome ever leaves this type." That is
  the right boundary: the journal may say the fingerprint matched or did not; it may not say what
  either fingerprint *was*.
- **The public key is the one exception, and it is not a secret to withhold** — it is embedded
  in the binary specifically so any customer can read it; there is nothing to protect there.

What a subject line may safely carry, by the same `key=value;key=value` idiom the grant-repair
addendum settled on for its own journal (never prose, never an identifier that could re-identify a
different tenant's row): the licence id (`§228`'s own `id` field — an opaque identifier Innovayse
issued, not a secret), the tier, the refusal reason's enum name, and the expiry date on an `Expired`
refusal. None of those reveal signature material or the fingerprint's raw inputs, and all are exactly
what an operator reading the journal later needs to reconstruct "what did this server's licence say,
and when."

## 5. The dependency the spec did not anticipate

§228 requires the licence to carry "отпечаток сервера `machine-id` + первичный интерфейс" — a server
fingerprint built from `machine-id` plus the primary network interface
(`docs/superpowers/specs/2026-08-29-maran-design.md:228`).

Verified directly, not assumed: `proto/agent/v1/system.proto` (read in full) defines exactly one
rpc, `SystemService.GetAgentInfo`, and its response message `AgentInfo` carries five fields —
`version`, `distro_id`, `family`, `proto_version`, `backup_root` (`proto/agent/v1/system.proto:24-46`).
Nothing there is `machine-id`, an interface name, a MAC address, or any value a fingerprint could be
built from.

- `grep -rn "machine-id\|MachineId\|machine_id\|primary interface\|PrimaryInterface" proto/ backend/
  src agent/crates` returns **no matches**. **NOT FOUND in the tree, searched for `machine-id` /
  `MachineId` / `machine_id` / "primary interface" / `PrimaryInterface` across `proto/`,
  `backend/src`, `agent/crates`.**
- The only existing agent↔API contract that reports anything about the host is `GetAgentInfoResponse`
  at `proto/agent/v1/system.proto:24-31`, and its doc comments (`:33-46`) enumerate exactly what it
  carries — none of it is machine- or interface-identifying.

So the fingerprint half of §228 cannot be built today: it requires a new agent rpc surface (a new
message field on `AgentInfo`, or a new rpc entirely) that does not exist in `proto/`, and correspondingly
new Rust code in `agent/crates` to read `/etc/machine-id` and enumerate the primary interface — none
of which exists (`grep -rn "AssemblyLoadContext" backend/src` finds only the *code-integrity* lane's
files, confirming no `Licensing/`-side code references any of this either). A threat note that did
not say this would let a reviewer approve a licence-verification design that has no way to obtain the
one input §228 names as mandatory — a hole a reviewer needs to see before approving anything that
claims to implement fingerprinting, and the reason this section exists rather than being folded
silently into "future work."

## 6. Two assumptions recorded as an agent's, not the owner's

The owner has been asked, across earlier passes on this design, whether the following two premises
hold, and has not answered by the time this note is written. They are recorded here as assumptions
this note and the design behind it make **in the owner's silence**, not as facts the owner confirmed:

1. **The core stays open under BSL.** Every argument in §1 above — that the adversary can read and
   modify the verifier — depends on the panel's core (including `Licensing/`) remaining source-
   available under BSL 1.1, as `README.md:367` currently states. If the owner later decides the
   licensing module itself should be closed-source or distributed only as a compiled artefact, the
   attacker model in §1 no longer holds as stated and the whole design — the seam direction
   ("closed → open, may I have a verdict"), the choice to
   make the refusal reasons a public enum, the choice to keep expiry comparison in plainly-readable
   C# — would need to be re-argued, not merely re-implemented.
2. **No decompilation resistance is attempted.** `AssemblyLoadContext` (§230) loads ordinary .NET
   assemblies; once decrypted in memory (per §230's own "decrypt with keys in the lease" step), IL is
   trivially readable with any standard decompiler, and this design does not propose obfuscation,
   NativeAOT-only packaging, or any other resistance to that. If secrecy of the *loaded module's*
   logic is later required, it would have to live either in NativeAOT compilation (which the Sdk
   contract model `AssemblyLoadContext` loading does not currently use for paid modules) or on the
   cloud side (§232, outside this monorepo) — not in anything this note or the open-half design
   touches.

**If either assumption is wrong, the design changes, not just the code.** This note does not attempt
to design for the alternative; it names the fork so a reviewer who knows the owner's actual answer
can say which branch applies before code is written against the wrong one.

## 7. What a second human reviewer must check — OUTSTANDING

This review has not happened. Per `rules/security.md`'s escalation rule, this note may not substitute
for it, and the change (when written) may land on a branch but must not merge to `main` until a human
completes this list. Ordered by what a human's attention is worth most on — decisions no test can
observe, then what is missing, then anything a claim and the tree disagree about:

1. **Whether §2's failure-direction argument is actually correct for every future caller, not just
   this slice.** This note argues the *right* shape is "gating code treats `Refused`/`Absent` as a
   reason to skip one module, never as a reason to refuse composing the Host" — but that gating code
   does not exist yet. A reviewer needs to re-check this argument against the actual gating
   implementation once it is written, because a note written before the caller exists cannot verify
   the caller honours the contract it assumes.
2. **Whether the two assumptions in §6 match the owner's actual, eventually-given answer.** If the
   owner answers either differently than assumed here, this note's §1 and §2 need re-argument before
   any code lands, not a quiet edit.
3. **Whether the six-entry refusal set is genuinely exhaustive** over what an Ed25519-signed,
   fingerprinted, expiring JSON document can fail at, or whether a reviewer can construct a seventh
   failure mode (a truncated-but-syntactically-valid JSON that isn't quite `Malformed`, say) that the
   closed enum's `switch` would need to become inexhaustive to handle.
4. **The §5 gap itself: that the new agent rpc/proto surface for the fingerprint, once written, reads
   `/etc/machine-id` and the primary interface without ever transmitting either value raw across the
   wire or into a log** — consistent with §4's "never logged raw" rule, which this note states as a
   requirement for code that does not exist yet and therefore cannot itself verify.
5. **That nothing in the eventual `LicenceVerifier` implementation can throw past its own boundary** —
   this note asserts the design's stated intent (`Task<LicenceStatus>` with no exception arm) but has
   not seen or run the code, so a reviewer must confirm the actual implementation matches the
   contract this note relies on.
6. **That the audit journal subject, once written, is exactly the `key=value;key=value` shape §4
   specifies** — licence id, tier, refusal reason name, expiry date on `Expired` — and carries neither
   signature bytes nor raw fingerprint inputs, checked against the real code rather than this note's
   description of what it should do.

## 8. What this mechanism does NOT achieve

Stated as plainly as the code-integrity note put it for its own mechanism, because the argument is
identical in shape: **a verifier the adversary can read and is licensed to modify, running on
hardware they fully control, is a deterrent and a bookkeeping device — never a proof.**

- It does not prove a customer has not patched `LicenceVerifier.cs` to always return `Valid`. Nothing
  in this design attempts to detect that patch, and §6's second assumption says explicitly that no
  resistance to reading or modifying the loaded module logic is attempted either.
- It does not prove a fingerprint was not spoofed. `/etc/machine-id` and interface identifiers are
  both values the same root who controls the verifier also controls; nothing stops them being set to
  match whatever a copied licence file expects.
- It does not, on its own, prove anything to Innovayse about which physical or virtual server a
  licence is actually running on — only that a customer who has NOT bothered to defeat it will see a
  correctly-refusing or correctly-accepting verifier.
- What it DOES achieve, honestly: it is a functioning gate against copy-paste reuse and casual
  tampering by the ordinary customer who has no reason to patch source they are legally permitted to
  read; it produces a clean audit trail of what a given server's installed licence claimed and when;
  and it gives Innovayse's support and billing processes a fact to reconcile against, which is useful
  precisely because most customers will not bypass it, not because bypassing it is hard.

Any sales or support claim beyond that — "this proves the customer is licensed," "this cannot be
bypassed," "this detects tampering" — overstates what an open-source, BSL-licensed verifier running on
hardware the licensee controls can ever establish. This note is the correction on record for that
overstatement, the same role the code-integrity note's "This mechanism is a deterrent … never a proof"
line already plays for its own surface.
