# Customer Area Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A hosting account's owner is invited by mail, sets a password, signs in to the same panel the administrator uses, and sees only their own resources.

**Architecture:** No new security mechanism. The tenant query filters, `AccountDirectory` scoping, plan-limit enforcement and audit journal already exist and are enforced by analyzers and architecture tests; this work adds the `Customer` login that can finally exercise them, the account-lifecycle handlers that keep it in step, one read endpoint, a module-catalogue audience so navigation is decided on the backend, and the SPA screens.

**Tech Stack:** C# modular monolith (Wolverine, EF Core, PostgreSQL), Vue 3 SPA, Playwright for end-to-end.

**Spec:** `docs/superpowers/specs/2026-09-25-customer-area-design.md`

**Issue:** https://github.com/innovayse/maran/issues/49

**Branch:** work happens on the current branch (`fix/41-agent-namespace-ftps-jail`) by the owner's instruction. No branch is created and nothing is committed or pushed without the owner's explicit command (rules/git.md, CLAUDE.md).

## Global Constraints

- **Order of work is implementation first, tests in a dedicated pass after** (rules/testing.md — the owner's choice, and it overrides the test-first habit of the executing skill). Tasks 1–10 are implementation; tasks 11–13 are the test pass. Definition of Done still gates completion: code without its tests is unfinished.
- Doc comments on ALL production code, private members included. One file = one type (`maran structure`).
- A module references only `Maran.Sdk` and `Maran.SharedKernel`. Cross-module traffic is a Wolverine message or an Sdk interface.
- No ambient clock (`IClock` only) and no process spawning in the backend — both are build errors.
- `IgnoreQueryFilters()` is a build error in `backend/src`; a deliberate use carries `#pragma warning disable RS0030` with a reason on the line.
- All user-facing text comes from the backend's resx in `en`, `ru` and `hy`. Every new key exists in all three.
- Secrets — the invitation token included — never reach a log, an audit entry, a URL the panel stores, or a response body.
- Verification: `maran check`, `dotnet test` in `backend/`, and `npm run lint && npm run typecheck && npm run build` in `frontend/`.

## Review Focus

Five things the spec implies, that no single task's happy path exercises, and that would bite a real operator. Each has its test pinned to the task that owns the code.

1. **An invitation token presented twice.** The second use must be refused even though the row still exists — pinned to Task 12.
2. **A sign-in attempt on an invited-but-passwordless login.** Must be indistinguishable from a wrong password, or the panel enumerates which accounts have been set up — pinned to Task 12.
3. **A suspended account's owner who is already signed in.** Suspension must revoke live sessions, not only block the next sign-in — pinned to Task 11.
4. **Account deletion while Identity holds rows.** The residue audit must see Identity's user and tokens, or a deletion completes green over an orphan login — pinned to Task 11.
5. **A customer reaching an administrator-only endpoint by hand.** Must be the API's refusal, not a hidden menu item — pinned to Task 13.

---

### Task 1: The `AccountCreated` contract

**Files:**
- Create: `backend/src/Maran.Sdk/Events/AccountCreated.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public sealed record AccountCreated(Guid AccountId, string Username, string OwnerEmail)` in namespace `Maran.Sdk.Events`. Tasks 2 and 4 both depend on this exact shape.

- [ ] **Step 1: Create the event**

```csharp
namespace Maran.Sdk.Events;

/// <summary>
/// Announced by the Accounts module immediately after a hosting account has been created, so that
/// the Identity module can issue the panel login that owns it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an Sdk event and not a call.</b> Accounts may not reference Identity
/// (rules/architecture.md "Backend: modular monolith", enforced by <c>ModuleIsolationTests</c>), so
/// the message is declared here, in the surface both already depend on.
/// </para>
/// <para>
/// <b>Past tense, and it is load-bearing.</b> Its sibling <see cref="AccountDeleting"/> is present
/// tense and promises that a throwing subscriber aborts the operation. This one cannot make that
/// promise and does not pretend to: by the time it is announced the agent has already created the
/// system user and the account row is committed, and there is no transaction spanning the two. It
/// is nevertheless INVOKED inline rather than published, so a subscriber's failure is reported to
/// the caller instead of disappearing into a queue — the account then exists without a login, the
/// command answers with the failure, and "resend invitation" is the one recovery path, shared with
/// the case of a panel that has no SMTP configured yet.
/// </para>
/// </remarks>
/// <param name="AccountId">The account that was created; the login issued for it carries this id.</param>
/// <param name="Username">The account's Linux system user name, which is also the login name.</param>
/// <param name="OwnerEmail">The address the invitation is sent to, as supplied by whoever created the account.</param>
public sealed record AccountCreated(Guid AccountId, string Username, string OwnerEmail);
```

- [ ] **Step 2: Build**

Run: `cd backend && dotnet build`
Expected: succeeds.

---

### Task 2: Accounts collects the owner's address and announces the creation

**Files:**
- Modify: `backend/src/Maran.Modules/Accounts/Commands/CreateAccount/CreateAccountCommand.cs`
- Modify: `backend/src/Maran.Modules/Accounts/Commands/CreateAccount/CreateAccountCommandValidator.cs`
- Modify: `backend/src/Maran.Modules/Accounts/Commands/CreateAccount/CreateAccountCommandHandler.cs`
- Modify: the Provisioning module's account-creation call site (find it with `grep -rn "CreateAccountCommand" backend/src/Maran.Modules/Provisioning`)

**Interfaces:**
- Consumes: `AccountCreated` from Task 1.
- Produces: `CreateAccountCommand` gains `string OwnerEmail` as its fourth positional member, before the bind-never members. Task 10's admin form posts it.

- [ ] **Step 1: Add the member to the command**

`OwnerEmail` goes after `PlanId` and before the `IpAddress`/`UserAgent` pair, which must stay last because they carry `[BindNever]`:

```csharp
    Guid PlanId,
    string OwnerEmail,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
```

Document it on the record with the reason it is here: the address is not stored on the account — Identity stores it on the login it creates, so there is exactly one copy of it and no chance of two that disagree.

- [ ] **Step 2: Validate it**

In the validator, beside the existing rules:

```csharp
        RuleFor(command => command.OwnerEmail)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(254);
```

- [ ] **Step 3: Invoke the event from the handler**

In `HandleAsync`, after the journal write and before the `return`:

```csharp
        // INVOKED, not published: a failure to issue the login must reach the caller. The account
        // already exists at this point and is not rolled back — see AccountCreated's remarks — so
        // the failure is reported and "resend invitation" is the recovery.
        await _bus.InvokeAsync(new AccountCreated(account.Id, account.Name, command.OwnerEmail), cancellationToken);
```

Add `IMessageBus _bus` to the handler's constructor if it does not already hold one (the delete handler's constructor is the model), documenting the parameter.

