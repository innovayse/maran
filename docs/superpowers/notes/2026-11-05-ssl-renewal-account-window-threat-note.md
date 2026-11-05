# Unattended account-name window for certificate renewal (issue #379) — threat note

Status: **second reviewer OUTSTANDING** (rules/security.md "Sensitive change escalation"). This change
adds one new member to a cross-module Sdk window, `IAccountDirectory.FindSystemUserAsync`, which
answers an account's system user name WITHOUT the caller's tenant scope, and one new deliberate
bypass of the Accounts query filter. The system user name is the value that addresses every agent
operation, and the member is the first one in this window that answers a single account while
ignoring the principal. That touches the tenant boundary, so it needs a second reviewer, and no
agent session can dispatch one.

The branch `fix/379-ssl-renewal-principal` may land on its issue branch and on `dev` through a pull
request; it **MUST NOT merge to `main`** until a human reviewer has discharged this note, or the owner
has recorded the beta exception for a named pre-release in writing (rules/security.md "The one
recorded exception"). The owner is told at hand-off that the Accounts directory window carries an
outstanding review.

**Honest ordering.** This note is written BEFORE the code, as rules/security.md requires. Every "why
it is safe" sentence below is therefore a forward argument from the question "what does an attacker
who reaches this get?", and a reviewer should still re-derive it rather than accept it. Where this
note and the code disagree once the code lands, the code ships and the note is wrong; the reviewer
adds the difference under "Deviations found when the code landed".

This note closes item 2 of "What could still go wrong" in
`2026-11-04-accounts-fail-closed-threat-note.md`, which recorded this defect as known, open and out
of scope there.

## The failure that motivated it

`Ssl/Jobs/CertificateRenewalHandler.cs` (`RenewAsync`) reads the owning account with
`IAccountDirectory.FindAsync(certificate.AccountId, ...)` to learn the system user name the ACME
challenge file is written under. `FindAsync` applies the caller's tenant scope in
`AccountDirectory.Sees`: an administrator any account, a customer its own, every other caller none.
Certificate renewal is a scheduled message handler with no request behind it, so its caller is
`AnonymousCaller`, and `Sees` answers false before any row is read.

The result: every renewal answered `null`, and the handler recorded `AccountNotFound` against every
certificate, on every pass, for every account. Nothing expired yet, so nothing was visibly broken;
every certificate the panel issued would have gone on silently failing to renew until it lapsed.

It was **caught only because a stub directory ignored scope.** The unit tests for the handler use a
stub `IAccountDirectory` that answers by id and applies no caller check, so they stayed green on a
code path the real directory refuses. That is the exact blind spot rules/testing.md "A check must be
able to observe what it reports on" describes: the stub could not observe the thing that was broken.
The fix therefore needs a test that runs the REAL directory against a REAL context as the anonymous
caller (see "Tests the change requires").

`ISiteDirectory` met the same problem earlier and solved it with `FindByIdUnscopedAsync`: a window
member that takes an identity the caller already holds and ignores scope, named for that fact. The
account window has no equivalent, which is the gap.

## What changes

1. **A new record**, `Maran.Sdk/Contracts/AccountSystemUser.cs`:
   `AccountSystemUser(Guid Id, string Username)`. A positional record, no logic.
2. **A new Sdk member**, `IAccountDirectory.FindSystemUserAsync(Guid accountId, CancellationToken)`,
   returning `Task<AccountSystemUser?>`. Its doc comment states that it applies NO caller scope, that
   a request path must never reach it, and what it answers (see below).
3. **An implementation** in `Maran.Modules/Accounts/Services/AccountDirectory.cs`: one query on
   `Accounts`, `IgnoreQueryFilters()` under one `RS0030` suppression carrying its reason on the line,
   `AsNoTracking`, a `Where(account => account.Id == accountId)`, a projection to
   `AccountSystemUser`, and `SingleOrDefaultAsync`. No plan join, no other set. A new account id
   answers `null`; so does an unknown one, and the two are not distinguished.
4. **The renewal handler** calls `FindSystemUserAsync` instead of `FindAsync`, and uses
   `.Username` where it used `account.Username`.
5. **Bypass count 34 → 35.** `rules/README.md` "Mechanical enforcement" states the count and the
   command that regenerates it (`grep -rn "\.IgnoreQueryFilters()" backend/src --include=*.cs | wc -l`);
   the sentence is updated in the same change with a "Raised to 35 on 2026-11-05 for
   `Maran.Modules/Accounts/Services/AccountDirectory.cs` (issue #379)" entry, and the closing
   "was N at HEAD, is N in the working tree" clause is re-measured, not edited by arithmetic.

What does **not** change: `FindAsync`, `FindForViewAsAsync`, `NamesOfAsync`, `FindIdsByNameAsync`
and `ListAsync` keep their behaviour and their contracts. In particular `FindAsync` is **not widened
for customers or for anyone**: it still answers null for the anonymous caller, and this note's test
list holds it to that.

## Assets

1. **A tenant's system user name.** The name that addresses every agent operation. Learning another
   tenant's is the first step of targeting them, and the existing window refuses a customer that.
2. **The meaning of "not your account is null"** (rules/security.md item 6): an id that does not
   exist and an id that belongs to someone else must be indistinguishable. This member does not weaken
   that because it never answers "someone else's": it has no notion of an owner. It answers the same
   to every caller, so the existence of an account becomes observable to whoever can call it. That is
   the whole of the risk, and it is the next section.
3. **The fail-closed Accounts context** (issue #371): an anonymous caller sees no rows through the
   module's own queries. This change must not teach any other path to read through that wall.

## What an attacker with a request path gets if a handler misuses it

The question is not "can an outsider call this" — an HTTP request cannot call an Sdk interface
member directly — but "what does it cost if a handler that IS on a request path calls it with an id
a caller chose", because the Sdk is a surface paid modules also compile against and a future handler
can always be written wrongly.

- **What comes back.** For a guessed account id, a `Guid` and a username string, or `null`. A caller
  who can reach it with a chosen id learns whether the account exists (the null versus not-null
  answer) and, when it does, the system user name of that account. That is the enumeration the
  tenant filter exists to prevent, bought one id at a time. Account ids are random `Guid`s, so a
  blind guess is not a practical enumeration; the realistic misuse is a handler that hands over an id
  it was GIVEN (a path or body id, another tenant's resource) and returns the answer, which is an
  IDOR with a 200 instead of a 404.
- **What it does not give.** No plan, no limits, no owner email, no status, no reseller, no domain, no
  name of the account, nothing that is a write. It cannot list: the signature takes one id and
  returns at most one row, so there is no way to ask for "all". It changes nothing.
- **Mitigation 1: the contract says so, in the open.** The doc comment on the member states, in the
  manner of `ISiteDirectory.FindByIdUnscopedAsync`, that it must never be reached from a request
  path, that its one legitimate caller is unattended work that holds the id from its own row, and
  that the name says "unscoped" because that is the whole warning. A member that can do harm in a
  careless hand is named for the harm. The name `FindSystemUserAsync` states what comes back; the
  scope statement lives in the doc comment's first sentence and `<remarks>`, because naming it
  `...Unscoped` as the site member does would be the better precedent, and a reviewer should decide
  whether the name should carry it (see "What a reviewer must check", item 7).
- **Mitigation 2: no controller reaches it.** The window is an Sdk interface; no controller calls
  the directory directly (controllers dispatch commands and queries through the bus). The reach to
  check is therefore every handler that takes `IAccountDirectory` and calls this member. Callers are
  enumerated by search, not memory (command below), and a reviewer re-runs it.
- **Mitigation 3: the census.** A caller list that is only a document is stale within a release.
  The change adds a test that fails when anything other than the named unattended handler calls
  `FindSystemUserAsync` (see "Tests the change requires", item 4). If that test is not in the diff,
  mitigation 3 does not exist and this note must say so.
- **Residual.** None of these stop a future author who adds a second caller, reads the test failure,
  and edits the allow-list to make it pass. That is a review problem and not a code one; the test
  makes it a visible edit to a named list rather than a silent call.

Callers, regenerated by (run with `/usr/bin/grep`, not a shell wrapper — rules/testing.md "A SEARCH
that found nothing is a check"):

```
/usr/bin/grep -rnE "FindSystemUserAsync" backend/src --include=*.cs
/usr/bin/grep -rn "\.IgnoreQueryFilters()" backend/src --include=*.cs | wc -l
```

Positive control for the first command: it must print the declaration in
`Maran.Sdk/Interfaces/IAccountDirectory.cs`, the implementation in
`Maran.Modules/Accounts/Services/AccountDirectory.cs`, and exactly one call in
`Maran.Modules/Ssl/Jobs/CertificateRenewalHandler.cs`. A result with only the declaration means the
renewal change was not made; a result with a fourth file means a new caller, which is the finding.
The second command must print 35.

## Why `Username` only

The renewal handler needs exactly one thing from the account: the system user name it writes the
ACME challenge file under and installs the certificate material for. Every other `AccountSnapshot`
field is a fact the handler does not use. A window exposes the narrowest value that answers the
question — an address, not the settings record it was read from (rules/architecture.md "A shared
facility's contract lives in `Maran.Sdk`").

Specifically not returned, and why each would be a worse window:

- **The plan and its limits.** Allowances are what a tenant bought; an unscoped answer that carries
  them is the host-wide enumeration `ListAsync` already is, and `ListAsync` puts its whole
  authorization burden on the caller. Renewal needs none of it.
- **The owner email and language.** Personal data, and the one thing in `AccountSnapshot` that an
  unattended handler could be tempted to mail. A renewal failure that wants to tell the owner
  belongs to a notification path that is entitled to that fact, not to a window that hands it to any
  caller.
- **Status.** A suspended account's certificate is still renewed (the files are on disk and the
  certificate still serves); a status field invites the handler to start deciding, and a decision
  taken from a field the window was not designed to be authoritative about is a second place the
  suspension rule is stated.

Returning `Id` beside `Username` is deliberate and cheap: the id is the caller's own input echoed
back, so it discloses nothing, and it lets a caller that batches several lookups pair answers to
requests without relying on order.

## Why not reuse `ListAsync`

`ListAsync` already bypasses the filters and already serves unattended work, so it looks like the
free answer: the handler could list everything and pick its account in memory. It is rejected for
three reasons.

1. **It loads every account and every plan on the host for one id.** Renewal calls it once per due
   certificate, inside a loop; a server with thousands of accounts and a pass touching hundreds of
   certificates reads the whole table that many times, each materialising every snapshot including
   owner emails. Cost grows with the product of the two sets, to learn one string.
2. **It widens what the renewal handler holds.** A handler that has every account's snapshot in its
   memory has the plans, limits and owner emails of tenants it is not renewing for. The window's
   promise is to answer the question and hand over nothing else; `ListAsync` cannot keep that
   promise for a one-id question.
3. **It makes the caller's boundary the only control, for a call that does not need it.**
   `ListAsync`'s own doc comment says "nothing may call it without gating itself to administrators
   first, or running with no request at all". A new member that cannot enumerate is a strictly
   smaller power than the one it replaces for this caller, and giving a caller a smaller power is the
   direction this repository moves in.

## Why a window and not a wider `FindAsync`

`FindAsync`'s whole contract is that it answers nothing for an account the caller's tenant data may
not touch (a customer sees its own; an anonymous caller sees none). Teaching it that "the anonymous
caller sees everything" would turn the one scoped method into an unscoped one for the very caller
kind the fail-closed work (issue #371) set out to make see nothing, and every module that
relies on `FindAsync` to refuse an unauthenticated read would silently stop being protected. A
second member with its own name keeps `FindAsync`'s guarantee a single sentence long.

## What could still go wrong

1. **A second caller, on a request path.** The risk above. The census test and the grep are the
   guard; neither stops a deliberate edit.
2. **Anonymous-only is not enforced by the member.** It answers every caller, administrator and
   customer too, identically. That is intentional (a member that refused customers would need to
   know who the caller is, and then a scheduled run with a check against an absent principal would
   fail in the dark, which `AccountDirectory.ListAsync`'s remarks call a feature that stops working).
   It means the control is the doc contract plus the census test and not a runtime refusal.
3. **Renewal that depends on a username changing underneath it.** The handler reads the user name,
   then orders a certificate that can take minutes. If an account is deleted in that window the
   challenge write fails and is recorded as a failure, which is the existing behaviour for every
   other step. This note does not add a recheck.
4. **The bypass reads an account the owning module has soft-hidden.** The Accounts filter fails
   closed on caller kind, not on a deletion flag; if a later change adds a "deleted" or "pending"
   filter to `Account` the unscoped read would still see those rows. That would be correct for
   renewal today and wrong for some future caller, and it is another reason the member is named and
   documented for one purpose.
5. **The test that would have caught the original defect is not the one that exists today.** If the
   real-context integration test (below) is not in the diff, the next regression of this shape is
   caught only by luck.

## Tests the change requires

All tests carry the doc comments and behaviour-sentence names rules/testing.md requires.

1. **Real-context integration test, as the anonymous caller, with a positive control.**
   `FindSystemUserAsync` called on the REAL `AccountDirectory` over the REAL `AccountsDbContext`
   (Testcontainers PostgreSQL, the existing Accounts integration fixture), with `ICurrentUser.Caller`
   an `AnonymousCaller`, after seeding an account whose username is known. Asserts the call returns
   that username and that account's id. This is the positive control: it proves the window answers
   where the scoped window cannot, with a stub that could not have observed the difference replaced
   by the thing that does.
2. **Inverse control: `FindAsync` still answers null for the same anonymous caller, same account,
   same context.** In the same test class, so the two assertions read side by side: `FindSystemUserAsync`
   returns the row and `FindAsync` returns `null`. A test that only asserted the new member answers
   would pass if `FindAsync` had been widened by mistake; the inverse control is what proves it was
   not. It also guards the failure that motivated the change, because the pair together is what a
   renewal run sees.
3. **An unknown id answers `null`**, and **the answer for an id that exists is the same record
   shape for an administrator and for a customer of another account** (the member ignores scope for
   every caller kind, stated as a test so that "anonymous only" is not assumed).
4. **A census over callers.** In `backend/tests/Maran.ArchitectureTests`, a test that scans the
   composed panel's module assemblies for call sites of `IAccountDirectory.FindSystemUserAsync` and
   fails naming any type outside the allow-list (`Ssl.Jobs.CertificateRenewalHandler`). It carries a
   positive control (the allow-listed handler IS found, so the scan is capable of a hit) and a
   planted violation as its refusing control (a fixture type that calls it is reported), per the
   architecture tests' own convention. A scan that cannot see call sites through an interface
   dispatch only finds what reflection over IL can; the test states its blind spot in its output.
5. **The renewal handler against the real directory.** A handler-level test, with a real
   `AccountDirectory` and an anonymous caller, that a due certificate whose account exists renews
   (the order carries the account's username) and does NOT record `AccountNotFound`. The existing
   stub-based unit tests stay, but a stub that answers by id is exactly what hid this defect, so they
   are not the evidence.
6. **Doc-comment and README sentence.** `rules/README.md` carries 35 and the new arrival line; the
   command in "What an attacker with a request path gets" prints 35. This is a check on the text, run
   by the reviewer, not a test.

## What the author could not verify

Nothing below has been run. By the repository's rules for this work, this note was written before
the code and no build, test or `maran` command was executed while writing it.

- **That the design compiles and the suite is green.** Nothing exists yet to compile.
- **That the call-site census is implementable by reflection.** Finding callers of an interface
  member inside module assemblies needs IL-level inspection (NetArchTest does not expose method
  calls). Whether the repository's existing architecture-test tooling can do it, or whether the
  alternative is a text scan over `backend/src`, was not checked. If only the text scan is possible,
  its blind spots (a call through an alias or `dynamic`, a call in a string) must be stated in the
  test, and the mitigation is weaker than described.
- **That the Accounts integration fixture can set `ICurrentUser.Caller` to `AnonymousCaller` with
  the real directory.** The existing fixtures were not re-read for this note. If they construct the
  directory only through the Host with a signed-in test user, the anonymous test needs its own
  scope.
- **That `IgnoreQueryFilters()` on `Accounts` alone, with no join to `Plans`, behaves as expected
  under the fail-closed filters.** The `Account` filter was read, not run; whether the projection
  to a two-field record translates is a claim about EF Core, not an observation.
- **That `CertificateRenewalHandler` is the only unattended caller of `FindAsync` that needs this.**
  The caller list for `FindAsync` was not re-enumerated for this note. Other unattended handlers
  that call `FindAsync` with no principal would have the same defect and are not known from here.
  The command to find them: `/usr/bin/grep -rnE "\.FindAsync\(" backend/src --include=*.cs` over the
  files that take `IAccountDirectory` (positive control: it must print
  `Ssl/Jobs/CertificateRenewalHandler.cs`).
- **Behaviour of account deletion racing a renewal** (item 3 above) is argued and not observed.

## What a reviewer must check

1. **The call-site list.** Re-run the grep in "What an attacker with a request path gets". The
   expected output is the declaration, the implementation and one call in the renewal handler, and
   nothing else. Confirm the positive control, then confirm no controller and no handler on a
   request path takes the id from the caller and passes it here.
2. **The implementation has exactly one `IgnoreQueryFilters()` and it is on `Accounts` only.** No plan
   read, no join, no `Include`. The projection is `new AccountSystemUser(account.Id, account.Name)`
   or the equivalent for the username column, and nothing else. Re-run the count command and read
   35.
3. **The record carries `Id` and `Username` and nothing else**, is a positional record with no logic,
   and sits in `Maran.Sdk/Contracts/` (rules/csharp.md: no interface beside a model).
4. **The doc comment on the member** states the three things: unscoped, never from a request path,
   the one legitimate caller. A member whose scope statement is missing is the defect rules/architecture.md
   names for a doc comment, and a reviewer treats it as blocking.
5. **`FindAsync` is unchanged.** Diff `AccountDirectory.FindAsync` and `Sees`; neither may be edited.
   The inverse-control test must exist and fail if either is widened for the anonymous caller.
6. **The tests in "Tests the change requires" exist in the diff**, with their controls, and each
   refusing assertion has the accepting one beside it. If the census test is missing, the
   mitigation described in the third bullet of the misuse section is a promise, not a mechanism.
7. **The name.** `ISiteDirectory.FindByIdUnscopedAsync` carries "Unscoped" in its name so a
   reviewer sees the exposure at the call site. Decide whether `FindSystemUserAsync` should carry it
   too (for example `FindSystemUserUnscopedAsync`). The author chose the shorter name because the
   return type already says what comes back; the reviewer may reasonably disagree, and a rename is
   cheap before the first caller lands and expensive after.
8. **`rules/README.md`** names 35, the arrival and the issue, with the command's count re-measured.
9. **The handler** uses `FindSystemUserAsync` for the account lookup, keeps the
   `AccountNotFound` failure path for a `null`, and no longer holds a snapshot it does not use. No
   other line of `CertificateRenewalHandler` changes.
10. **That other unattended callers of `FindAsync` are not left behind** (see the last bullet of
    "What the author could not verify").

## Deviations found when the code landed

None recorded yet. This note was written before the code; a reviewer who finds a difference between
this note and the diff should add it here, stated plainly.
