# Threat note: the panel boots with the published sample keys (issue #86)

Date: 2026-10-26
Branch: `fix/86-identity-sample-keys`, based on `dev` at `7e75b67`. (`dev` has since moved to `697cad1`,
which touches only `README.md` and `docs/screenshots/`: `git diff --stat 7e75b67 697cad1`. Every
census below is anchored to `7e75b67` so it reads the same whatever `dev` or the working tree holds.)
Surface: the token-signing key (`Jwt:SigningKey`, `Maran.Modules/Identity/Options/JwtOptions.cs`) and
the secret-encryption key (`Security:EncryptionKey`, `Maran.Host/Configuration/SecurityOptions.cs`),
the startup validation that admits them (`Maran.Host/Extensions/ConfigurationExtensions.cs:42-66` at
`7e75b67`), and the bearer handler that trusts whatever the signing key signs
(`Maran.Host/Extensions/AuthenticationExtensions.cs:28-49`).

This is auth and session-token handling, so `rules/security.md` "Sensitive change escalation"
applies: a second reviewer and a threat note, with the note written first.

**This note was written before any code change.** When it was written the worktree was clean at
`7e75b67` (`git status --short` printed nothing), and nothing in `backend/**` or `.env.example` had
been touched for this issue. The plan below is an argument for a change not yet made, so a reviewer
can hold the diff against it rather than read the note as a description of the diff.

**Re-checks, same day, before any further code.** Three later runs of this same stage re-read the
note against the rules and the code. All found in-flight code from an interrupted implementation run:
`backend/src/Maran.SharedKernel/Security/SecretKeyPolicy.cs` (new), an edited
`backend/src/Maran.Host/Extensions/ConfigurationExtensions.cs` and an edited `.env.example`. Their
modification times (`stat -c '%y %n'`: 18:48:55, 18:58:47 and 18:58:26 +0400) are all later than this
note's first version (18:46:02), so the order the rule requires held; each re-check edits the note,
so its own mtime no longer shows that and these timestamps are the record.

- The first re-check changed no part of the plan. It re-anchored the censuses the in-flight code
  changes, widened the session-hook search to all of `backend/src`, cited the two-factor gate, and
  added the case-insensitive and multi-source environment name to the decisions a reviewer rules on.
- The second re-check (19:10 +0400) split the plan into the three parts this branch is now worked in
  and wrote down the **contract between them** (below), so the parts can be written in parallel. It
  made one change to the plan and says so: the equal-keys question widens from "equal, or one begins
  with the other" to "equal, or one **contains** the other anywhere" — the argument for the prefix
  ("holding it gives the other key just the same") does not depend on the offset, so the prefix was
  an arbitrary subset of its own justification. The in-flight code implements the prefix and is
  corrected to the contract by the parts that own it. It also recorded the installer scout's answer,
  one new installer fact (a hand-set environment name does not survive a re-run), and moved every
  `dev` census to `7e75b67`.
- The third re-check (after 19:13 +0400) changed no part of the plan and no part of the contract. It
  found the in-flight files unchanged since the second (the same three mtimes; `git status
  --porcelain` lists exactly those three and this note), so the in-flight table below still
  describes them, and `dev` still at `697cad1`. It widened the census of API launches that the second
  correction to the issue rests on to every invocation naming the host project, and found none that
  runs it outside Development (correction 2). And it recorded one adjacent published secret that
  this issue does not cover, the development setup token, under "What the change does not close",
  for the owner to rule on rather than for this branch to absorb. Two tables (the parts, and the
  in-flight code) became lists with the same content, because their rows broke the repository's
  120-column limit (`.editorconfig`, `[*]`), which no other note here exceeds.

## Second reviewer: OUTSTANDING

No human has reviewed this change, and an agent session cannot be the second reviewer. The
requirement is **OUTSTANDING**. `fix/86-identity-sample-keys` may land on its branch; it **MUST NOT
merge to `main`** until a human second reviewer has read this note and the diff and signed off below.

## What the two keys are and what each one buys

- **The signing key** is the HMAC-SHA256 key every access token is signed with
  (`JwtAccessTokenIssuer`) and verified against (`AuthenticationExtensions`). The bearer handler
  checks the signature, the issuer, the audience and the lifetime, and **consults no session**:
  `git grep -nE "OnTokenValidated|JwtBearerEvents|IClaimsTransformation|JwtBearerOptions" 7e75b67 -- 'backend/src/*.cs'`
  prints nothing, across the Host and every module (positive control: the same command with
  `|AddJwtBearer` added to the pattern prints exactly
  `7e75b67:backend/src/Maran.Host/Extensions/AuthenticationExtensions.cs:29`). A signature check is
  therefore the whole of authentication.
