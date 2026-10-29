# Threat note: the administrator can be handed an invitation link (issue #78)

`rules/security.md` requires a threat note, written first, for a change to authentication and token
handling, and a second reviewer. This change adds a way for an administrator to obtain a usable
invitation link from the panel. That is a credential leaving the system through a new door, so the
note exists to argue why the door is safe.

**The second reviewer requirement is OUTSTANDING.** The work was carried out by an agent session,
which cannot obtain one. This branch MUST NOT merge to `main` until a human reviewer has read it.

## The problem being solved

An account created on a default install could never be signed into. `AccountCreatedHandler` mints
the invitation token, stores only its SHA-256 digest, and hands the plaintext to the mail composer.
On a fresh install no mail is delivered (SMTP is unconfigured), so the plaintext was gone the moment
the handler returned and nothing could recover it. The administrator must never learn a customer's
password, so "let the administrator type one" was refused as a design; the link is what they hand
over instead.

## The decision: the plaintext is never stored, only re-minted on request

The tempting design is to keep a recoverable copy of the token (encrypted at rest) so the panel can
show the same link again and again while the invitation is unused. It is rejected, for the same
reason a durable mail queue was rejected for this token (`rules/csharp.md`, "a message carrying a
secret is never durable"): a recoverable copy turns "a dump of the table yields nothing usable"
into "a dump of the table plus the encryption key takes over every account that has not been
claimed yet", and the key lives on the same host.

Instead the panel gains `POST /api/v1/invitations/{accountId}/link`, which does exactly what
`ResendInvitation` already did to the token (retire every outstanding one, mint a new one, store its
digest) and returns the plaintext link ONCE, in that response, to an administrator. This mirrors the
panel's existing one-time credentials (a freshly minted FTPS or SFTP password is shown once and
stored nowhere, and a lost one is replaced, not recovered).

The consequence is stated to the operator rather than hidden: creating a link retires the one
already mailed, so the page says so before the button is pressed.

## What an attacker could do with this surface

- **Read a link as a customer, or as nobody.** The route sits on `InvitationsController`, which is
  `[Authorize(Policy = AdminOnly)]`. A customer is refused with 403 and an anonymous caller with
  401, and the refusal discloses nothing: it is decided before the handler runs, so a customer
  cannot tell whether an account exists. Asserted over real HTTP with an inverse control (the
  administrator receives a link), because a test that only refuses a customer also passes against
  an endpoint that refuses everybody.
- **Read the token out of the database.** Nothing new is stored. `InvitationToken.TokenHash` is
  still the only form that rests in a table. Asserted: the plaintext is not found in any stored
  invitation row, nor in any audit row.
- **Read it out of the audit journal or the logs.** The audit entry for issuing a link
  (`InvitationLinkIssued`) names the administrator, the account and the client, and carries no
  token and no link. The response record holds the link as a `SensitiveString`, so the one
  reflexive way a value ends up in a log (a record's generated `ToString()`, an interpolated
  message, a structured-logging argument) prints `[redacted]` instead. The handler logs nothing.
- **Read it from a cache.** The response is a credential, so the action carries
  `Cache-Control: no-store`; asserted on the wire. The route is a POST, which no shared cache stores,
  and there is no response-caching or idempotency layer in the host that would persist the body.
- **Read it from a message envelope.** The link travels as the return value of an in-process
  `InvokeAsync`, which is not an envelope and never touches the durable `wolverine` tables.
  Nothing is published to a queue by this command. The mail that `ResendInvitation` publishes is
  unchanged.
- **Keep using a dead link.** A link is only ever created on request and is never remembered by the
  server. The panel's read of an account's invitation (`GET /api/v1/invitations/{accountId}`)
  reports one of four states (`none`, `pending`, `expired`, `accepted`) computed from the stored
  token rows at the moment of the request, so once the invitation is accepted or has lapsed the page
  stops offering a link. The SPA holds a link in memory for as long as the operator stays on that
  account's page and drops it when the page is left or the account is reloaded.
- **Mint a token for a login that is already live.** The same refusal `ResendInvitation` has: a login
  that is not in the `Invited` state is refused (`InvitationLoginNotPending`, 409). Without it, this
  endpoint would be a password reset for any customer that bypasses the reset endpoint's own rate
  limit. Both commands now run through ONE type, `InvitationIssuer`, so the refusal cannot be
  fixed in one place and forgotten in the other.
- **Aim the link at the wrong host.** The absolute link is built from the configured
  `PanelOptions.PanelUrl`, never from the request's `Host` header (the host-header-injection
  argument the mail composer already makes). When no public address is configured the response
  carries a root-relative path and the SPA prefixes its own origin: the page the administrator is
  looking at.
- **Hammer the endpoint.** It sits under the `api` rate-limit policy like the rest of the controller,
  and every call is a journalled event: a granted link as `InvitationLinkIssued` naming the customer's
  login, a refused one (unknown account, login already claimed) as the same action with `Succeeded`
  false and the account id that was probed as its subject. Asserted over real HTTP and in the handler's
  unit tests.

## What a reviewer should check

1. That nothing in `Maran.Modules/Identity` reads or writes the plaintext token to a table, a log
   line, an audit entry or a Wolverine envelope. The plaintext leaves its `SensitiveString` in exactly
   two places, which are the two handlers that hand it on:
   `/usr/bin/grep -rn "Reveal()" backend/src/Maran.Modules/Identity --include=*.cs | /usr/bin/grep -v '///'`
   lists `ResendInvitationCommandHandler` (to the mail composer) and `IssueInvitationLinkCommandHandler`
   (to the link builder) and nothing else; a third line is a new door to argue for. (Positive control:
   the same command over `backend/src/Maran.SharedKernel/Security` lists `SensitiveString` and its
   JSON converter, so the pattern is capable of a hit. An earlier draft of this note named the composer
   and the builder, which call nothing of the sort — they receive an already-revealed string.)
2. That `InvitationsController` still carries `AdminOnly` at class level and that the new action did
   not add `[AllowAnonymous]` or a laxer policy.
3. That `Cache-Control: no-store` is on the link response.
4. That `ResendInvitation`'s behaviour is unchanged by its move into `InvitationIssuer`
   (the existing resend tests were kept and still pass).

## What the author could not verify

- **No second reviewer.** This is an agent session.
- **Browser clipboard behaviour** in locked-down configurations is handled by showing the link as
  selectable text beside the copy control, not by detecting the failure; it was exercised in
  Chromium only.
- **A reverse proxy that caches POST responses** is outside the repository. None is configured by
  the installer, and `no-store` is the instruction any such proxy is given.
