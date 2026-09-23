# Threat note — what the Identity module releases when a hosting account is deleted

**Surface:** panel logins, refresh sessions, password-reset tokens and two-factor recovery codes —
`backend/src/Maran.Modules/Identity/IntegrationEvents/Handlers/AccountDeletingHandler.cs`, reached
from the account-deletion cascade. This is session and token handling, so rules/security.md
"Sensitive change escalation" applies.

**Status: written BEFORE this session's change.** No file under
`backend/src/Maran.Modules/Identity/` or `backend/tests/Maran.Modules.Identity.Tests/` had been
edited when this file was created. The change this note precedes is the proof pass over the handler
— the retention, idempotency and failure-path tests described under "What a reviewer must check".

**It is also, deliberately, a late note for an earlier change.** The handler itself is already in
the tree (`b17b900`) and landed with no note in `docs/superpowers/notes/`, which is the gap this
file closes. A note written after the code cannot contradict the choices it describes, so the
reasoning below is written forward from the question — *what does this module hold that an account's
deletion must take away, and what must it refuse to take away* — and each answer is stated against
the mapping rather than against the code that happens to be there.

**Second reviewer: OUTSTANDING.** No reviewer can be dispatched from this session. Per
rules/security.md this work may live on a branch and MUST NOT merge to `main` until a second
reviewer has read it.

## The contract this handler sits inside

`AccountDeleting` (`backend/src/Maran.Sdk/Events/AccountDeleting.cs`) is **invoked inline** by
`DeleteAccountCommandHandler`, not published. A subscriber that throws **aborts the deletion** and
the account is left exactly as it was. That is a veto point, not an announcement, and it is what
makes "propagate the failure" the correct behaviour here rather than a rough edge: the recoverable
state is an account that still exists, and the unrecoverable one is a panel that has forgotten an
account whose credentials still authenticate.

The Backups module holds the same veto and uses it in the same direction — a failed final backup
refuses the deletion — so Identity is not claiming a power the cascade does not already grant.

## Ordering, and what it is guaranteed by rather than hoped for

Six modules subscribe to the same event. The order they run in is **not** a thing this handler can
ask for, and it is not a thing it needs: nothing else in the cascade reads Identity's tables, and
Identity reads nobody else's, so the handler is order-independent by construction. That is the
guarantee — an absence of coupling — not a scheduling promise. The one ordering that *is* promised
is the one `DeleteAccountCommandHandler` states in its own doc comment and holds by writing the
steps out in sequence: the final backup is taken before the cascade, the cascade before the residue
audit, the audit before the agent touches the host, and the `Account` row last.

**The honest limit, stated because it is security-relevant.** Each subscriber commits its own
`SaveChangesAsync` against its own module's context; there is no transaction spanning the six. So a
subscriber that refuses *after* Identity has run leaves the account intact with its logins already
gone. This module sits on the safe side of that asymmetry in both directions: the failure mode it
produces is *access removed too early* — an operator whose tenant cannot sign in until the deletion
is retried, which the panel's own error tells them to do — and never *access left behind*, which is
the only outcome that cannot be repaired by trying again. Composed as the panel builds it today,
Identity is in fact the first of the six
(`backend/src/Maran.Host/Internal/Generated/WolverineHandlers/AccountDeletingHandler332383031.cs`),
so the window is the widest it can be, and the argument above is written to hold at that worst case
rather than at a position no code guarantees.

## What this module holds against an account, and what happens to each

| Row | Keyed by | Decision | Why |
|---|---|---|---|
| `User` | `AccountId` (nullable) | **Delete** | A login that outlives its tenant is a username and password that still authenticate against the panel. |
| `Session` | `UserId` | **Delete** | The refresh token is the credential that works *without* the password; left behind, the session keeps renewing to its own expiry. |
| `PasswordResetToken` | `UserId` | **Delete** | A live permission to set a password. Worth more to an attacker than the row it hangs off, and it survives the account name being re-created. |
| `RecoveryCode` | `UserId` | **Delete** | Two-factor bypass material; it is only as good as the login it unlocks, but it must not outlive it. |
| `AuditEvent` | `ActorUserId` (a reference, not ownership) | **Retain** | Append-only journal. Erasing an account's history is the opposite of what an audit trail is for, and a deletion is precisely the event that makes someone want to read it. |
| `SecurityPolicy` | a constant singleton id | **Retain** | Server-wide, owned by no account. |
| `FailedLoginByIp` | an IP **address** | **Retain** | Counting state for brute-force lockout. Clearing it because a tenant left would hand an attacker a way to reset their own lockout by having an account deleted. |