- **The encryption key** is the AES-256-GCM key behind `EncryptedStringConverter`. The census of
  columns that go through it,
  `/usr/bin/grep -rn "new EncryptedStringConverter(" backend/src --include=*.cs | grep -v /obj/`,
  finds three (plus the usage example in the converter's own doc comment):
  `identity.Users.TotpSecret`, `ssl.AcmeAccounts.PrivateKeyPem` and
  `notifications.SmtpSettings.Password`. Nothing else calls `Encrypt`/`Decrypt`
  (`/usr/bin/grep -rn "\.Decrypt(\|\.Encrypt(" backend/src --include=*.cs` finds only the converter
  and the cipher itself).

**Two corrections to the issue, where the code says otherwise:**

1. The issue lists "backup destination credentials" among the encrypted secrets. There are none:
   `BackupDestination`'s remarks state that no S3 credential column exists on this build, and the
   census above finds no converter in the Backups module. What the key does expose is the three
   columns above, and the first of them matters more than the issue says (see attack 2).
2. The issue says the CI handshake job "sets the key without setting an environment". The workflow
   step (`.github/workflows/cross.yml:113`) sets only `Security__EncryptionKey`, but the harness it
   runs starts the API with `ASPNETCORE_ENVIRONMENT=Development` (`scripts/lib/e2e-handshake.sh:191`),
   and takes `Jwt__SigningKey` from `.env`, which `scripts/dev` (sourced at `e2e-handshake.sh:41`)
   creates from `.env.example`. So the handshake runs in Development and keeps working under a
   Development allowance with no workflow change. Every other API launch the scripts make
   (`dev-stages.sh:285`) is Development too:
   `/usr/bin/grep -rnE "dotnet run|Maran\.Host\.dll" scripts .github docker` finds exactly those two
   launches (`e2e-handshake.sh:194`, `dev-stages.sh:285`); its other hits are comments. That pattern
   cannot see a launch that names the project rather than the binary, so the third re-check widened
   it: `/usr/bin/grep -rnE "dotnet run|Maran\.Host\.dll|Maran\.Host\b" scripts .github docker` also
   finds the EF tooling (`scripts/lib/migrations.sh:514-520`, `ef database update` and
   `ef migrations list` with `--startup-project Maran.Host.csproj`), and both lines set
   `ASPNETCORE_ENVIRONMENT=Development` themselves; and `release-bundle.sh:170-180`, which publishes
   the host and never runs it. The SPA's Playwright lane starts no API at all (its `webServer` is
   `npm run preview`, `frontend/playwright.config.ts:71`, and the API is mocked from
   `e2e/fixtures/`). Its positive control is itself: the widened pattern hits the two launches above.
   The one place outside a test that writes a key into a `panel.env` is a polygon assertion
   (`docker/polygon/asserts/parts/service-account.sh:359`), which writes `Security__EncryptionKey=x`
   into a throwaway file to test a rename step, starts nothing, and deletes it.

## What an attacker can do with this surface today

**Precondition, stated at its real size.** The sample value reaches a server only by hand. The
installer generates both keys separately with `openssl rand -base64 32`
(`installer/lib/60-config.sh:37`, used at `:707` and `:719`), `installer/panel.env.example` ships
both empty, and nothing falls back to the sample. But a re-run preserves whatever `panel.env` holds
(`existing_value`, `60-config.sh:693-697`, read at `:705` and `:717`), so a key seeded by hand —
from the root `.env.example` because "it boots locally", from a configuration-management template,
from an image built outside the installer — stays for the life of the host. The systemd unit sets no
environment (`installer/systemd/maran-api.service` has only `EnvironmentFile=`, and
`ExecStart=/usr/local/maran/api/Maran.Host` at `:36` passes no argument), so such a host runs as
Production.

On such a host, anyone who has read this public repository can:

1. **Become the administrator over HTTP.** The sample value decodes to the 32 ASCII bytes
   `0123456789abcdef0123456789abcdef`. With it an attacker signs a token carrying the administrator
   role claim, the public issuer and audience (`maran` / `maran-panel`, `appsettings.json`), any
   `sub`, and any `exp` they like. `AdminOnly` accepts it. The two-factor enrolment gate is a claim
   in the same token, so the forger simply leaves it out:
   `Maran.Modules/Identity/Authorization/TwoFactorEnrolmentCompleteHandler.cs` succeeds whenever the
   principal carries no `PanelClaimTypes.TwoFactorSetupRequired` claim. No password, no session, no
   rate limit and no audit line of a sign-in is involved, because no sign-in happens.
2. **Defeat every second factor and take the panel's other credentials from a dump.** With a
   database dump or a backup, the key decrypts `TotpSecret` for every user who enrolled, which is
   the seed that generates their codes: the second factor stops being a factor. It also yields the
   ACME account's private key (so the attacker can act as the panel's account at the certificate
   authority, including revoking its certificates) and the SMTP password for the operator's mail
   provider.
3. **Get both keys for the price of one.** `.env.example` uses the same value for both. A host whose
   two keys are equal — sample or not — gives the encryption key to whoever obtains the signing key,
   and the reverse.