- [ ] **Step 4: Fix the Provisioning call site**

The Provisioning API creates accounts too. Pass the owner address it already receives; if its request contract has no address field, add one with the same validation as Step 2 — an account created through the API must be as reachable by its owner as one created in the UI.

- [ ] **Step 5: Build**

Run: `cd backend && dotnet build`
Expected: succeeds. Compilation errors at other `CreateAccountCommand` construction sites are the point of this step — fix each by supplying the address.

---

### Task 3: The invited state on a login

**Files:**
- Create: `backend/src/Maran.Modules/Identity/Domain/Enums/UserState.cs`
- Modify: `backend/src/Maran.Modules/Identity/Domain/Entities/User.cs`
- Modify: `backend/src/Maran.Modules/Identity/Persistence/Configurations/UserConfiguration.cs`
- Create: a migration in `backend/src/Maran.Modules/Identity/Persistence/Migrations/`

**Interfaces:**
- Produces: `UserState { Invited, Active, Suspended }`; `User.State`; `User.Invite(...)`, `User.Activate(string passwordHash)`, `User.Suspend()`, `User.Resume()`. Tasks 4, 5, 6 and 7 all call these.

- [ ] **Step 1: Create the enum**

```csharp
namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>Whether a panel login may be used, and why not when it may not.</summary>
public enum UserState
{
    /// <summary>Created for a hosting account, waiting for its owner to set a password. Cannot sign in.</summary>
    Invited,

    /// <summary>Usable.</summary>
    Active,

    /// <summary>Its hosting account is suspended, so it cannot sign in until the account is resumed.</summary>
    Suspended,
}
```

