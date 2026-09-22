# Second-review packet — four threat notes blocking `main`

## What this is, and what it is not

Four threat notes carry an OUTSTANDING second review. `rules/security.md` requires a second
human reviewer on a threat note before the work it covers reaches `main`; nothing in this
repository can satisfy that requirement on a human's behalf, and this file does not try to.
It exists to make the review short: it says where each claim is, which claims are load-bearing,
and — most usefully — which ones the author already believes are the weakest.

The author of all four notes is an AI agent working under the owner's direction. That is
precisely why the second reviewer is not optional: every argument below was written by the
same party that wrote the code it defends.

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
