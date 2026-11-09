# Provisioning API: writing accounts by API key (issue #7) — threat note

Status: **Second review: OUTSTANDING** (rules/security.md "Sensitive change escalation"; tracked as #331).
This change lets a long-lived bearer secret CREATE, SUSPEND, CHANGE and DELETE hosting accounts: it is
authentication, token handling and a tenant boundary, and it is the first time a key can destroy data. The
branch may land on a branch and **MUST NOT merge to `main`** until a second reviewer has read this note and
checked every item under "What a reviewer must check". Written BEFORE the code, so it can contradict a
choice instead of validating one.

## What is being added

Reseller-owned API keys (issue #368) can hold two more scopes.

- `accounts.write` reaches four operations on the reseller's OWN accounts: create, suspend, reactivate and
  change plan. Each is recoverable: suspend is undone by reactivate, a plan change by another plan change,
  and a created account by termination.
- `accounts.delete` reaches one: terminate. It is a scope of its own because it is the one operation that
  cannot be undone.
- Three read operations join the existing `accounts.read`: one account, its usage and the provisioning info.

All routes sit under `api/v1/reseller-api/`. Administrator-owned keys gain nothing: both scopes are
reseller-grade, so they stay `self.read` and `server.read`. No endpoint is added to the JSON-session surface;
the handlers are the ones the panel's own screens already use.

This note covers the whole change. Task B1 (this commit's first part) lands only the AUTH SURFACE: the two
scopes, the two policies that admit them, and the `provisioning` rate-limit bucket. No endpoint uses them
yet; B2 and B3 add the controllers. The argument below is the one the finished change must satisfy.

## What changes about the threat model

After #368 a stolen reseller key equals a stolen reseller password plus second factor for READ access to two
lists. After this change the same theft equals the ability to create accounts up to the reseller's envelope
(cost to the operator: disk, a Linux user, a login invitation mailed to an address the thief chooses), to
suspend customers (availability), to move customers between plans and, with `accounts.delete`, to delete
customers' data. The mitigation is not a new control but a split of what a scope can do plus the existing
controls, argued one by one below.

## The argument, by concern

1. **Reach is the reseller's own rows, decided below the controller.** The caller's reseller id for a key is
   the key's `ResellerId`, the same value `AccountsDbContext` filters by. An account of another reseller
   answers 404 (`AccountNotFound`), never 403. An administrator-owned key has no reseller id and sees and
   writes nothing. The owner of a created account is established from the key, never from the body.
2. **Two write scopes, because delete is not like suspend.** A billing integration that only provisions never
   needs `accounts.delete`, so the key a provider is told to create cannot destroy data by default. A key
   holding only `accounts.write` is refused at the policy for DELETE, which discloses nothing about a tenant.
3. **Both scopes are reseller-grade, with two locks.** `ApiKeyScopes.ResellerGrantable` lists them and
   `AdministratorGrantable` does not, so an administrator-owned key cannot be created with them and
   `ApiKeyScopePolicy.EffectiveOf` drops them from a row that somehow holds them. The policies
   (`ApiKeyAccountsWrite`, `ApiKeyAccountsDelete`) additionally require the reseller claim, so a scope claim
   without an owning reseller opens nothing. The scopes are NOT implied by `accounts.read` and neither
   implies the other: delete does not include write, and write does not include delete.
4. **Terminate always takes the final backup, and a key cannot skip it.** The key controller (B2) builds the
   delete command from the route id alone; the handler's own rule (a reseller-side caller may not skip the
   final backup) must now cover a key. Deletion is not confirmed by a second factor; the compensating controls
   are the backup and the scope split.
5. **A suspended or deleted reseller stops its keys** (unchanged from #368): the authenticator denies on the
   next call, and the handler's active-reseller check is the second floor for writes (B2).
6. **Replay and retry safety.** Every mutation carries `[Idempotent]` scoped to the key: a retried create
   returns the stored result and creates nothing twice, and the same header with a different body is 422.
7. **Blast-radius ceilings.** The reseller envelope already bounds how many accounts, how much disk and how
   many of each resource a reseller can allocate; a key cannot exceed what the reseller's login can. A
   dedicated `provisioning` rate-limit bucket bounds writes per key (60 per minute by default, the owner may
   change it), independently of the `api` bucket, so a noisy reader cannot spend the writer's budget and a
   writer cannot starve reads. The bucket is partitioned by the key id (`RateLimitPartitionKey` puts the key
   first), so two integrations behind one address do not share it; an unauthenticated request never reaches
   the bucket as a key and is refused by the policy first. The existing per-address failure limiter still
   covers guessing.
8. **Audit.** Every key call is already journalled once by the API key audit filter (key id, method, route
   template, status, address). The domain entry the Accounts journal writes (B2) gains a key arm: actor = the
   key, with the owning reseller recorded, never the secret.
9. **The invitation e-mail.** Creating an account mails an invitation to the owner e-mail in the body. A thief
   with `accounts.write` can make the panel send mail to an arbitrary address, bounded by the envelope, the
   `provisioning` bucket and the invitation sending limiter. Residual, stated below.
10. **View-as.** All new controllers refuse view-as; a key cannot start one.
11. **No new surface.** No new port, daemon, outbound call or agent rpc (rules/security.md item 10); no schema
    change, since a scope is a stored string on the existing key row.

## What a reviewer must check

1. `ApiKeyScopes`: `AccountsWrite` and `AccountsDelete` are in `All` and `ResellerGrantable` and in NEITHER
   `AdministratorGrantable` nor implied by another scope.
2. `ApiKeyPolicies`: both policies use `KeyOwner.Reseller` (scheme, key id claim, scope claim AND reseller
   claim). Run `grep -n "AddScopePolicy" backend/src/Maran.Host/Authorization/ApiKeyPolicies.cs` and read every
   arm; control: the same grep must list the older policies too.
3. `ProvisioningRateLimitPolicy`: fixed window, queue zero, partition from `RateLimitPartitionKey` (key id
   first); `RateLimitOptions` bounds (`ProvisioningPermitLimit`, `ProvisioningWindowSeconds`) reject zero.
4. The five mutating actions (B2): each carries the right policy at class level, `[Idempotent]`, refused
   view-as and `[EnableRateLimiting(RateLimitPolicies.Provisioning)]`; DELETE builds the command without a
   way to skip the final backup.
5. Cross-reseller IDOR: reseller A's key naming B's account id answers 404 on every route.
6. The OpenAPI diff is additive only.
7. Mutation evidence (rules/testing.md harness): policy scope check, reseller arm of a policy, scope set
   membership, partition key; a named test dies for each.

## What the author could not verify

No test, build or integration run was possible in the authoring session (the lead runs all gates once, after
every coder has finished). That the invitation mail cannot be turned into an amplification beyond the stated
limiters is argued, not measured. Behaviour of a key held during a reseller's suspension-resume window is
#368's open decision.

## Accepted residual

A stolen `accounts.write` key can fill the envelope with accounts and mail invitations to chosen addresses
until revoked; a stolen `accounts.delete` key can delete the reseller's accounts (each leaving a final backup)
until revoked. Bounded by envelope, rate limits, address binding, expiry and fast revocation; not eliminated.

## Owner decisions this note raises

Who may provision (reseller-owned keys only, as recommended), whether `Idempotency-Key` is required on the
mutations, whether terminate needs a confirming field, and the default rate (60 per minute per key).