- [ ] **Step 2: Put the state on the user**

Add the property with its doc comment, defaulting to `Active` so every existing row and every administrator keeps working:

```csharp
    /// <summary>
    /// Whether this login may be used. An invited login holds no password at all rather than an
    /// empty hash: a sentinel hash in the same field a real one lives in is one careless comparison
    /// away from being accepted.
    /// </summary>
    public UserState State { get; private set; }
```

Add a static factory and the transitions, each documented:

```csharp
    /// <summary>Creates the login of a hosting account's owner, before they have set a password.</summary>
    /// <param name="id">The login's identity.</param>
    /// <param name="username">The account's system user name, which is also the login name.</param>
    /// <param name="email">The address the invitation was sent to.</param>
    /// <param name="accountId">The hosting account this login owns.</param>
    /// <param name="createdAt">The instant of creation, taken from <see cref="IClock"/>.</param>
    /// <returns>A login in <see cref="UserState.Invited"/>, which cannot sign in.</returns>
    public static User Invite(Guid id, string username, string email, Guid accountId, DateTimeOffset createdAt)
    {
        var user = new User(id, username, email, string.Empty, UserRole.Customer, createdAt)
        {
            State = UserState.Invited,
        };

        user.AssignAccount(accountId);
        return user;
    }

    /// <summary>Accepts an invitation: stores the first password and makes the login usable.</summary>
    /// <param name="passwordHash">The Argon2id hash of the password its owner chose.</param>
    public void Activate(string passwordHash)
    {
        PasswordHash = passwordHash;
        State = UserState.Active;
    }

    /// <summary>Blocks sign-in because the hosting account was suspended. Keeps the password.</summary>
    /// <remarks>
    /// An invited login stays invited: suspending an account whose owner never set a password must
    /// not silently consume the invitation they have not used yet.
    /// </remarks>
    public void Suspend()
    {
        if (State == UserState.Active)
        {
            State = UserState.Suspended;
        }
    }

    /// <summary>Lifts a suspension, returning the login to the state it was in before.</summary>
    public void Resume()
    {
        if (State == UserState.Suspended)
        {
            State = UserState.Active;
        }
    }
```

The existing public constructor stays as it is; it is what `CompleteSetup` uses for the administrator, and an administrator is `Active` because `Active` is the enum's zero value.

- [ ] **Step 3: Map the column**

In `UserConfiguration`, store it as a string rather than an integer, matching how the module already persists `UserRole` (read the file and follow it exactly — if the role is stored as an int, store this as an int too; consistency inside the module beats the preference).

- [ ] **Step 4: Create the migration**

Run: `cd backend && dotnet ef migrations add AddUserState --project src/Maran.Modules/Identity --startup-project src/Maran.Host`
Expected: a migration adding a non-null column with the `Active` default.

- [ ] **Step 5: Check the migration is forward-safe**

Run: `maran migrate guard`
Expected: passes — the migration only adds.

---

### Task 4: The invitation token

**Files:**
- Create: `backend/src/Maran.Modules/Identity/Domain/Entities/InvitationToken.cs`
- Create: `backend/src/Maran.Modules/Identity/Services/InvitationTokenHasher.cs`
- Create: `backend/src/Maran.Modules/Identity/Persistence/Configurations/InvitationTokenConfiguration.cs`
- Modify: `backend/src/Maran.Modules/Identity/Persistence/IdentityDbContext.cs`
- Create: a migration

**Interfaces:**
- Produces: `InvitationToken(Guid id, Guid userId, string tokenHash, DateTimeOffset createdAt)`, `IsUsable(DateTimeOffset now)`, `Consume(DateTimeOffset at)`, `static TimeSpan Lifetime`; `InvitationTokenHasher.Generate()` and `.Hash(string token)`. Tasks 5, 6 and 7 call these.

- [ ] **Step 1: Copy the shape of `PasswordResetToken`**

