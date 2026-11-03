# Sub-users: threat note (issue #315)

**Second review: OUTSTANDING.** This changes who may act for a hosting account and adds a second,
per-person check in front of every plan-gated endpoint, so rules/security.md "Sensitive change
escalation" applies. It may land on a branch and MUST NOT merge to `main` until a second reviewer has
read it.

## What changes

- An account's owner can add people (`api/v1/me/sub-users`). Each is a login of their own with a
  `Member` row in `identity.AccountMemberships` carrying a set of `AccountFeature` grants.
- `RequiresAccountFeatureFilter` now asks two questions: does the PLAN include the feature
  (`IAccountFeatureDirectory`), and was the PERSON given it (`IAccountMemberGrants`, new, implemented by
  Identity). Both must say yes.
- Invitations reuse the existing one-time token: the owner never sees a password or a link.

## What an attacker could try, and why it fails

| Attempt | Why it fails |
|---|---|
| A member adds a teammate, widens their own grants, or removes someone | Every sub-user operation starts at `SubUserOwnerGuard`: the caller must hold an `Owner` membership of the account their token speaks for. A member and an administrator get 403 `SubUsersOwnerOnly`. |
| An owner names another account's sub-user (IDOR) | The account is never a parameter; it is the caller's own token. Membership lookups filter by it, so another account's person, the owner themselves, and an unknown id are all 404 `SubUserNotFound`. Covered by `SubUsersEndpointTests`. |
| A removed member keeps using a live access token | Gated endpoints read the membership on each call and `AccountMemberGrants` fails closed when there is no row, so removal refuses them at once. Sessions are deleted with the login (or revoked for this account), so the refresh token stops renewing. |
| A member uses a feature the owner withheld, with the plan including it | The grants window refuses with 403 `AccountFeatureNotGranted`, independently of the menu and the plan. |
| The invitation endpoint is used to mail strangers | The action carries the `invitation-sending` rate limit (per account; issue #341 split it from the acceptance bucket) and a per-account cap on sub-users. The mail goes through the existing Notifications path; nothing about the token is journalled, logged or stored beyond its digest. |
| Joining an existing person to the team without consent | Refused: a taken login name or address creates nothing and mails nobody, and is answered exactly as a free one (issue #341, see 2026-11-03-invitation-threat-note.md). Joining an existing login is a separate, consenting flow that does not exist yet. |
| Role or grants forged through the request | `IpAddress`, `UserAgent` and the route `UserId` are `[JsonIgnore][BindNever]` members stamped by the action; the account comes from the token. |

## What this does NOT cover (stated limits)

- An endpoint that is not plan-gated (sites, SSL, tasks, PHP) does not ask the grants question, so a
  member reaches those regardless of grants and a removed member's access token works there until it
  expires (its fifteen-minute lifetime). `AccountFeature` has no member for them. Adding one is a new
  `AccountFeature` bit plus one attribute line per controller.
- A suspended account: members are not suspended with the owner's login by this change.
- No resend of a lost invitation to a sub-user; the owner removes and re-invites.

## What the author could not verify

Nothing was built or run when this was written (the working tree is shared). The reviewer should
confirm: that `AccountMemberGrants` is the only reader of the grant bits (everything else goes
through `AccountMembership.Allows`); that no gated controller lacks the attribute; and that the
removal path leaves no session able to refresh.