There is no "retain but detach" row: `AuditEvent.ActorUserId` is deliberately left pointing at an id
whose row is gone, because the id is the operator's record of *who* did the thing, and nulling it
would destroy the only link between two entries made by the same login.

## What an attacker could do with this surface, and why it is safe

1. **Leave a credential behind by making the handler miss a table.** The residue auditor that gates
   the deletion counts rows carrying an `AccountId`, and three of the four deleted tables carry a
   `UserId` instead — so a handler that removed `User` rows alone would be pronounced clean by the
   audit while leaving live refresh tokens. Nothing but this module's own tests looks at those three
   tables. **A reviewer must check that each of the three has an assertion of observed absence**, and
   that the assertions are not satisfied by an emptied table (the stranger-tenant control).
2. **Delete more than the account owns.** The inverse risk, and the more likely bug: a handler that
   emptied `Sessions` would sign every other tenant out and would look exactly like a correct
   cascade. `AccountId` is nullable and the administrator's is `null`, which in SQL is neither equal
   nor unequal to the account being deleted — so a null-handling mistake locks the operator out of
   their own panel on the first customer deletion. Covered by the stranger and administrator tests.
3. **Half-delete.** Everything leaves in a single `SaveChangesAsync`, dependents staged before
   principals, so a save that fails part-way cannot leave a token pointing at a user that is gone.
   The transaction is the guarantee; the ordering is the belt.
4. **Replay.** Wolverine can deliver the same message twice. The second delivery matches no user and
   returns before touching anything — it neither throws nor removes more.
5. **Swallowed failure.** Nothing here catches. A failure propagates to the Accounts handler, which
   abandons the deletion with the account intact and logs the reason for the operator while the
   customer-facing message stays free of machine text (rules/security.md item 8).
6. **No query filter is bypassed.** `User` carries no global tenant filter — the module exposes no
   endpoint that lists or fetches users — so there is no `IgnoreQueryFilters` here to justify.
7. **No new surface.** No port, no daemon, no outbound call, no shell string, no agent capability:
   this handler touches its own module's schema and nothing else.

## What the author could not verify

- **No production path creates a customer login.** Measured on this tree: the only `new User(...)`
  in `backend/src` is `CompleteSetupCommandHandler`, which writes `UserRole.Admin` with a null
  `AccountId`; `UserRole.Customer` appears in `backend/src` only inside a doc comment. So the state
  this handler exists for cannot be reached by the running panel today, and every test that proves
  it works must construct that state directly. The handler is therefore correct against the schema
  and unexercised by the product — which is the reason to fix it now, while a deletion failing is
  the only consequence, rather than on the day customer logins ship.
- **Real PostgreSQL cascade behaviour is asserted elsewhere.** This module's unit tests run on the
  EF Core InMemory provider, where a foreign key is a suggestion; the `DeleteBehavior.Cascade`
  mapping that would remove the dependents even if this handler named none of them is guarded by
  `AccountCascadeTests`, not here.
- **Which half of the session removal actually holds, measured rather than argued.** The handler's
  three explicit `RemoveRange` calls and the `DeleteBehavior.Cascade` mapping mask each other, so
  each was mutated alone and then both together, every mutant scored against the whole
  eighteen-project solution. Neutering the handler's session removal alone SURVIVES — the mapping
  covers it, on the in-memory provider too, because EF Core's change tracker cascades whatever the
  store does. Breaking the mapping alone is killed by
  `AccountCascadeTests.Every_dependent_of_a_tenant_row_is_removed_with_it`, and leaves Identity's own
  suite green, which is the provider-independence the handler claims. Breaking BOTH is killed by
  Identity's own tests, by name:
  `Deleting_an_account_removes_the_refresh_sessions_of_the_logins_it_owns` and
  `Delivering_the_same_deletion_twice_leaves_the_second_delivery_nothing_to_do`. So neither half is
  decoration, and a reviewer changing either one should expect a named test to say so.
- **The failure path is asserted as "the exception escapes", not as "the store is unchanged."**
  InMemory has no transaction to observe, so a test claiming rollback there would be reporting on
  the provider rather than on the guarantee. UNOBSERVED HERE: the atomicity of the single save under
  PostgreSQL.

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