Read `backend/src/Maran.Modules/Identity/Domain/Entities/PasswordResetToken.cs` first and mirror it exactly: `TokenHash`, `ExpiresAt`, nullable `UsedAt`, `IsUsable`, `Consume`. Two deliberate differences, and both go in the doc comment:

- `Lifetime` is longer than a password reset's — an invitation is the first contact with a new customer and may sit in an inbox for a day. Use `TimeSpan.FromDays(7)`.
- Single use is still RECORDED (`UsedAt` stamped), not deleted: a deleted row is indistinguishable from one that never existed, and a replay stops being visible.

- [ ] **Step 2: Mirror the hasher**

`InvitationTokenHasher` mirrors `PasswordResetTokenHasher` — same CSPRNG draw, same digest. Do not share one type between the two: a single hasher would make a reset token and an invitation token interchangeable if either table were ever queried by digest alone.

- [ ] **Step 3: Register the set and configuration**

Add `public DbSet<InvitationToken> InvitationTokens` to `IdentityDbContext` and apply the configuration in `OnModelCreating`, following the neighbouring lines.

- [ ] **Step 4: Migrate**

Run: `cd backend && dotnet ef migrations add AddInvitationTokens --project src/Maran.Modules/Identity --startup-project src/Maran.Host && maran migrate guard`
Expected: both succeed.

---

### Task 5: Identity issues the login and the invitation mail

**Files:**
- Create: `backend/src/Maran.Modules/Identity/IntegrationEvents/Handlers/AccountCreatedHandler.cs`
- Create: `backend/src/Maran.Modules/Identity/Services/InvitationMailComposer.cs`
- Modify: `backend/src/Maran.Modules/Identity/Resources/EmailTemplates.resx`, `.ru.resx`, `.hy.resx`
- Modify: `backend/src/Maran.Sdk/Contracts/AuditActions.cs`
- Modify: `backend/src/Maran.Modules/Identity/Resources/` audit display names (follow `DisplayNameLawTests` — every audit action needs its localized name in all three languages)

**Interfaces:**
- Consumes: `AccountCreated` (Task 1), `User.Invite` (Task 3), `InvitationToken` + `InvitationTokenHasher` (Task 4).
- Produces: `InvitationMailComposer.Compose(string recipient, string token)` returning `SendMailRequested`. Task 6 reuses it.

- [ ] **Step 1: Write the composer**

Mirror `RequestPasswordResetCommandHandler.Compose` — read it first. It formats a link from `PasswordResetOptions.PanelUrl` and falls back to the bare token when no public URL is configured. The invitation link points at `/accept-invitation?token=…`. It is a separate type rather than a private method because two handlers need it (Tasks 5 and 6), and a shared facility inside a module is a service, not a copy.

- [ ] **Step 2: Write the handler**

```csharp
/// <summary>
/// Issues the panel login of a newly created hosting account and sends its owner an invitation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Idempotent.</b> A login already bound to the account means this is a repeat — a retried
/// command, a resent event — and the handler returns rather than creating a second login or a
/// second live token. Two live invitations to one account are two keys, and the owner cannot know
/// which the panel will still honour.
/// </para>
/// <para>
/// <b>The mail is PUBLISHED, never sent here.</b> The SMTP credential lives in the Notifications
/// module and is deliberately out of reach (rules/architecture.md "shared facility"). The queue is
/// local and non-durable on purpose: the body carries a live token, and a durable queue would rest
/// it on disk and outlive its own lifetime. A process that dies between the publish and the send
/// loses the mail, and the administrator resends.
/// </para>
/// </remarks>
public sealed class AccountCreatedHandler
```

Its `HandleAsync(AccountCreated message, CancellationToken cancellationToken)`:

```csharp
        var existing = await _dbContext.Users
            .AnyAsync(candidate => candidate.AccountId == message.AccountId, cancellationToken);
        if (existing)
        {
            return;
        }

        var now = _clock.UtcNow;
        var user = User.Invite(Guid.NewGuid(), message.Username, message.OwnerEmail, message.AccountId, now);
        var token = InvitationTokenHasher.Generate();

        _dbContext.Users.Add(user);
        _dbContext.InvitationTokens.Add(
            new InvitationToken(Guid.NewGuid(), user.Id, InvitationTokenHasher.Hash(token), now));
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordIdentifiedAsync(
            user.Id,
            user.Username,
            AuditActions.CustomerInvited,
            ipAddress: string.Empty,
            userAgent: string.Empty,
            succeeded: true,
            cancellationToken);

        await _bus.PublishAsync(_composer.Compose(user.Email, token));
```

