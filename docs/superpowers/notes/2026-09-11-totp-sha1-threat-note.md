# Threat note — why one-time-code provisioning names SHA1, and why that is not the banned use

Date: 2026-09-11
Surface: `backend/src/Maran.Modules/Identity/Services/TotpService.cs` — `BuildProvisioningUri`
(`:39-42`) and `Verify` (`:44-`). Two-factor authentication is auth, so `rules/security.md`
("Sensitive change escalation") applies.

## THIS NOTE IS LATE, AND THAT IS THE FIRST THING TO KNOW ABOUT IT

`rules/security.md` requires the note **before** the change, because an argument written afterwards
can only validate a choice already made. This is written after the fact: `TotpService` has shipped,
`2026-08-30-auth-threat-note.md` covers TOTP's *window* and *replay protection* and names no
algorithm at all (`grep -nE "SHA|algorithm|HMAC" docs/superpowers/notes/2026-08-30-auth-threat-note.md`
returns one line, about refresh tokens), and the gap was found by a reviewer pass on 2026-09-11, not
by the author of the code. **Reconstruction, not reasoning written forward.** A reader must be able
to tell the difference.

## Second reviewer: OUTSTANDING

No human has reviewed this. The requirement is **OUTSTANDING**, and `fix/live-findings` must not
merge to `main` while this line stands.

## The finding this note exists for

`rules/security.md` item 9 bans "MD5/SHA1 for anything security-relevant". The production tree
contains **five** mentions of those names, and until this note four of them carried a sentence saying
why they were not the banned use:

```
grep -rniE "sha1|md5" backend/src agent/crates/*/src --include=*.cs --include=*.rs | grep -v /tests/
```

- `backend/src/Maran.SharedKernel/Utilities/Tokens/PasswordResetTokenHasher.cs:15` — cites item 9
- `backend/src/Maran.SharedKernel/Utilities/Tokens/RefreshTokenHasher.cs:15` — cites item 9
- `agent/crates/ops/src/backup/archive/checksum_file.rs:22-23` — cites item 9
- `agent/crates/ops/src/backup/archive/extract_databases_as_root.rs:47` — cites item 9
- `backend/src/Maran.Modules/Identity/Services/TotpService.cs:41` — **the only one that did not**

The defect is not in the code. It is that a reviewer grepping for banned algorithms hits line 41,
finds no argument, and has to re-derive one from RFC 6238 — which several reviewers will now do
independently, each spending the same hour, each with a chance of getting it wrong in the alarming
direction. The purpose of this note is that nobody derives it a third time.

## What line 41 actually does, and what it does not

```csharp
return $"otpauth://totp/{label}?secret={secret}&issuer={…}&algorithm=SHA1&digits=6&period=30";
```

This is the `otpauth://` **provisioning URI** — the string an authenticator app consumes, usually
through a QR code, to learn the parameters it must use. The `algorithm` parameter is a *declaration
of the agreed parameter set*, not a choice of a hash for a security property of this panel.

Two things follow, and both are checkable:

- **It is not the collision-resistance or preimage-resistance use item 9 bans.** TOTP is
  `HMAC-SHA1` (RFC 6238, which builds on RFC 4226's HOTP). HMAC's security does not rest on the
  collision resistance of its hash; HMAC-SHA1 has no practical break, and the published SHA-1
  collision work does not reach it. What item 9 exists to stop is a digest standing in for integrity
  or an identity — a file checksum, a token "hash", a signature — and this is neither. The four
  justified hits above are all of that other kind, which is why they each explain themselves in the
  same terms and this one explains itself differently.
- **The panel does not choose this value; the standard does.** RFC 6238's default is SHA-1, and
  essentially every authenticator app assumes it. Sending `algorithm=SHA256` would produce codes a
  large share of apps compute differently or refuse outright, so the practical effect of "upgrading"
  it would be users who cannot enrol — a real availability failure traded for no real confidentiality
  gain. Line 41 is therefore a *conformance* statement.

**And the verification side is the same algorithm whether the string is present or not.** `Verify`
(`:47`) builds `new Totp(Base32Encoding.ToBytes(secret))`, whose default is HMAC-SHA1. So the URI
parameter is the panel *telling the truth about* what it will check, not a knob that selects it. A
reviewer tempted to "fix" the string alone should know that doing so would make the panel advertise
one algorithm and verify another, which is strictly worse than today: every enrolment would silently
break.

## What an attacker would try

Written as attacks, so this note is not just an exoneration.

1. **Break HMAC-SHA1 to forge a code.** Not a path. Forging a TOTP code needs the shared secret, not
   a hash collision. The secret is 160 bits from `KeyGeneration.GenerateRandomKey` (`SecretBytes =
   20`, `:16`), which is RFC 4226's recommendation.
2. **Brute-force the 6-digit code.** The real attack on any TOTP deployment, and the one that has
   nothing to do with the hash. The mitigations are elsewhere and were verified by the 2026-08-30
   note: the window is `VerificationWindow(previous: 1, future: 0)` (`:58`) so exactly two steps are
   ever live, and a last-accepted-window guard (`:63-70`) stops a replay of an intercepted code.
   **A reviewer should check the rate limit on the verification endpoint**, because that — not the
   algorithm — is what bounds a million guesses a minute; it is outside this file and this note makes
   no claim about it.
3. **Read the secret out of the provisioning URI.** The URI carries the secret in the clear, by
   design, because the app must learn it. It is served over TLS to an authenticated session and
   should appear in no log. That is a real surface and it is not this note's — flagged here because a
   reviewer reading line 41 is looking at the same string.
4. **Downgrade the parameters.** The URI is generated server-side from constants; nothing
   caller-supplied reaches `algorithm`, `digits` or `period`. `label` is the one interpolated value
   and goes through `Uri.EscapeDataString` (`:39`).

## The change this note asks for, which is one sentence and no behaviour

`TotpService.BuildProvisioningUri` should carry a `<remarks>` saying that `algorithm=SHA1` is
RFC 6238's mandated default, that HMAC-SHA1 is not the collision-resistance use `rules/security.md`
item 9 bans, and that `Verify` computes the same thing — so the parameter states a fact rather than
selecting one. **That edit is not made here: this session's writable scope excludes `backend/`.** It
is owed, it is one comment, and the code needs no change at all.

## What a second reviewer must check, specifically

1. Re-run the grep at the top and confirm the count is five and that four carry an explanation. If
   a sixth appears, it owes the same sentence.
2. Confirm `algorithm=SHA1` appears **only** in the provisioning URI and never where a digest stands
   for integrity: `grep -n SHA1 backend/src/Maran.Modules/Identity/Services/TotpService.cs` → one
   line, `:41`.
3. Confirm `Verify` does not pass an algorithm and therefore uses the library's HMAC-SHA1 default
   (`:47`) — i.e. that the advertised and the computed algorithm are the same one. This is the check
   that would catch the "fix" described above.
4. Agree, or not, that a conformance parameter is outside item 9. **This is the one judgement in this
   note**, and it is the reason the note exists rather than a code change: the author believes item 9
   is about digests standing in for integrity or identity, and a reviewer is entitled to read it more
   literally and require the rule itself to gain an exception clause naming TOTP.
5. Check the rate limit on the two-factor verification endpoint, which is the actual defence against
   the actual attack and is not in this file.

## What the author could not verify

- That every authenticator app users actually have would refuse `algorithm=SHA256`. The claim is
  about widely-reported behaviour of other people's software; the argument above does not depend on
  it, because the conformance point stands on RFC 6238 alone.
- Anything about the verification endpoint's rate limiting, which is named above as a reviewer item
  rather than asserted here.