4. **Guess a weak key offline** (the low-entropy case, which this branch adds to the issue's scope).
   A key an operator typed — a passphrase base64-encoded, a repeated pattern, a padded word — is a
   key with far less than 256 bits behind it. One access token, seen once in a browser, a proxy log
   or a support screenshot, is a complete offline test for a signing-key guess; one ciphertext from
   a dump is the same for the encryption key. Neither guess touches the server.
5. **Rely on nobody noticing.** The boot succeeds, `/health` answers ok, and nothing in the panel
   compares a key with the published value, with a structural floor, or with the other key.

Attacks 1 to 3 need nothing but this repository. Attack 4 needs one token or one dump.

## The change planned, in three parts

- **key-policy** (wave 1) owns `backend/src/Maran.SharedKernel/**`: the one type that judges key
  material.
- **samples-docs** (wave 1) owns `.env.example`, `docker/.env.example` (comments only) and the
  documentation lines that describe key setup: the sample files say plainly that their keys are
  refused outside Development and Testing.
- **startup-wiring** (wave 2) owns `backend/src/Maran.Host/**`,
  `backend/src/Maran.Modules/Identity/Options/**` and `backend/src/Maran.Host/Configuration/` (the
  folder of `SecurityOptions`): applies the policy at startup, with the environment allow-list and
  the refusal messages.

### key-policy

1. **One policy type in `Maran.SharedKernel`**, answering three questions about **decoded** key
   bytes: is this a published development key; is it structurally low-entropy; are these two keys
   the same key. One place, so the signing-key check and the encryption-key check cannot drift apart.
   It holds **no environment logic** — which environments may run with a refused key is a decision of
   the composition root, not of the kernel, which reads no configuration of its own
   (`SecurityExtensions.cs:29-30` says the same of the cipher).
2. **Its folder, by `rules/csharp.md` "`Security/`, `Utilities/` and a module's `Common/`", questions
   asked in order.** Question 1 (one module's own tables, contract or vocabulary?): no — it judges
   both the Host's encryption key and Identity's signing key, and takes only strings. Question 2
   (holds a secret, or states a policy an operator or a stored row depends on?): **yes** — it is the
   panel's one answer to "which key may this panel run with", the operator's `panel.env` depends on
   it, and a second answer would be a defect. So `Maran.SharedKernel/Security/SecretKeyPolicy.cs`,
   beside `AesGcmEncryptionService` (the key it judges) and `SecretRedactionPolicy`. The issue
   proposes `Utilities/`; question 3 is never reached, and the rule wins over the issue. The
   interrupted run already put it there, and by these questions it stays.
3. **No contract in `Interfaces/`.** The type is a `static` pure function over strings with no
   dependency, no state and no DI lifetime — the shape of `SecretRedactionPolicy` and
   `PasswordHashParameters`, both `public static class` in the same folder. There is no seam to
   substitute: a test calls it directly, and a fake policy in a test host would be the one thing that
   must never be possible. An interface would be a speculative abstraction (`rules/architecture.md`,
   YAGNI).

### samples-docs

1. **`.env.example`'s comments** say that the sample values are refused outside Development and
   Testing, what else is refused, that an unset environment is Production, and the remedy — so an
   operator who copies the file learns it from the file rather than from a failed boot. The values
   stay (`rules/security.md` item 7: safe placeholders; the tests and the handshake need this one).
2. **`docker/.env.example` carries no key** (`/usr/bin/grep -n "Key" docker/.env.example` prints
   nothing; the same command over the root `.env.example` prints its key lines, the positive
   control), and the dev compose stack runs no API container
   (`grep -n "^  [a-z]" docker/docker-compose.dev.yml`: `postgres`, `agent`, `website`) — the API is
   `dotnet run` with the root `.env`. So it needs at most a one-line comment pointing at the root
   file for the keys; it must gain no key line. (Observed, not in this issue's scope: its own comment
   "ACME, for the API container" names a container the compose file does not define.)
3. **Documentation lines that describe key setup.** Outside the two sample files the only current
   ones are `installer/panel.env.example:11-19`, which describe the server's keys. A comment there
   saying the panel refuses a published, text-shaped or shared key is in this part's scope and is
   comment-only; it must add no `KEY=` line, because
   `docker/polygon/asserts/parts/config-and-finish.sh:368-421` compares the documented keys against
   what `60-config.sh` writes. `docs/superpowers/plans/*` are historical plans and are not edited.
4. **The installer scout's answer: the installer can keep the sample keys only if a human put them
   there first, so no installer edit.** `60-config.sh` never reads `.env.example` (`/usr/bin/grep -rn
   "env.example" installer/lib` finds only the comment at `60-config.sh:582` naming
   `panel.env.example`); it generates both keys when `panel.env` lacks them and preserves them when
   it has them. The one path that keeps a sample key is a `panel.env` seeded by hand before the
   install or between runs. That path is now refused by the panel itself (startup-wiring), and an
   installer-side refusal would be a second copy of the policy in shell — exactly the drift the
   single type exists to prevent. On a re-run the refusal surfaces at the installer's schema step
   (see "What the change does not close").

### startup-wiring

1. **Applied in the existing `.Validate(...)` callbacks**, which already run under `ValidateOnStart`,
   keeping the `ValidateDataAnnotations().Validate(...).ValidateOnStart()` shape `rules/csharp.md`
   requires, with `IHostEnvironment` taken through `Validate<TDep>`. The refusal applies everywhere
   **except** the Development and Testing environments: an allow-list, not "refuse in Production".
   Production, Staging, an unset variable (which .NET reads as Production) and a misspelt name are
   all refused, because the failure this exists for is a server nobody configured carefully.
2. **The equal-keys check needs both options at once.** It is applied from the `JwtOptions`
   registration, with the encryption key read from `IConfiguration["Security:EncryptionKey"]` rather
   than from `IOptions<SecurityOptions>` — resolving those options inside the callback would run their
   own validators there, and a refusal of the encryption key would surface as a failure of the
   signing key. It must run at start, not at the first request.
3. **The refusal message** names the setting, the class of problem (a published development key; a
   key too structured to be random; the signing key the same as the encryption key) and the remedy —
   and **never the value or any byte of it**. The remedy differs per key and the message says which:
   - the signing key can be replaced freely (`openssl rand -base64 32`); every session signs out;
   - the encryption key can be replaced only at a cost: what was encrypted under the old key can no
     longer be read (see "What the change does not close");
   - for the same key, the message tells the operator to replace the **signing** key, which loses no
     data, and leave the encryption key alone.
4. **`AddPanelAuthentication` is not changed.** It reads the signing key straight from configuration
   at composition time (`AuthenticationExtensions.cs:22`); the protection rests on the host refusing
   to start before anything is served (reviewer check 12).

### Tests, in the dedicated pass after the code (`rules/testing.md`)

- Unit, `backend/tests/Maran.SharedKernel.Tests/Security/SecretKeyPolicyTests.cs`: each question
  answers yes for its case and no for a fresh `RandomNumberGenerator.GetBytes(32)` key (the inverse
  control); the published value with surrounding whitespace is still published; null, empty and
  non-base64 answer no to all three; a 64-byte signing key containing the encryption key at offset
  0, at a middle offset and at the end is the same key.
- Host, beside `backend/tests/Maran.Host.Tests/Configuration/SecurityOptionsTests.cs`: a host
  started in `Production` with the `.env.example` keys refuses to start with the stated message, and
  the same test is red against `7e75b67`; two distinct valid random keys set equal to each other are
  refused; a structurally weak key is refused; an environment that is neither Production nor on the
  allow-list (`Staging`) is refused; and the **inverse controls** — the same Production host with two
  independent random keys boots, and a Testing host with the sample keys boots — because a guard
  mutated to refuse everything passes every test that only hands it bad keys. No file under
  `backend/src` reads the environment at `7e75b67` (census under "Decisions"), so a Production test
  host differs from a Testing one only in framework behaviour. The existing suites stay in `Testing`
  with the sample value unchanged: every test host that sets a key also runs as `Testing`
  (49 files set one; the only file without its own `UseEnvironment("Testing")`,
  `SecurityOptionsTests.cs`, builds on `PanelTestFactory`, which sets it at `:27`).

**Why this makes the surface safe.** It does not make a leaked key safe; nothing can. It turns the
silent state — a Production panel serving with a public key — into a panel that does not serve at
all, with a message that tells the operator why and what to do. Attacks 1 to 3 then need a server
that is not running, and attack 4 is narrowed to keys that look random.

## The contract between the parts

Written so key-policy and startup-wiring can be written in parallel and samples-docs can describe the
behaviour without reading either. A part that needs something not listed here changes this section
first.

**key-policy provides** (`Maran.SharedKernel.Security`, file `Security/SecretKeyPolicy.cs`):

```csharp
public static class SecretKeyPolicy
{
    public static bool IsPublishedDevelopmentKey(string? base64Key);
    public static bool IsLowEntropy(string? base64Key);
    public static bool AreSameKey(string? firstBase64Key, string? secondBase64Key);
}
```

- **Input** is the configured string exactly as the options hold it (base64 text). Each method
  decodes with `Convert.FromBase64String`; comparison is on the decoded bytes, never on the text.
- **Null, empty, whitespace-only, non-base64 or zero-length-decoded** input answers `false` to every
  method, for both arguments of `AreSameKey`. That is not approval: the format callbacks beside the
  callers already refuse such a value, and answering here would repeat their message under a
  misleading name.
- **`IsPublishedDevelopmentKey`**: true when the decoded bytes equal the decoded bytes of a value this
  repository publishes. Today that list is exactly one value (the census under "What the author could
  not verify"), and it is the **only** copy of the published value in `backend/src`.
- **`IsLowEntropy`**: true when every decoded byte is printable ASCII (`0x20`–`0x7e`) **or** the key
  holds at most **16** distinct byte values. Nothing broader or looser (reviewer check 6). The
  published value answers true here too; the caller decides which message wins.
- **`AreSameKey`**: true when the two decoded keys are equal, or the shorter one occurs as a
  contiguous byte sequence anywhere inside the longer one. Symmetric in its arguments.
- **Never throws, never logs, never returns or formats key material.** The type holds no state and
  reads no configuration and no environment.

**startup-wiring consumes it, and owns**:

- the allow-list `IHostEnvironment.IsDevelopment() || IHostEnvironment.IsEnvironment("Testing")`,
  in one private member of `ConfigurationExtensions`, and nowhere else;
- five refusals, each a `Validate` callback that returns true when the allow-list admits the host:
  encryption key published; encryption key low-entropy and not published; signing key published;
  signing key low-entropy and not published; signing key the same key as
  `IConfiguration["Security:EncryptionKey"]`. A published key is reported once, as published;
- the refusal texts, as English operator-facing literals (`rules/csharp.md`: operator-facing
  messages are not localized), each naming the setting as `Security:EncryptionKey` or
  `Jwt:SigningKey`, never a byte of the value;
- the existing format callbacks and their messages, unchanged in meaning (the existing tests assert
  on `"EncryptionKey"` and `"256-bit key"`).

**samples-docs describes exactly this behaviour**, and nothing broader: refused outside Development
and Testing (matched without regard to case); an unset `ASPNETCORE_ENVIRONMENT` is Production and is
refused; refused are the published value, a key that decodes to text or to a short repeated pattern,
and a signing key equal to or containing the encryption key; the remedy is a separate
`openssl rand -base64 32` per key, and the installer does that itself.

**The in-flight code against this contract** (updated after the agent review): the three
mismatches noted earlier are fixed. `AreSameKey` now checks containment at any offset and its doc
matches; the Host message says "contains it"; `.env.example` says "equal to, or containing,". The
plan and the code agree.

## Decisions the reviewer is asked to accept or overturn

- **`Testing` becomes a name production code trusts.** No file under `backend/src` reads the host
  environment at the branch base:
  `git grep -nE "I(Web)?HostEnvironment|Is(Development|Production|Environment)" 7e75b67 -- 'backend/src/*.cs'`
  prints nothing. (Positive control for the alternation and the tool:
  `git grep -nE "UseEnvironment|IsDevelopment" 7e75b67 -- 'backend/tests/*.cs' | wc -l` prints `48`, and
  all 48 are the test hosts' `UseEnvironment("Testing")` calls. Over the working tree the first
  command hits the in-flight `ConfigurationExtensions.cs`, which is the change and not a
  contradiction of this sentence.) This change makes the environment name a security input for the
  first time. The alternative the issue offers — move the 55 occurrences in 51 test files and the CI
  value to a different throwaway key and refuse everywhere but Development — keeps any test-only name
  out of production code, at the cost of a larger diff. The task chose the allow-list of two; the
  reviewer is asked whether `Testing` in that list is acceptable.
  (Counts: `git grep -n "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=" 7e75b67 -- 'backend/tests/*.cs' | wc -l`
  → 55, and `/usr/bin/grep -rlo` of the same value over `backend/tests --include=*.cs` → 51 files;
  the working-tree count is also 55, so the branch has not touched the tests' copies.)
- **The environment name is matched without regard to case, and it has more than one source.**
  `IHostEnvironment.IsEnvironment` and `IsDevelopment` compare with `OrdinalIgnoreCase`, so
  `testing` and `DEVELOPMENT` are admitted exactly as `Testing` and `Development` are. And the name
  is read from `ASPNETCORE_ENVIRONMENT`, from `DOTNET_ENVIRONMENT`, or from an `--environment`
  argument. The installer sets none of them:
  `git grep -nE "ASPNETCORE_ENVIRONMENT|DOTNET_ENVIRONMENT" 7e75b67 -- installer` prints nothing
  (positive control: the same command over `scripts/lib` prints 4 lines, the development launches),
  and `installer/systemd/maran-api.service:36` passes no argument. So every way to reach the
  allowance on a server is an edit to an operator-owned file, which is the argument of the next
  item; the reviewer is asked to accept that reading for the case-insensitive and the `DOTNET_`
  spellings too, or to require an exact-case match.
- **The environment name is trusted exactly as far as the keys are.** It comes from the same
  `root:maran 0640` `panel.env` (or a systemd drop-in, which is root's), so anyone who can set it to
  Development can also replace the key; the allowance grants an attacker nothing they did not have.
  What it does grant is an operator's escape hatch, and setting Development on a server also layers
  `appsettings.Development.json` (Debug logging, a one-second agent probe timeout) over the product
  and turns on the container's build-time validation. That is a visible choice in one file, not a
  silent one. **And it does not survive an installer re-run:** `write_config` regenerates
  `panel.env` from scratch and carries forward only what `existing_value` reads back, which is the two
  keys (`/usr/bin/grep -n "existing_value " installer/lib/60-config.sh` prints `:705` and `:717`
  only) — so a hand-added `ASPNETCORE_ENVIRONMENT=Development` is dropped on the next
  upgrade, while a hand-seeded key is kept. A host that used the hatch to keep running on a refused
  key stops again at its next re-run. That is the right direction to fail in, and the operator should
  hear it from the refusal message, not discover it.
- **Comparison is on decoded bytes, never on the base64 text.** `Convert.FromBase64String` ignores
  whitespace, so a text comparison would let ` MDEy…=` or a line-wrapped copy of the sample through.
- **The same key means containment, not only equality.** A signing key (at least 32 bytes) that holds
  the 32-byte encryption key anywhere inside it gives its holder the encryption key, whatever the
  offset. The check stops at a contiguous byte copy: a reversed, re-encoded or hex-spelled copy is not
  detected, and saying so is the limit, not a promise. The chance that a random 64-byte key contains a
  given random 32-byte sequence is about 33 × 2⁻²⁵⁶ — no false positive to weigh.
- **The low-entropy floor must have a false-positive rate that is zero for practical purposes**,
  because a false positive takes down a correctly installed server whose encryption key cannot be
  replaced without loss. The arithmetic for 32 bytes from a uniform generator, computed for this
  note and recomputed at the second re-check (exact Stirling-number sum for the distinct-count rows):

  | criterion (decoded 32 bytes) | probability a genuine random key trips it |
  |---|---|
  | every byte printable ASCII (0x20–0x7e) | 1.7e-14 |
  | every byte a hex digit character (`0-9a-fA-F`, 22 values) | 7.8e-35 |
  | at most 16 distinct byte values | 2.2e-15 |
  | at most 20 distinct byte values | 3.6e-9 |
  | at most 24 distinct byte values | 1.3e-4 — about one install in 7 800; **unacceptable** |

  "Every byte printable ASCII" catches the sample itself (it has exactly 16 distinct values, so the
  distinct-count floor catches it too, at its boundary — the explicit published-key comparison is
  what does not depend on that), and it catches any key that is text: a passphrase, a hex string
  base64-encoded a second time. That last case is refused on purpose even when the hex was random
  (128 bits in a 256-bit slot): the panel cannot tell it from a typed passphrase, and the remedy costs
  one command. A longer signing key only lowers both probabilities.
- **Refuse, not warn.** The issue offers a startup warning as a half-measure and says itself why it
  does not close anything: nobody reads the journal of a panel that works.

## What the change does not close

- **A key that is random-looking and nonetheless public** — copied from another server, a blog, a
  template written from someone else's install — passes every check. The floor detects structure,
  not secrecy; a single sample cannot have its entropy measured, and a key from a broken generator
  or derived from public facts looks random.
- **A host that already ran on the sample key is already compromised**, and refusing to boot does
  not un-leak anything. Replacing the signing key invalidates every forged token at once, whatever
  `exp` the forger chose. The three encrypted secrets must be treated as exposed: TOTP re-enrolment,
  a new SMTP password at the provider, a new ACME account.
- **The recovery path has a data-loss step the product does not help with.** There is no
  re-encryption tool (`/usr/bin/grep -rn -iE "re-?encrypt" backend/src installer scripts` finds only
  the two warnings at `60-config.sh:590` and `panel.env.example:12`). Read from the code, not run:
  `AesGcmEncryptionService.Decrypt` lets AES-GCM's tag check throw, and the converter does not catch
  it, so after the encryption key is replaced any query that materializes a user with a stored
  `TotpSecret`, the ACME account or the SMTP settings throws. A user with two-factor enrolled may be
  unable to sign in at all. This change does not add a recovery path; **the owner must rule** on
  whether that is acceptable for the hand-seeded hosts this refusal will stop, or whether a
  documented procedure (or a tool) is a precondition of shipping it. The refusal is still the right
  default: the alternative is a panel whose every stored secret is readable by the public.
- **Where the refusal surfaces on an installer re-run** is not the service start alone. The
  installer's schema step runs `Maran.Host --migrate` with `panel.env` exported
  (`installer/lib/65-schema.sh:58`, `set -a` over the file), which never starts the host, so
  `ValidateOnStart` does not fire there (`Program.cs:67-71` returns before `app.Run()`); but building
  a module `DbContext` resolves `IEncryptionService` — `IdentityDbContext`, `NotificationsDbContext`
  and `SslDbContext` take it in their constructors, and `--migrate` resolves every registered context
  — which reads `IOptions<SecurityOptions>.Value`, and the options factory runs every registered
  validator on that read. So a host carrying a refused encryption key is expected to stop at step 65,
  with the message printed into the installer's output, which `install.sh:244` tees into
  `/var/log/maran/install.log`. That is one more reason the message must carry no byte of the key.
  The signing key is probably not read during `--migrate` and would be refused at service start
  instead, together with the equal-keys check, which hangs off the `JwtOptions` registration. Both
  are reasoned, not measured.
- **An adjacent published secret, outside this issue: the development setup token.** `.env.example`
  also publishes `Setup__Token=dev-setup-token`, and a setup token is permission to create the first
  administrator (`rules/security.md` item 8 names it as a secret). `SetupOptions` has no validation
  callback at all (`ConfigurationExtensions.cs`, the `AddOptions<SetupOptions>()` registration, binds
  only). Its precondition is narrower than the keys': the installer generates a fresh token on
  **every** run (`60-config.sh:725-727`) rather than preserving it, so the sample reaches a server
  only through a `panel.env` the installer never touched, and it stops working once the first user
  exists. On such a host the key refusal above already stops the boot when the keys were copied from
  the same file, but a host given the sample token beside random keys is not refused. This branch does
  **not** absorb it — the parts and their contract are about the two keys — and the agent session
  opens no issue; the owner rules on whether it becomes one.

## What a second reviewer must check

1. **The refusal is red today and green after.** The Production + sample-keys test fails against
   `7e75b67` (run it there, or read the branch's own evidence of having done so), and passes on the
   branch.
2. **The inverse controls exist and pass**: a Production host with two independent random keys
   boots; a Testing host with the sample keys boots. Without them a guard that refuses everything is
   indistinguishable from a correct one.
3. **The allow-list really is an allow-list**: an unset environment, `Staging` and a misspelt name
   are refused, not only `Production`. Ask for one test with an environment name that is neither.
4. **No byte of a key reaches a message, a log line or an exception.** Read every string the policy
   and the callbacks build. Then start a refused host and search its output for the key and for its
   decoded text; the search owes a positive control (plant the value in a line the search must find).
5. **The same-key check runs at start**, in both orders of key assignment, compares decoded bytes,
   and refuses a signing key that contains the encryption key at a non-zero offset — the case the
   in-flight prefix check missed (see "The contract between the parts").
6. **The thresholds match the table above**, and none is looser than 16 distinct values or broader
   than "every byte printable ASCII". A threshold change after review is a change to this note.
7. **The policy type's folder** against `rules/csharp.md` (questions 1 and 2, in order — `Security/`
   vs `Utilities/`), that it carries no environment logic and has no `Interfaces/` contract, and that
   it is one type, used by every callback, with no second copy of the published value in production
   code: `/usr/bin/grep -rn "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=" backend/src --include=*.cs`
   must print at most the one line that defines it (positive control: the same command over
   `backend/tests` prints 55 lines today).
8. **The remedy text per key is accurate** — above all that the encryption-key message does not tell
   an operator to "just generate a new key" without saying what that costs.
9. **The recovery-path ruling** in "What the change does not close" has been made by the owner, not
   assumed.
10. **`.github/workflows/cross.yml` and `scripts/lib/e2e-handshake.sh` are unchanged** and the
    handshake still prints `HANDSHAKE VERDICT: OK`; the Development launch is what keeps it working.
11. **The same-key check compares against the key the panel actually uses.** It reads
    `IConfiguration["Security:EncryptionKey"]`; `AesGcmEncryptionService` is built from
    `IOptions<SecurityOptions>` (`SecurityExtensions.cs:32-35`), bound from the same configuration's
    `Security` section. They agree only while nothing post-configures `SecurityOptions`:
    `/usr/bin/grep -rnE "(Post)?Configure<" backend/src --include=*.cs` prints one line today,
    `ForwardedHeadersExtensions.cs:56` (`Configure<ForwardedHeadersOptions>` — the positive control
    that the pattern hits), and nothing for `SecurityOptions`. Ask for the same-key test to set the
    keys the way the other tests do (`UseSetting`), and confirm the refusal fires.
12. **The bearer handler cannot use a key the validation never saw.** `AddPanelAuthentication`
    reads `Jwt:SigningKey` straight from configuration at composition time
    (`AuthenticationExtensions.cs:22`), not through `IOptions<JwtOptions>`, so the protection rests on
    the host refusing to start before anything is served. Confirm the refused host never answers a
    request (no `/health`, no listening socket) — reasoned from `ValidateOnStart` running when the
    host starts, before Kestrel's hosted service, and not measured here.
13. **The case-insensitive and multi-source environment name** (see "Decisions") has been ruled on:
    either accepted as stated, or the match made exact-case.
14. **The sample files say what the code does**, no more and no less: `.env.example` (and, if
    touched, `installer/panel.env.example` and `docker/.env.example`) against the contract's
    "samples-docs describes exactly this behaviour", and no `KEY=` line added to either example file
    under `installer/` or `docker/`.
15. **The adjacent setup-token item** in "What the change does not close" has reached the owner, and
    the diff did not quietly widen to cover it: a setup-token check on this branch is a change to the
    plan and the contract, so it would first be a change to this note.

## What the author could not verify

- **No second reviewer.** This is an agent session; the requirement above stands as OUTSTANDING.
- **Nothing on a real installed host.** Whether the refusal text actually appears in
  `journalctl -u maran-api` under the unit's `Restart=on-failure` with `StartLimitBurst=5`, and how an
  operator experiences five failed starts and a unit marked failed, needs systemd; no polygon image
  here boots one.
- **The `--migrate` behaviour** described above is read from the options factory's documented
  behaviour and the registration in `SecurityExtensions.cs:32-35`, not run.
- **Two framework behaviours the plan leans on are read, not run**: that `IsEnvironment` and
  `IsDevelopment` ignore case and that `DOTNET_ENVIRONMENT` and `--environment` also set the name
  (the documented behaviour of `Microsoft.Extensions.Hosting`), and that `ValidateOnStart` fails the
  start before Kestrel binds. Reviewer checks 12 and 13 exist because of this.
- **Whether a `Production` test host boots at all with good keys.** No panel code reads the
  environment, but framework defaults differ from `Testing`; the inverse control is the measurement,
  and it has not been run (this stage runs nothing).
- **What the panel does with each encrypted column after a key replacement** is read from
  `AesGcmEncryptionService` and `EncryptedStringConverter`, not run: whether a user with a stale
  `TotpSecret` is locked out entirely, or only some screens fail, is unmeasured.
- **How many real hosts carry the sample key.** Unknowable from the repository; the refusal is sized
  for the hand-seeded case the issue describes, and the installer never produces it.
- **Whether `0123456789abcdef…` is the only development key ever published.** This tree publishes one.
  The census for quoted 32-byte base64 values at the branch base,
  `git grep -hoE '"[A-Za-z0-9+/]{43}="' 7e75b67 -- backend docs scripts .github | sort | uniq -c`,
  prints that one value 55 times and nothing else (it is its own positive control: the pattern hits).
  Over the working tree,
  `/usr/bin/grep -rhoE '"[A-Za-z0-9+/]{43}="' backend docs scripts .github --exclude='*sample-keys*' | sort | uniq -c`
  prints 56 at the second re-check, the one addition being the in-flight policy type's own
  definition (`SecretKeyPolicy.cs:56`); the exclusion is this note, which quotes the value inside its
  own commands. The unquoted occurrences are `.env.example:23`, `:27` and `cross.yml:113` at the base
  (`git grep -n "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=" 7e75b67 -- . ':!backend/tests'` prints
  exactly those three). An older revision, a fork or a tutorial may have published another key; the
  list is a list of what is known, not of what exists.

## Independent agent review (recorded before the human second review)

Five lenses reviewed the code: correctness, security, rules, compatibility, design. All five
approved. No blocking and no major finding was raised by any lens. Only minor findings exist; the
decision on each is below. This section is the agent review only: it does not replace the human
second review, which stays OUTSTANDING.

### Security-lens checklist (`rules/security.md`)

- Items 3 (shell), 7 (configuration documented, no real secrets committed), 8 (secrets never in
  logs or messages), 9 (crypto), 10 (surface), 11 (dependencies): yes.
- Items 1, 2, 4, 5, 6, 12, 13 (input, paths, config-file injection, SQL, authorization, agent rpc,
  module reach): not applicable, none touched.
- Escalation: this note exists as a file, the second review is OUTSTANDING, the plan matches the
  code (after the stale paragraph above was corrected).
- Hole hunt (allow-list, bypass, boot ordering, error leakage): nothing found. Development and
  Testing form an allow-list; an unset or misspelt environment is refused; decoding is on bytes, so
  whitespace cannot sneak the sample through; the two key validators are independent of order; no
  refusal message echoes a key byte.

### Findings and decisions

No blocking or major findings. Minor findings:

- Stale "in-flight code" paragraph in this note (security): fixed here.
- Installer step 65 prints a generic "migrations failed" when the encryption-key refusal throws in
  `MigrateAsync`, because `--migrate` exits before `ValidateOnStart` runs (correctness, security,
  compatibility, design): deferred as a follow-up. It fails closed and the key is never used in
  Production; the refusal text is in the journal. No installer edit in this branch.
- Installer comment echo (`installer/lib/60-config.sh`) still says "signs everyone out", which is
  false, and `write_config` drops a hand-added `ASPNETCORE_ENVIRONMENT=Development` line on re-run
  (correctness, rules, compatibility, design): outside this branch's parts; deferred to a follow-up,
  and the lost exemption is to be mentioned in the upgrade notes.
- CHANGELOG announcement of the startup behaviour change (compatibility): added in this branch
  (CHANGELOG.md, Unreleased). Affected are
  only hand-seeded installs; installer-generated keys (`openssl rand -base64 32`, separate per key)
  pass the policy. Remedy: replace the signing key at no data cost; replacing the encryption key
  invalidates values encrypted under it.
- Tests absent in Phase A (correctness, security, rules, design): accepted, per the testing order;
  written afterwards in this branch (SecretKeyPolicyTests, SecretKeyPolicySampleFilesTests,
  DevelopmentKeyRefusalTests, ConfigurationExtensionsTests).
  Phase B must cover: Production with the sample refused; Staging and unset refused; Testing and
  Development admitted; two random keys boot; signing key equal to or containing the encryption key
  refused; containment at a non-zero offset; symmetry; null or malformed input; whitespace-padded
  sample refused.
- Unverified compile items, including the `Validate<IHostEnvironment, IConfiguration>` overload, the
  span `IndexOf` calls and the `HostEnvironmentEnvExtensions.IsDevelopment` cref under
  warnings-as-errors (correctness, security, rules): Phase B build decides; if the cref fails it is
  replaced with plain text.
- Static `SecretKeyPolicy` versus the rule text saying `Security/` implementations are DI-registered
  (rules, design): code kept; the rule wording is stale and any change needs owner approval, so it is
  only flagged here.
- Duplicate checks and message constants for `SecurityOptions` and `JwtOptions` (design): accepted
  as is; extract a single-purpose type if it grows.
- Diff noise against `dev` (compatibility): the orchestrator confirms the branch is rebased before
  merge.

---

## Second review

**OUTSTANDING.** Reviewer, date and verdict to be recorded here by a human. Until they are, this
branch does not merge to `main`.