Read `IdentityAuditJournal` before writing this and use whichever of its methods matches an entry with no request behind it; the parameter names above follow `RequestPasswordResetCommandHandler` and may differ.

**The token appears in the mail and nowhere else** — not in the journal entry, not in the return value, not in a log line.

- [ ] **Step 3: Add `CustomerInvited` to the audit vocabulary**

Add the constant to `AuditActions` and its display name to the three resx files. `AuditActionDisplayNameTests` and `ResourceKeyParityTests` fail if any language is missing.

- [ ] **Step 4: Add the mail templates**

`InvitationSubject`, `InvitationBody`, `InvitationLink`, `InvitationTokenOnly` in `en`, `ru` and `hy`, mirroring the password-reset keys.

- [ ] **Step 5: Build**

Run: `cd backend && dotnet build && maran structure`
Expected: both succeed.

---

### Task 6: Accepting and resending an invitation

**Files:**
- Create: `backend/src/Maran.Modules/Identity/Commands/AcceptInvitation/` (command, validator, handler)
- Create: `backend/src/Maran.Modules/Identity/Commands/ResendInvitation/` (command, validator, handler)
- Modify: `backend/src/Maran.Modules/Identity/Controllers/AuthController.cs` (accept — anonymous)
- Create: `backend/src/Maran.Modules/Identity/Controllers/InvitationsController.cs` (resend — administrator)

**Interfaces:**
- Consumes: everything from Tasks 3–5.
- Produces: `POST /api/v1/auth/accept-invitation` taking `{ token, newPassword }`; `POST /api/v1/invitations/{accountId}/resend`. Task 10's SPA calls both.

- [ ] **Step 1: Write `AcceptInvitationCommandHandler`**

Model it on `ResetPasswordCommandHandler` — read that file first; the token lookup, the `IsUsable` check, the consume-and-retire loop and the single refusal path are all there and must not be re-invented. The differences:

```csharp
        // The login becomes usable only here, so a token that never arrives leaves an account
        // nobody can sign in to rather than one anybody can.
        user.Activate(_passwordHasher.Hash(command.NewPassword));
```

and the refusal is the same shape for every unusable token — expired, spent, or never issued.

Validate `NewPassword` against the existing password policy the reset command uses. Do not add a second policy.

- [ ] **Step 2: Expose it anonymously, rate limited**

In `AuthController`, beside `reset-password`. Name the `[AllowAnonymous]` on the action, never on the class — the class-level attribute outranks action-level `[Authorize]` and would open `logout-all`, which the file's own remarks explain. Apply the same rate-limiting policy the reset endpoint carries.

- [ ] **Step 3: Write `ResendInvitationCommandHandler`**

Administrator action, taking an account id. It:

1. Finds the login for that account; **creates it** if missing — this is the recovery path for a failed `AccountCreated` handler, and it is the reason the command exists rather than a plain "new token" endpoint.
2. Refuses when the login is already `Active`: there is nothing to invite somebody to, and a fresh token for a live account would be a password-reset in disguise, bypassing the reset endpoint's rate limit.
3. Retires every outstanding token for that user, then issues one.
4. Publishes the mail through `InvitationMailComposer`.
5. Journals `AuditActions.CustomerInvited` with the administrator's address and client.

- [ ] **Step 4: Expose it to administrators**

`InvitationsController` with `[Authorize(Policy = RolePolicies.AdminOnly)]`, following `BaseApiController` and the thin-controller shape (bind, dispatch, translate).

- [ ] **Step 5: Build and check structure**

Run: `cd backend && dotnet build && maran structure`
Expected: both succeed.

---

### Task 7: The login follows the account's life

**Files:**
- Create: `backend/src/Maran.Modules/Identity/IntegrationEvents/Handlers/AccountSuspendingHandler.cs`
- Create: `backend/src/Maran.Modules/Identity/IntegrationEvents/Handlers/AccountResumingHandler.cs`
- Create: `backend/src/Maran.Modules/Identity/IntegrationEvents/Handlers/AccountDeletingHandler.cs`
- Modify: `backend/src/Maran.Modules/Identity/Commands/Login/LoginCommandHandler.cs`
- Modify: whatever type implements `IAccountResidueAuditor` mapping for Identity (find it with `grep -rn "IAccountResidueAuditor" backend/src`)

**Interfaces:**
- Consumes: `User.Suspend()`, `User.Resume()` (Task 3); the existing `ISessionService.RevokeAllAsync`.
- Produces: nothing other tasks call.

- [ ] **Step 1: Suspension closes the door on people already inside**

```csharp
        user.Suspend();
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Revoking the sessions is the half that matters: a suspension that only blocks the NEXT
        // sign-in leaves whoever is already signed in working normally until their access token
        // expires, which is exactly the window a suspension exists to close.
        await _sessionService.RevokeAllAsync(user.Id, SessionRevocationReason.AccountSuspended, cancellationToken);
```

Add the revocation reason to `SessionRevocationReason` if it has no suitable member, with its doc comment.

- [ ] **Step 2: Resuming reopens it**

Calls `user.Resume()` and saves. It does NOT restore sessions — a revoked session is gone, and the owner signs in again.

- [ ] **Step 3: Deletion takes the login, its tokens and its sessions**

Follow `backend/src/Maran.Modules/Ftp/IntegrationEvents/Handlers/AccountDeletingHandler.cs` exactly, including the shape of the filter bypass:

```csharp
#pragma warning disable RS0030 // the account is being deleted, so its rows must be found whoever asked for the deletion
        var owned = await _dbContext.Users
            .IgnoreQueryFilters()
            .Where(row => row.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
```

Delete the users found, their invitation tokens, their password-reset tokens and their sessions.

- [ ] **Step 4: Refuse sign-in for a login that is not active**

In `LoginCommandHandler`, after the password is verified and before a session is issued:

```csharp
        // Same refusal as a wrong password, deliberately. A distinct "your account is suspended"
        // tells an attacker which usernames exist and which of them belong to real customers.
        if (user.State != UserState.Active)
        {
            return await RefuseAsync(user, command, cancellationToken);
        }
```

Read the handler first and reuse its existing refusal path rather than composing a second one. An invited user holds an empty password hash, so verification already fails — this check is what makes a SUSPENDED user refuse too, and what makes the invited case refuse for a stated reason rather than by accident.

- [ ] **Step 5: Map Identity's rows into the residue audit**

Identity must report the users it holds for an account. A module that subscribed to nothing is indistinguishable from one that had nothing to release, which is the failure `IAccountResidueAuditor` exists to catch — read its doc comment and follow the mapping another module already registers.

- [ ] **Step 6: Build**

Run: `cd backend && dotnet build && maran structure`

---

### Task 8: "My account"

**Files:**
- Create: `backend/src/Maran.Modules/Accounts/Queries/GetMyAccount/` (query, handler, DTO)
- Modify: `backend/src/Maran.Modules/Accounts/Controllers/AccountsController.cs`

**Interfaces:**
- Produces: `GET /api/v1/accounts/me` returning `{ id, name, primaryDomain, status, plan: { displayName, diskQuotaMb, maxSites, maxDatabases, maxSftpUsers, maxFtpUsers, maxCronEntries } }`. Task 10 renders it.

- [ ] **Step 1: Write the handler**

The account id comes from `ICurrentUser.AccountId` and from nowhere else:

```csharp
        // Read off the token, never off the request. There is no parameter to forge because there
        // is no parameter.
        if (_currentUser.AccountId is not { } accountId)
        {
            return Result<MyAccountDto>.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }
```

An administrator holds no `AccountId` and therefore gets the same `NotFound` — the endpoint answers "the account you own", and an administrator owns none. That is not a gap; the administrator has the whole list.

- [ ] **Step 2: Expose it**

Add the action to `AccountsController` with `[Authorize(Policy = RolePolicies.AnyAuthenticated)]` on the ACTION — the class carries `AdminOnly` today and that must stay the default for every other action on it. Verify by reading the class attribute; if the class-level policy cannot be overridden by the action, put this action in its own controller instead.

- [ ] **Step 3: Build**

Run: `cd backend && dotnet build`

---

### Task 9: The module catalogue decides what a customer is offered

**Files:**
- Modify: `backend/src/Maran.Sdk/Contracts/Manifest.cs`
- Create: `backend/src/Maran.Sdk/Contracts/ModuleAudience.cs`
- Modify: every `*Manifest.cs` in `backend/src/Maran.Modules/` (one line each)
- Modify: the modules catalogue query handler (find it with `grep -rn "Manifest" backend/src/Maran.Modules/*/Queries -l`)

**Interfaces:**
- Produces: `ModuleAudience { Everyone, AdministratorOnly }`; `Manifest` gains a trailing `ModuleAudience Audience` member.

- [ ] **Step 1: Create the enum**

```csharp
namespace Maran.Sdk.Contracts;

/// <summary>Who a module's screens are for, as the module itself declares it.</summary>
/// <remarks>
/// This is what keeps the customer's navigation an answer given by the panel rather than a list the
/// SPA maintains. A module — including one bought on the marketplace, which the open code does not
/// know exists — states its own audience, and the catalogue endpoint returns to each caller only
/// what is addressed to them. The alternative, a role check in the SPA, is a second copy of an
/// authorization decision: it would go stale the first time a module was added and it would be
/// wrong in the direction that hides working screens.
/// </remarks>
public enum ModuleAudience
{
    /// <summary>Every signed-in user, each seeing their own resources through the tenant filters.</summary>
    Everyone,

    /// <summary>Administrators only: the module governs the server rather than an account's resources.</summary>
    AdministratorOnly,
}
```

- [ ] **Step 2: Add it to the manifest**

Append `ModuleAudience Audience` to the record with its doc comment. Every module's manifest now fails to compile, which is the intent: each must state its audience rather than inherit a default.

- [ ] **Step 3: Declare each module's audience**

`AdministratorOnly`: Firewall, Monitoring, Notifications, Licensing, Accounts. `Everyone`: Sites, Ssl, Databases, Ftp, Sftp, Files, Cron, Backups, Tasks, Identity.

- [ ] **Step 4: Filter the catalogue**

In the catalogue handler, drop `AdministratorOnly` entries when `!_currentUser.IsAdmin`. Document that this is presentation, not enforcement — the endpoints remain the boundary.

- [ ] **Step 5: Build and run the architecture tests**

Run: `cd backend && dotnet build && dotnet test tests/Maran.ArchitectureTests`
Expected: pass. `ModuleCoverageTests` may need the new member; read its failure before changing it.

---

### Task 10: The SPA's customer zone

**Files:**
- Create: `frontend/src/pages/auth/AcceptInvitationPage.vue`
- Create: `frontend/src/pages/accounts/MyAccountPage.vue`
- Modify: `frontend/src/router/index.ts`
- Modify: `frontend/src/composables/apis/useAuthApi.ts`, `useAccountsApi.ts`
- Modify: `frontend/src/stores/auth.ts`, `stores/accounts.ts`
- Modify: `frontend/src/pages/accounts/AccountFormPage.vue` (owner address field)
- Modify: `frontend/src/components/shell/ShellUserBlock.vue` (no change to the permissive branch — read its remarks first)
- Modify: `frontend/src/locales/en.json`, `ru.json`, `hy.json` (follow the existing file layout)

**Interfaces:**
- Consumes: the endpoints from Tasks 6 and 8.

- [ ] **Step 1: Accept-invitation screen**

`/accept-invitation?token=…`, in the public layout beside `/reset-password` — read `ResetPasswordPage.vue` and follow it: same form shape, same password rules, same error rendering. Only the endpoint and the copy differ.

- [ ] **Step 2: Owner address on the account form**

Add the field to `AccountFormPage.vue` and to the create payload. Label and validation message in all three locales.

- [ ] **Step 3: "My account" screen**

Route `/my-account`, rendering the plan, its limits and the account's status from Task 8's endpoint. All API access goes through `useAccountsApi` called from `stores/accounts.ts`; the page reads the store (rules/vue.md). Const arrow functions; UI kit components only; no raw HTML elements where a kit component exists.

- [ ] **Step 4: Do NOT add a role guard to the router**

Confirm by reading the comments already in `router/index.ts` around the database and backup routes: a second copy of an authorization decision is a bug. A customer who types an administrator URL gets the API's 403 rendered on the page. The sidebar is already correct without a change, because Task 9 made the catalogue answer per role.

- [ ] **Step 5: Verify**

Run: `cd frontend && npm run lint && npm run typecheck && npm run build`
Expected: all three pass.

---

### Task 11: Tests — lifecycle and residue

**Files:**
- Create: `backend/tests/Maran.Modules.Identity.Tests/IntegrationEvents/AccountCreatedHandlerTests.cs`, `AccountSuspendingHandlerTests.cs`, `AccountDeletingHandlerTests.cs`

- [ ] **Step 1: Write them**

Follow the fixture style of `backend/tests/Maran.Modules.Ftp.Tests/TestSupport/`. Cover, one test each:

- A created account yields exactly one invited login, one live token, and one published `SendMailRequested`.
- The handler is idempotent: invoked twice, one login and one live token.
- The published mail's body contains the token; the audit entry does not. Assert the absence explicitly.
- **(Review Focus 3)** Suspension revokes the live sessions of an already-signed-in owner, not merely the next sign-in.
- Suspension leaves an INVITED login invited, so the unused invitation survives.
- **(Review Focus 4)** Deletion leaves no user, no invitation token, no reset token and no session for the account, asserted through the residue audit rather than only by direct query.

- [ ] **Step 2: Run**

Run: `cd backend && dotnet test tests/Maran.Modules.Identity.Tests`

---

### Task 12: Tests — invitation and sign-in refusals

**Files:**
- Create: `backend/tests/Maran.Modules.Identity.Tests/Commands/AcceptInvitation/AcceptInvitationCommandHandlerTests.cs`
- Create: `backend/tests/Maran.Modules.Identity.Tests/Commands/ResendInvitation/ResendInvitationCommandHandlerTests.cs`
- Modify: the existing login handler tests

- [ ] **Step 1: Write them**

- A valid token sets the password and activates the login.
- **(Review Focus 1)** The same token presented twice: the second attempt is refused, and the refusal is identical to the one an unknown token gets.
- An expired token is refused.
- Accepting retires every other outstanding token of that user.
- **(Review Focus 2)** Signing in as an invited login is refused with the same result as a wrong password — assert the results are equal, not merely that both fail.
- Signing in as a suspended login is refused the same way.
- Resend on a missing login creates it; resend on an active login is refused; resend retires the previous token.

- [ ] **Step 2: Run**

Run: `cd backend && dotnet test tests/Maran.Modules.Identity.Tests`

---

### Task 13: Tests — the tenant boundary and the end-to-end path

**Files:**
- Create/modify: integration tests under `backend/tests/*.IntegrationTests` for every endpoint a customer can reach
- Create: `frontend/e2e/customer-area.spec.ts`

- [ ] **Step 1: The IDOR test per endpoint**

For each customer-reachable endpoint (sites, SSL, databases, FTP, SFTP, files, cron, backups, tasks, `accounts/me`): customer A requests customer B's resource and gets **404, not 403** (rules/testing.md, Definition of Done item 3). A 403 confirms the resource exists.

- [ ] **Step 2: (Review Focus 5) The administrator-only surface**

A customer calling an `AdminOnly` endpoint by hand gets the API's refusal. Assert it against the endpoint, not against the menu.

- [ ] **Step 3: The end-to-end path**

In `frontend/e2e/`, following the fixtures already there: invitation link → set password → signed in → own sites visible → server-wide entries absent from the navigation.

- [ ] **Step 4: Full verification**

Run: `maran check`, then `cd backend && dotnet test`, then `cd frontend && npm run lint && npm run typecheck && npm run build`
Expected: all pass. Report the actual output; a claim of success without it is not a result (superpowers:verification-before-completion).
