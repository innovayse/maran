# Maran Authentication Implementation Plan (roadmap item 2, part 1 of 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn Maran from an open panel into an authenticated one: a first administrator created from the installer's one-time token, password login with Argon2id and TOTP, 15-minute JWTs with rotating refresh cookies, revocable sessions, an append-only audit journal, and a real `ICurrentUser` that makes every existing endpoint deny by default.

**Architecture:** A new `Maran.Modules.Identity` module owns the `identity` PostgreSQL schema (users, sessions, recovery codes, audit) and the whole HTTP surface under `/api/v1/auth`. The Host gains JWT bearer authentication, an authorization policy per role, a CSRF header requirement and a real `ICurrentUser` read from `HttpContext`. The SPA gains an auth store, a login page, a setup page and a router guard; every API call carries the access token in memory and the refresh token in an httpOnly cookie the JavaScript never sees.

**Tech Stack:** .NET 9 (ASP.NET Core, EF Core 9, Wolverine, FluentValidation), `Microsoft.AspNetCore.Authentication.JwtBearer` 9.0.19, `Konscious.Security.Cryptography.Argon2` 1.3.1, `Otp.NET` 1.4.1, PostgreSQL 16, Vue 3 + Pinia + vue-i18n, Playwright.

**Spec:** `docs/superpowers/specs/2026-08-29-maran-design.md` (§8 tenancy and roles, §10 panel security, §15 errors, §16 testing, §17 UI)

**Issue:** https://github.com/innovayse/maran/issues/1

## Scope note — why this is part 1 of 2

Roadmap item 2 reads "Auth + Accounts". Those are two subsystems, not one: authentication is a panel-wide concern with its own schema, its own HTTP surface and its own threat model, while the accounts lifecycle is a module that provisions Linux users through the Rust agent's `AccountsService` (already specified in `proto/agent/v1/accounts.proto`, not yet implemented in the agent). Each produces working, testable software alone, and bundling them would make a single plan nobody can hold in their head.

This document is authentication. The accounts lifecycle — the agent's `AccountsService` implementation, `Maran.Agent.Client`'s wrapper for it, `GET/PATCH/DELETE /api/v1/accounts/{id}`, suspension, quotas, and the plan picker in the SPA — becomes its own plan, written when this one lands. The authorization attributes this plan puts on `AccountsController` are what make that plan's IDOR tests meaningful, so the order is not arbitrary.

## Global Constraints

Copied from the spec and `rules/`. Every task's requirements implicitly include this section.

- `rules/README.md` and every file it lists are normative and binding. Read them before Task 1.
- **Never `git commit` or push.** Each task's last step is a checkpoint: report and wait for the owner's explicit command (`rules/git.md`). No AI attribution trailers; identity when commanded is `edgar2031 <edgar.poghosyan.2031@gmail.com>`; Conventional Commits.
- Every repository file — code, comments, test names, script output, commit messages — is in English. Russian belongs in chat only.
- Doc comments (`///`) on ALL production code including private members. One file = one type/unit. Test code is exempt from doc comments; the behavior-sentence name is the documentation.
- C#: `net9.0`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, package versions ONLY in `backend/Directory.Packages.props`.
- Braces on every body, including single-line ones. No expression-bodied members.
- Errors flow as `Result<T>` / `Error`; exceptions are for bugs and infrastructure faults only. Error codes are flat PascalCase and are the name of their resx entry.
- The backend owns all user-facing text: every error code has an entry in `Resources/ErrorMessages.resx`, `.ru.resx` and `.hy.resx`. `Maran.ArchitectureTests`' `ResourceKeyParityTests` enforces this.
- Database naming is PascalCase for tables, columns, indexes and constraints; schemas are lowercase and named after the module (`identity`).
- Passwords are Argon2id. Nothing security-relevant uses MD5 or SHA1. No home-grown crypto.
- Secrets never reach logs, error messages or URLs — including anything that *acts* as a secret (a setup token, a recovery code, a refresh token). Every configuration variable the product reads gets an entry in `.env.example`, `docker/.env.example` and `installer/panel.env.example` as applicable.
- Frontend: `const` arrow functions only; UI comes from `components/ui` (never raw markup); API composables are called from Pinia stores only; every `alt`/`label`/`aria-label`/`placeholder` exists AND comes from the locale files, in `en`, `ru` and `hy`; types are grouped by domain in `src/types/`; maximal use of stock Tailwind classes.
- Definition of Done per feature (`rules/testing.md`): unit tests for the handler logic, an integration test of the real HTTP surface, an audit event written and asserted, and i18n keys present in all three locales.
- Verification gates: `bash scripts/preflight.sh`, `bash scripts/check-structure.sh`, `dotnet test` in `backend/`, and `npm run lint && npm run typecheck && npm run build` plus the Playwright specs in `frontend/`.
- **Sensitive change escalation** (`rules/security.md`): this entire plan touches auth, session and token handling. Every task's report includes a threat note — what an attacker could do with the surface it adds, and why it is safe.

---

## File Structure

### Backend — new project `backend/src/Maran.Modules/Identity/`

| File | Responsibility |
| --- | --- |
| `Maran.Modules.Identity.csproj` | Module project; references `Maran.Sdk`, `Maran.SharedKernel`. |
| `IdentityModule.cs` | `IPanelModule`: registers `IdentityDbContext`, the token services, the resource managers. |
| `IdentityManifest.cs` | Module identity (`Id: "identity"`, tier `Included`). |
| `Domain/User.cs` | Panel login: id, username, email, password hash, role, 2FA state, lockout, timestamps. |
| `Domain/UserRole.cs` | `Admin` / `Customer` (spec §8). |
| `Domain/Session.cs` | One refresh-token chain: hash, family id, issue/expiry, revocation, IP, user agent. |
| `Domain/RecoveryCode.cs` | One single-use 2FA recovery code (hash + consumed flag). |
| `Domain/AuditEvent.cs` | One append-only journal row. |
| `Domain/SessionRevocationReason.cs` | Why a session ended — logout, rotation, reuse detected, admin. |
| `Persistence/IdentityDbContext.cs` + `Configurations/` + `Migrations/` | The `identity` schema. |
| `Commands/…`, `Queries/…` | One folder per use case, each with command/query + handler + validator. |
| `Controllers/AuthController.cs`, `SessionsController.cs`, `SetupController.cs` | The HTTP surface. |
| `Services/` | `TotpService`, `RecoveryCodeService`, `SessionService` — the module's domain services. |
| `Resources/ErrorMessages*.resx`, `DisplayNames*.resx` | Its text in `en`/`ru`/`hy`. `ErrorMessages.resx` also defines the module's error codes: the class is generated from it and code raises `Error.Of(nameof(ErrorMessages.X))` (rules/csharp.md). |

### Backend — shared and host changes

| File | Responsibility |
| --- | --- |
| `Maran.SharedKernel/Interfaces/IPasswordHasher.cs` | Contract: hash, verify, needs-rehash. |
| `Maran.SharedKernel/Security/Argon2idPasswordHasher.cs` | The only implementation. |
| `Maran.SharedKernel/Security/PasswordHashParameters.cs` | Memory/iterations/parallelism/salt length, one place. |
| `Maran.Sdk/Interfaces/IAuditWriter.cs` + `Maran.Sdk/Contracts/AuditEntry.cs` | The cross-module audit contract every module writes through. |
| `Maran.Sdk/Permissions/AuthPermissions.cs` | Permission constants for the auth area. |
| `Maran.Host/Configuration/JwtOptions.cs`, `SetupOptions.cs` | Typed, startup-validated settings. |
| `Maran.Host/Extensions/AuthenticationExtensions.cs` | `AddPanelAuthentication` / `UsePanelAuthentication`. |
| `Maran.Host/Authorization/RolePolicies.cs` | Policy names and their registration. |
| `Maran.Host/Security/HttpContextCurrentUser.cs` | The real `ICurrentUser`; replaces `UnauthenticatedCurrentUser.cs` (deleted). |
| `Maran.Host/Middleware/CsrfHeaderMiddleware.cs` + its `Extensions/` partner | Rejects cookie-authenticated state changes without the custom header. |
| `Maran.Host/Extensions/SecurityHeadersExtensions.cs` | CSP and the security header set. |

### Frontend

| File | Responsibility |
| --- | --- |
| `src/types/auth.ts` | Every auth type, grouped by domain (one file, per `rules/vue.md`). |
| `src/composables/apis/useAuthApi.ts` | Login, refresh, logout, sessions, 2FA, setup. |
| `src/stores/auth.ts` | The only caller of that composable; owns the in-memory access token. |
| `src/router/authGuard.ts` | Redirects unauthenticated navigation to `/login`. |
| `src/pages/auth/LoginPage.vue`, `TwoFactorPage.vue`, `SetupPage.vue` | The three unauthenticated screens, on `AuthLayout`. |
| `src/pages/settings/SessionsPage.vue`, `TwoFactorSettingsPage.vue` | The two authenticated ones. |
| `e2e/auth-*.spec.ts` | Playwright coverage for each flow. |

---

### Task 1: Argon2id password hashing

**Files:**
- Modify: `backend/Directory.Packages.props`
- Create: `backend/src/Maran.SharedKernel/Interfaces/IPasswordHasher.cs`
- Create: `backend/src/Maran.SharedKernel/Security/PasswordHashParameters.cs`
- Create: `backend/src/Maran.SharedKernel/Security/Argon2idPasswordHasher.cs`
- Modify: `backend/src/Maran.SharedKernel/DependencyInjection.cs`
- Test: `backend/tests/Maran.SharedKernel.Tests/Security/Argon2idPasswordHasherTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `IPasswordHasher` with `string Hash(string password)`, `bool Verify(string password, string hash)`, `bool NeedsRehash(string hash)`; registered by `AddSharedKernel()`.

- [ ] **Step 1: Add the package**

In `backend/Directory.Packages.props`, inside the existing `<ItemGroup>`:

```xml
    <!-- Argon2id is the mandated password KDF (rules/security.md item 9). Konscious is the
         reference managed implementation; .NET ships no Argon2 of its own, and
         Rfc2898DeriveBytes (PBKDF2) is not an acceptable substitute for a new system. -->
    <PackageVersion Include="Konscious.Security.Cryptography.Argon2" Version="1.3.1" />
```

Add `<PackageReference Include="Konscious.Security.Cryptography.Argon2" />` to `backend/src/Maran.SharedKernel/Maran.SharedKernel.csproj`.

- [ ] **Step 2: Write the failing tests**

`backend/tests/Maran.SharedKernel.Tests/Security/Argon2idPasswordHasherTests.cs`:

```csharp
using Maran.SharedKernel.Security;

namespace Maran.SharedKernel.Tests.Security;

public sealed class Argon2idPasswordHasherTests
{
    private readonly Argon2idPasswordHasher _hasher = new();

    [Fact]
    public void Hashing_the_same_password_twice_produces_different_hashes()
    {
        var first = _hasher.Hash("correct horse battery staple");
        var second = _hasher.Hash("correct horse battery staple");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Verifying_the_original_password_succeeds()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.True(_hasher.Verify("correct horse battery staple", hash));
    }

    [Fact]
    public void Verifying_a_different_password_fails()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.False(_hasher.Verify("correct horse battery stapl", hash));
    }

    [Fact]
    public void Verifying_against_a_malformed_hash_returns_false_instead_of_throwing()
    {
        Assert.False(_hasher.Verify("anything", "not-a-hash"));
    }

    [Fact]
    public void A_hash_produced_with_weaker_parameters_needs_rehashing()
    {
        // Encoded with half the current memory cost, which is exactly the migration
        // case NeedsRehash exists for: the parameters were raised after this hash was stored.
        var weaker = $"$argon2id$v=19$m={PasswordHashParameters.MemoryKib / 2},t={PasswordHashParameters.Iterations},p={PasswordHashParameters.Parallelism}$c2FsdHNhbHRzYWx0c2ExdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g";

        Assert.True(_hasher.NeedsRehash(weaker));
    }

    [Fact]
    public void A_hash_produced_with_the_current_parameters_does_not_need_rehashing()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.False(_hasher.NeedsRehash(hash));
    }
}
```

- [ ] **Step 3: Run them to verify they fail**

Run: `cd backend && dotnet test tests/Maran.SharedKernel.Tests`
Expected: compile failure — `Argon2idPasswordHasher` does not exist.

- [ ] **Step 4: Write the parameters**

`backend/src/Maran.SharedKernel/Security/PasswordHashParameters.cs`:

```csharp
namespace Maran.SharedKernel.Security;

/// <summary>
/// The Argon2id cost parameters every panel password hash is produced with, in one place so that
/// raising them is a single edit and <see cref="Interfaces.IPasswordHasher.NeedsRehash"/> can
/// compare a stored hash against them.
/// </summary>
/// <remarks>
/// 64 MiB with three iterations and two lanes is the OWASP Password Storage Cheat Sheet's
/// second configuration, chosen deliberately over the 19 MiB first one: the panel authenticates a
/// handful of operators, not a consumer login wall, so a hash costing ~100 ms of one core is
/// affordable per login and expensive per billion guesses. The values are constants rather than
/// configuration because an operator who lowers them silently weakens every future password, and
/// the only safe direction — raising them — is served by <c>NeedsRehash</c> on next login.
/// </remarks>
public static class PasswordHashParameters
{
    /// <summary>Memory cost, in kibibytes.</summary>
    public const int MemoryKib = 65536;

    /// <summary>Number of passes over memory.</summary>
    public const int Iterations = 3;

    /// <summary>Degree of parallelism (lanes).</summary>
    public const int Parallelism = 2;

    /// <summary>Length of the random per-password salt, in bytes.</summary>
    public const int SaltBytes = 16;

    /// <summary>Length of the derived hash, in bytes.</summary>
    public const int HashBytes = 32;
}
```

- [ ] **Step 5: Write the contract**

`backend/src/Maran.SharedKernel/Interfaces/IPasswordHasher.cs`:

```csharp
namespace Maran.SharedKernel.Interfaces;

/// <summary>Hashes and verifies panel passwords. The only way a password is ever stored.</summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a plaintext password with a fresh random salt.</summary>
    /// <param name="password">The plaintext password, never logged or stored.</param>
    /// <returns>The PHC-string-format encoded hash, safe to store.</returns>
    string Hash(string password);

    /// <summary>Verifies a plaintext password against a stored hash in constant time.</summary>
    /// <param name="password">The plaintext password supplied by the caller.</param>
    /// <param name="hash">The stored encoded hash.</param>
    /// <returns>True when the password matches; false when it does not, and also when the stored hash is malformed.</returns>
    bool Verify(string password, string hash);

    /// <summary>Reports whether a stored hash was produced with weaker parameters than the current ones.</summary>
    /// <param name="hash">The stored encoded hash.</param>
    /// <returns>True when the hash should be recomputed on the next successful login.</returns>
    bool NeedsRehash(string hash);
}
```

- [ ] **Step 6: Implement it**

`backend/src/Maran.SharedKernel/Security/Argon2idPasswordHasher.cs` — encode as the PHC string
`$argon2id$v=19$m=<mem>,t=<iters>,p=<lanes>$<b64 salt>$<b64 hash>` (unpadded base64, as the PHC
format specifies), parse the same shape back in `Verify`, compare with
`CryptographicOperations.FixedTimeEquals`, and return `false` from every parse failure rather than
throwing — a malformed row in the database must fail the login, not the process. `NeedsRehash`
parses the `m`/`t`/`p` values and returns true when any is below `PasswordHashParameters`.

- [ ] **Step 7: Register it**

In `AddSharedKernel`, beside the existing registrations:

```csharp
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/Maran.SharedKernel.Tests`
Expected: PASS, zero warnings.

- [ ] **Step 9: Checkpoint** — report Task 1 done with its threat note; the owner decides on the commit (`feat(backend): argon2id password hashing`).

---

### Task 2: The Identity module — domain, schema, migration

**Files:**
- Create: `backend/src/Maran.Modules/Identity/Maran.Modules.Identity.csproj`, `GlobalUsings.cs`, `IdentityModule.cs`, `IdentityManifest.cs`
- Create: `backend/src/Maran.Modules/Identity/Domain/{User,UserRole,Session,SessionRevocationReason,RecoveryCode,AuditEvent}.cs`
- Create: `backend/src/Maran.Modules/Identity/Persistence/IdentityDbContext.cs`, `DesignTimeDbContextFactory.cs`, `Configurations/{User,Session,RecoveryCode,AuditEvent}Configuration.cs`
- Create: `backend/src/Maran.Modules/Identity/Resources/ErrorMessages{,.ru,.hy}.resx`, `Resources/DisplayNames{,.ru,.hy}.resx`
- Modify: `backend/Maran.sln`, `backend/src/Maran.Host/Modules/ModuleRegistry.cs`, `backend/src/Maran.Host/Maran.Host.csproj`
- Test: `backend/tests/Maran.Modules.Identity.Tests/Domain/{UserTests,SessionTests,RecoveryCodeTests}.cs`

**Interfaces:**
- Consumes: `IPasswordHasher` (Task 1) — the domain stores a hash, it never hashes.
- Produces: `User` (`Id`, `Username`, `Email`, `PasswordHash`, `Role`, `TotpSecret`, `IsTotpEnabled`, `CreatedAt`, `LastLoginAt`), `Session` (`Id`, `UserId`, `FamilyId`, `TokenHash`, `IssuedAt`, `ExpiresAt`, `RevokedAt`, `RevocationReason`, `IpAddress`, `UserAgent`), `RecoveryCode`, `AuditEvent`, `IdentityDbContext.SchemaName = "identity"`.

- [ ] **Step 1: Copy the Accounts module's project shape**

Mirror `backend/src/Maran.Modules/Accounts/Maran.Modules.Accounts.csproj` exactly — same target framework, same `EmbeddedResource` handling for the resx files, same project references — with the assembly name `Maran.Modules.Identity`. Copy `GlobalUsings.cs` verbatim.

- [ ] **Step 2: Write the failing domain tests**

`backend/tests/Maran.Modules.Identity.Tests/Domain/UserTests.cs`:

```csharp
using Maran.Modules.Identity.Domain;

namespace Maran.Modules.Identity.Tests.Domain;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_user_has_two_factor_disabled()
    {
        var user = new User(Guid.NewGuid(), "admin", "admin@example.com", "hash", UserRole.Admin, Now);

        Assert.False(user.IsTotpEnabled);
        Assert.Null(user.TotpSecret);
    }

    [Fact]
    public void Enabling_two_factor_stores_the_secret()
    {
        var user = new User(Guid.NewGuid(), "admin", "admin@example.com", "hash", UserRole.Admin, Now);

        user.EnableTotp("JBSWY3DPEHPK3PXP");

        Assert.True(user.IsTotpEnabled);
        Assert.Equal("JBSWY3DPEHPK3PXP", user.TotpSecret);
    }

    [Fact]
    public void Disabling_two_factor_clears_the_secret_rather_than_only_the_flag()
    {
        var user = new User(Guid.NewGuid(), "admin", "admin@example.com", "hash", UserRole.Admin, Now);
        user.EnableTotp("JBSWY3DPEHPK3PXP");

        user.DisableTotp();

        Assert.False(user.IsTotpEnabled);
        Assert.Null(user.TotpSecret);
    }

    [Fact]
    public void Recording_a_login_updates_the_last_login_instant()
    {
        var user = new User(Guid.NewGuid(), "admin", "admin@example.com", "hash", UserRole.Admin, Now);

        user.RecordLogin(Now.AddHours(1));

        Assert.Equal(Now.AddHours(1), user.LastLoginAt);
    }
}
```

`backend/tests/Maran.Modules.Identity.Tests/Domain/SessionTests.cs`:

```csharp
using Maran.Modules.Identity.Domain;

namespace Maran.Modules.Identity.Tests.Domain;

public sealed class SessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private static Session NewSession()
    {
        return new Session(
            Guid.NewGuid(),
            userId: Guid.NewGuid(),
            familyId: Guid.NewGuid(),
            tokenHash: "hash",
            issuedAt: Now,
            expiresAt: Now.AddDays(14),
            ipAddress: "203.0.113.7",
            userAgent: "Mozilla/5.0");
    }

    [Fact]
    public void A_new_session_is_active()
    {
        Assert.True(NewSession().IsActive(Now));
    }

    [Fact]
    public void A_revoked_session_is_not_active()
    {
        var session = NewSession();

        session.Revoke(Now.AddMinutes(5), SessionRevocationReason.Logout);

        Assert.False(session.IsActive(Now.AddMinutes(6)));
    }

    [Fact]
    public void An_expired_session_is_not_active_even_though_it_was_never_revoked()
    {
        Assert.False(NewSession().IsActive(Now.AddDays(15)));
    }

    [Fact]
    public void Revoking_twice_keeps_the_first_reason_and_instant()
    {
        var session = NewSession();
        session.Revoke(Now.AddMinutes(5), SessionRevocationReason.Rotated);

        session.Revoke(Now.AddMinutes(9), SessionRevocationReason.ReuseDetected);

        Assert.Equal(Now.AddMinutes(5), session.RevokedAt);
        Assert.Equal(SessionRevocationReason.Rotated, session.RevocationReason);
    }
}
```

- [ ] **Step 3: Run them to verify they fail**

Run: `cd backend && dotnet test tests/Maran.Modules.Identity.Tests`
Expected: compile failure — the domain types do not exist.

- [ ] **Step 4: Write the domain**

One type per file, all `sealed`, all state `private set`, EF's parameterless constructor `private`, exactly as `Accounts/Domain/Account.cs` does it.

`Domain/UserRole.cs` — `Admin`, `Customer` (spec §8). `Domain/SessionRevocationReason.cs` — `Logout`, `LogoutAll`, `Rotated`, `ReuseDetected`, `RevokedByAdmin`, `PasswordChanged`.

`Domain/User.cs` carries `Id`, `Username`, `Email`, `PasswordHash`, `Role`, `TotpSecret` (nullable), `IsTotpEnabled`, `CreatedAt`, `LastLoginAt` (nullable), with `EnableTotp(string secret)`, `DisableTotp()`, `ChangePassword(string hash)` and `RecordLogin(DateTimeOffset at)`. `DisableTotp` sets the secret to `null` — a disabled flag beside a live secret is a re-enable away from working, which is not what "disabled" must mean.

`Domain/Session.cs` carries the fields listed under **Produces** plus `IsActive(DateTimeOffset now)` (not revoked and not expired) and `Revoke(DateTimeOffset at, SessionRevocationReason reason)` which is a no-op when already revoked, so the first reason survives.

`Domain/RecoveryCode.cs` — `Id`, `UserId`, `CodeHash`, `ConsumedAt` (nullable), `Consume(DateTimeOffset at)`.

`Domain/AuditEvent.cs` — `Id`, `OccurredAt`, `ActorUserId` (nullable — a failed login has no actor), `ActorUsername`, `Action`, `Subject`, `IpAddress`, `UserAgent`, `Succeeded`, `CorrelationId`. Append-only: no mutating methods at all.

- [ ] **Step 5: Write the persistence**

`IdentityDbContext` mirrors `AccountsDbContext`: `public const string SchemaName = "identity";`, `DbSet` per entity, `HasDefaultSchema(SchemaName)` and one `ApplyConfiguration` per entity. The configurations set PascalCase table names explicitly and add:

- `IX_Users_Username` unique, `IX_Users_Email` unique.
- `IX_Sessions_TokenHash` unique, `IX_Sessions_FamilyId`, `IX_Sessions_UserId`.
- `IX_RecoveryCodes_UserId`.
- `IX_AuditEvents_OccurredAt` descending, `IX_AuditEvents_ActorUserId`.
- `TotpSecret` mapped through `EncryptedStringConverter` — a TOTP secret is a second factor at rest, and `Maran.SharedKernel.Security.EncryptedStringConverter` already exists for exactly this.

Copy `DesignTimeDbContextFactory.cs` from Accounts, changing only the context type.

- [ ] **Step 6: Write the error codes and their text**

The codes are the keys of `Resources/ErrorMessages.resx`; there is no errors class (rules/csharp.md). Flat PascalCase, and the suffix drives the HTTP status (`ApiResultExtensions.MapStatusCode`):

```csharp
```csharp
return Result<LoginResultDto>.Fail(Error.Of(nameof(ErrorMessages.InvalidCredentialsUnauthorized)));
```
```

The full set, each with an entry in all three resx files: `InvalidCredentialsUnauthorized`, `TwoFactorRequiredUnauthorized`, `InvalidTwoFactorCodeUnauthorized`, `RefreshTokenInvalidUnauthorized`, `RefreshTokenReusedUnauthorized`, `SessionNotFound`, `UserNotFound`, `UsernameTaken`, `EmailTaken`, `SetupAlreadyCompletedForbidden`, `SetupTokenInvalidUnauthorized`, `TwoFactorAlreadyEnabledForbidden`, `TwoFactorNotEnabledForbidden`, `PasswordTooWeak`.

Every message is operator-facing English; the resx entries are what a customer sees. **The English resx text for `InvalidCredentialsUnauthorized` must not distinguish an unknown username from a wrong password** — that difference is a user-enumeration oracle. The Russian and Armenian entries say the same thing.

- [ ] **Step 7: Wire the module in**

Add the project to `backend/Maran.sln` and as a `ProjectReference` from `Maran.Host.csproj`; register `new IdentityModule()` in `ModuleRegistry.cs` beside the Accounts one. `IdentityModule.ConfigureServices` mirrors `AccountsModule`: `AddDbContext<IdentityDbContext>` on the `Panel` connection string and the two `ResourceManager` singletons.

- [ ] **Step 8: Generate the migration**

Run: `cd backend && dotnet ef migrations add InitialIdentitySchema --project src/Maran.Modules/Identity --startup-project src/Maran.Host`
Expected: a migration under `Identity/Persistence/Migrations/` creating the `identity` schema and its four tables.

- [ ] **Step 9: Run the gates**

Run: `bash scripts/check-structure.sh && cd backend && dotnet test`
Expected: `STRUCTURE-OK`, all tests green including the new domain tests and `ResourceKeyParityTests`.

- [ ] **Step 10: Checkpoint** — report, with the threat note covering the encrypted TOTP secret and the non-enumerating credential error.

---

### Task 3: Access tokens — JWT issuing and validation

**Files:**
- Modify: `backend/Directory.Packages.props`, `backend/src/Maran.Host/Maran.Host.csproj`
- Create: `backend/src/Maran.Host/Configuration/JwtOptions.cs`
- Create: `backend/src/Maran.Sdk/Contracts/PanelClaimTypes.cs`
- Create: `backend/src/Maran.Modules/Identity/Common/Interfaces/IAccessTokenIssuer.cs`, `Services/JwtAccessTokenIssuer.cs`
- Create: `backend/src/Maran.Host/Extensions/AuthenticationExtensions.cs`, `Authorization/RolePolicies.cs`
- Modify: `backend/src/Maran.Host/Program.cs`, `appsettings.json`, `appsettings.Development.json`, `.env.example`, `installer/panel.env.example`, `installer/steps/60-config.sh`
- Test: `backend/tests/Maran.Modules.Identity.Tests/Services/JwtAccessTokenIssuerTests.cs`, `backend/tests/Maran.Host.Tests/Configuration/JwtOptionsTests.cs`

**Interfaces:**
- Consumes: `User`, `UserRole` (Task 2).
- Produces: `IAccessTokenIssuer.Issue(User user, Guid sessionId)` returning `AccessToken(string Value, DateTimeOffset ExpiresAt)`; claim names on `PanelClaimTypes`; `RolePolicies.AdminOnly` / `RolePolicies.AnyAuthenticated`.

- [ ] **Step 1: Add the packages**

```xml
    <!-- JWT bearer authentication for the panel API. 9.0.19 matches every other Microsoft pin
         here; the 10.x line targets .NET 10 and drops the net9.0 assets. -->
    <PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="9.0.19" />
```

- [ ] **Step 2: Write the failing options test**

`backend/tests/Maran.Host.Tests/Configuration/JwtOptionsTests.cs` — mirrors the existing `SecurityOptionsTests`:

```csharp
using Maran.Host.Configuration;

namespace Maran.Host.Tests.Configuration;

public sealed class JwtOptionsTests
{
    [Fact]
    public void A_signing_key_shorter_than_thirty_two_bytes_is_rejected()
    {
        var options = new JwtOptions { SigningKey = Convert.ToBase64String(new byte[31]) };

        Assert.False(options.HasValidSigningKey());
    }

    [Fact]
    public void A_thirty_two_byte_signing_key_is_accepted()
    {
        var options = new JwtOptions { SigningKey = Convert.ToBase64String(new byte[32]) };

        Assert.True(options.HasValidSigningKey());
    }

    [Fact]
    public void A_signing_key_that_is_not_base64_is_rejected_rather_than_throwing()
    {
        var options = new JwtOptions { SigningKey = "not base64 at all" };

        Assert.False(options.HasValidSigningKey());
    }

    [Fact]
    public void The_access_token_lifetime_defaults_to_fifteen_minutes()
    {
        Assert.Equal(15, new JwtOptions().AccessTokenMinutes);
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `cd backend && dotnet test tests/Maran.Host.Tests --filter JwtOptionsTests`
Expected: compile failure — `JwtOptions` does not exist.

- [ ] **Step 4: Write `JwtOptions`**

Modelled on `SecurityOptions` (same `SectionName` constant, same `[Required]` annotations, same decode-and-measure `HasValidSigningKey()` used by a startup `Validate` callback so a bad key fails the boot, not the first login): `SigningKey` (base64, ≥ 32 bytes), `Issuer` (default `"maran"`), `Audience` (default `"maran-panel"`), `AccessTokenMinutes` (default `15`, per spec §10), `RefreshTokenDays` (default `14`).

Register it in `ConfigurationExtensions.AddPanelConfiguration` alongside the existing options, with `.Validate(o => o.HasValidSigningKey(), "Jwt:SigningKey must be a base64-encoded key of at least 32 bytes.")` and `.ValidateOnStart()`.

- [ ] **Step 5: Write the claim names**

`backend/src/Maran.Sdk/Contracts/PanelClaimTypes.cs` — `UserId` (`"sub"`), `Username` (`"name"`), `Role` (`"role"`), `AccountId` (`"account"`), `SessionId` (`"sid"`). They live in the Sdk because a paid module's authorization handler reads them and must not reference the Host.

- [ ] **Step 6: Write the failing issuer test**

`backend/tests/Maran.Modules.Identity.Tests/Services/JwtAccessTokenIssuerTests.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using Maran.Modules.Identity.Domain;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Services;

public sealed class JwtAccessTokenIssuerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private static JwtAccessTokenIssuer NewIssuer()
    {
        var options = new JwtOptions
        {
            SigningKey = Convert.ToBase64String(new byte[32]),
            Issuer = "maran",
            Audience = "maran-panel",
            AccessTokenMinutes = 15,
        };

        return new JwtAccessTokenIssuer(Options.Create(options), new FakeClock(Now));
    }

    [Fact]
    public void An_issued_token_expires_fifteen_minutes_after_it_was_issued()
    {
        var user = new User(Guid.NewGuid(), "admin", "admin@example.com", "hash", UserRole.Admin, Now);

        var token = NewIssuer().Issue(user, sessionId: Guid.NewGuid());

        Assert.Equal(Now.AddMinutes(15), token.ExpiresAt);
    }

    [Fact]
    public void An_issued_token_carries_the_user_id_username_role_and_session()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var user = new User(userId, "admin", "admin@example.com", "hash", UserRole.Admin, Now);

        var token = NewIssuer().Issue(user, sessionId);

        var claims = new JwtSecurityTokenHandler().ReadJwtToken(token.Value).Claims.ToList();
        Assert.Equal(userId.ToString(), claims.Single(c => c.Type == PanelClaimTypes.UserId).Value);
        Assert.Equal("admin", claims.Single(c => c.Type == PanelClaimTypes.Username).Value);
        Assert.Equal(nameof(UserRole.Admin), claims.Single(c => c.Type == PanelClaimTypes.Role).Value);
        Assert.Equal(sessionId.ToString(), claims.Single(c => c.Type == PanelClaimTypes.SessionId).Value);
    }

    [Fact]
    public void An_issued_token_never_carries_the_password_hash()
    {
        var user = new User(Guid.NewGuid(), "admin", "admin@example.com", "a-very-secret-hash", UserRole.Admin, Now);

        var token = NewIssuer().Issue(user, sessionId: Guid.NewGuid());

        Assert.DoesNotContain("a-very-secret-hash", token.Value, StringComparison.Ordinal);
    }
}
```

Copy `FakeClock` from `backend/tests/Maran.Modules.Accounts.Tests/TestSupport/FakeClock.cs` into this project's `TestSupport/`.

- [ ] **Step 7: Run it to verify it fails, then implement**

Run: `cd backend && dotnet test tests/Maran.Modules.Identity.Tests --filter JwtAccessTokenIssuerTests`
Expected: FAIL — `JwtAccessTokenIssuer` does not exist.

Implement `Services/JwtAccessTokenIssuer.cs` with `JwtSecurityTokenHandler`, `SymmetricSecurityKey` over the decoded signing key, `SecurityAlgorithms.HmacSha256`, and `IClock` for every instant — never `DateTime.UtcNow` (`rules/csharp.md` forbids it). `Common/AccessToken.cs` is the returned record.

- [ ] **Step 8: Add the authentication and authorization wiring**

`Extensions/AuthenticationExtensions.cs` — `AddPanelAuthentication(this IServiceCollection, IConfiguration)` registering `JwtBearer` with `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime` and `ValidateIssuerSigningKey` all true and `ClockSkew = TimeSpan.Zero` (a 15-minute token with the default five-minute skew is a 20-minute token), plus `UsePanelAuthentication(this WebApplication)` calling `UseAuthentication()` then `UseAuthorization()`.

`Authorization/RolePolicies.cs` — `AdminOnly` requiring the `role` claim to equal `nameof(UserRole.Admin)`, `AnyAuthenticated` requiring an authenticated user, and a `Configure(AuthorizationOptions)` that adds both. Set `FallbackPolicy` to `AnyAuthenticated`: an endpoint that forgets its attribute then denies rather than opens.

In `Program.cs`, add `builder.Services.AddPanelAuthentication(builder.Configuration);` after `AddPanelSecurity()` and `app.UsePanelAuthentication();` after `app.UsePanelLocalization()` and before `app.UseRateLimiter()`.

- [ ] **Step 9: Document the new configuration**

Add `Jwt:SigningKey`, `Jwt:Issuer`, `Jwt:Audience`, `Jwt:AccessTokenMinutes`, `Jwt:RefreshTokenDays` to `appsettings.json` (everything except the key), a development key to `appsettings.Development.json` clearly marked as never-for-a-server, and `Jwt__SigningKey` to `.env.example` and `installer/panel.env.example`. `installer/steps/60-config.sh` must generate the key with `openssl rand -base64 32` the same way it generates the encryption key, and — like that one — must **preserve an existing key across re-runs**: rotating it silently logs every user out.

- [ ] **Step 10: Run the gates**

Run: `cd backend && dotnet test`
Expected: all green. Note that with a `FallbackPolicy` in force, `HealthEndpointTests` and `ModulesEndpointTests` will fail until the next step.

- [ ] **Step 11: Open what must stay open**

Add `[AllowAnonymous]` to the health endpoint and the modules catalogue mapping: a readiness probe that requires a token cannot tell systemd the panel is up, and the SPA reads the module catalogue before anyone has logged in. Re-run `dotnet test`; expected all green.

- [ ] **Step 12: Checkpoint** — report with the threat note: what the signing key protects, why `ClockSkew` is zero, and what the fallback policy denies.

---

### Task 4: Sessions and refresh-token rotation

**Files:**
- Create: `backend/src/Maran.Modules/Identity/Common/Interfaces/ISessionService.cs`, `Services/SessionService.cs`, `Common/IssuedSession.cs`
- Create: `backend/src/Maran.Modules/Identity/Common/RefreshTokenHasher.cs`
- Test: `backend/tests/Maran.Modules.Identity.Tests/Services/SessionServiceTests.cs`

**Interfaces:**
- Consumes: `Session`, `SessionRevocationReason`, `IdentityDbContext` (Task 2), `IClock`.
- Produces: `ISessionService` with `Task<IssuedSession> IssueAsync(Guid userId, string ip, string userAgent, CancellationToken)`, `Task<Result<IssuedSession>> RotateAsync(string refreshToken, string ip, string userAgent, CancellationToken)`, `Task RevokeAsync(Guid sessionId, SessionRevocationReason, CancellationToken)`, `Task RevokeAllAsync(Guid userId, SessionRevocationReason, CancellationToken)`. `IssuedSession(Guid SessionId, string RefreshToken, DateTimeOffset ExpiresAt)` — the plaintext refresh token exists only in this record, on its way to the cookie.

- [ ] **Step 1: Write the failing tests**

`backend/tests/Maran.Modules.Identity.Tests/Services/SessionServiceTests.cs`, backed by `Microsoft.EntityFrameworkCore.InMemory` exactly as `CreateAccountCommandHandlerTests` is:

```csharp
    [Fact]
    public async Task Rotating_a_refresh_token_revokes_the_old_session_and_issues_a_new_one()
    {
        var service = NewService();
        var issued = await service.IssueAsync(_userId, "203.0.113.7", "agent", CancellationToken.None);

        var rotated = await service.RotateAsync(issued.RefreshToken, "203.0.113.7", "agent", CancellationToken.None);

        Assert.True(rotated.IsSuccess);
        Assert.NotEqual(issued.RefreshToken, rotated.Value.RefreshToken);
        var old = await _context.Sessions.SingleAsync(s => s.Id == issued.SessionId);
        Assert.Equal(SessionRevocationReason.Rotated, old.RevocationReason);
    }

    [Fact]
    public async Task Presenting_an_already_rotated_refresh_token_revokes_the_whole_family()
    {
        var service = NewService();
        var first = await service.IssueAsync(_userId, "203.0.113.7", "agent", CancellationToken.None);
        var second = await service.RotateAsync(first.RefreshToken, "203.0.113.7", "agent", CancellationToken.None);

        var replay = await service.RotateAsync(first.RefreshToken, "203.0.113.7", "agent", CancellationToken.None);

        Assert.False(replay.IsSuccess);
        Assert.Equal("RefreshTokenReusedUnauthorized", replay.Error!.Code);
        var live = await _context.Sessions.SingleAsync(s => s.Id == second.Value.SessionId);
        Assert.Equal(SessionRevocationReason.ReuseDetected, live.RevocationReason);
    }

    [Fact]
    public async Task An_unknown_refresh_token_is_rejected_without_revoking_anything()
    {
        var service = NewService();
        var issued = await service.IssueAsync(_userId, "203.0.113.7", "agent", CancellationToken.None);

        var result = await service.RotateAsync("a-token-nobody-issued", "203.0.113.7", "agent", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("RefreshTokenInvalidUnauthorized", result.Error!.Code);
        Assert.True((await _context.Sessions.SingleAsync(s => s.Id == issued.SessionId)).IsActive(_now));
    }

    [Fact]
    public async Task An_expired_refresh_token_is_rejected()
    {
        var service = NewService();
        var issued = await service.IssueAsync(_userId, "203.0.113.7", "agent", CancellationToken.None);
        _clock.Advance(TimeSpan.FromDays(15));

        var result = await service.RotateAsync(issued.RefreshToken, "203.0.113.7", "agent", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("RefreshTokenInvalidUnauthorized", result.Error!.Code);
    }

    [Fact]
    public async Task The_database_never_holds_the_plaintext_refresh_token()
    {
        var service = NewService();

        var issued = await service.IssueAsync(_userId, "203.0.113.7", "agent", CancellationToken.None);

        var stored = await _context.Sessions.SingleAsync(s => s.Id == issued.SessionId);
        Assert.NotEqual(issued.RefreshToken, stored.TokenHash);
        Assert.DoesNotContain(issued.RefreshToken, stored.TokenHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoking_all_sessions_leaves_no_active_session_for_the_user()
    {
        var service = NewService();
        await service.IssueAsync(_userId, "203.0.113.7", "agent", CancellationToken.None);
        await service.IssueAsync(_userId, "198.51.100.4", "other", CancellationToken.None);

        await service.RevokeAllAsync(_userId, SessionRevocationReason.LogoutAll, CancellationToken.None);

        Assert.Empty(await _context.Sessions.Where(s => s.UserId == _userId && s.RevokedAt == null).ToListAsync());
    }
```

`FakeClock` gains an `Advance(TimeSpan)` method for the expiry test.

- [ ] **Step 2: Run them to verify they fail**

Run: `cd backend && dotnet test tests/Maran.Modules.Identity.Tests --filter SessionServiceTests`
Expected: compile failure — `SessionService` does not exist.

- [ ] **Step 3: Write the token hasher**

`Common/RefreshTokenHasher.cs` — generates 32 bytes from `RandomNumberGenerator`, base64url-encodes them for the cookie, and stores `SHA-256` of the token string. SHA-256 is correct here and Argon2id is not: a refresh token is 256 bits of machine-generated entropy, not a memorable password, so there is nothing to brute-force and the lookup must be fast enough to run on every refresh. `rules/security.md` item 9 forbids SHA-1 and MD5, not SHA-256.

- [ ] **Step 4: Implement `SessionService`**

`RotateAsync` is the whole security story, in this order:

1. Hash the presented token and find the session by hash. Not found → `Error.Of(nameof(ErrorMessages.RefreshTokenInvalidUnauthorized))`.
2. Found but already revoked → **reuse detected**: revoke every session sharing its `FamilyId` with `ReuseDetected` and return `Error.Of(nameof(ErrorMessages.RefreshTokenReusedUnauthorized))`. This is what makes a stolen cookie a one-use weapon: the moment either the thief or the legitimate user presents the rotated token, the entire chain dies.
3. Found but expired → `RefreshTokenInvalid()`.
4. Otherwise revoke it with `Rotated` and insert a new session carrying the **same `FamilyId`**.

Every instant comes from `IClock`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/Maran.Modules.Identity.Tests --filter SessionServiceTests`
Expected: PASS.

- [ ] **Step 6: Checkpoint** — report with the threat note on reuse detection and on why the hash is SHA-256.

---

### Task 5: The audit journal

**Files:**
- Create: `backend/src/Maran.Sdk/Interfaces/IAuditWriter.cs`, `backend/src/Maran.Sdk/Contracts/AuditEntry.cs`, `backend/src/Maran.Sdk/Contracts/AuditActions.cs`
- Create: `backend/src/Maran.Modules/Identity/Services/DatabaseAuditWriter.cs`
- Create: `backend/src/Maran.Modules/Identity/Queries/ListAuditEvents/{ListAuditEventsQuery,ListAuditEventsQueryHandler}.cs`, `Common/AuditEventDto.cs`
- Modify: `backend/src/Maran.Modules/Identity/IdentityModule.cs`
- Test: `backend/tests/Maran.Modules.Identity.Tests/Services/DatabaseAuditWriterTests.cs`, `Queries/ListAuditEvents/ListAuditEventsQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `AuditEvent`, `IdentityDbContext` (Task 2), `IClock`, `ICorrelationIdAccessor`.
- Produces: `IAuditWriter.WriteAsync(AuditEntry entry, CancellationToken)`; `AuditEntry(Guid? ActorUserId, string ActorUsername, string Action, string Subject, string IpAddress, string UserAgent, bool Succeeded)`; `AuditActions.LoginSucceeded` / `LoginFailed` / `LoggedOut` / `LoggedOutEverywhere` / `SessionRevoked` / `TwoFactorEnabled` / `TwoFactorDisabled` / `RecoveryCodeUsed` / `PasswordChanged` / `AdministratorCreated`.

The contract lives in `Maran.Sdk` because every future module writes to this journal and none of them may reference `Maran.Modules.Identity`. The implementation lives in Identity because Identity owns the table.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async Task A_written_entry_is_stamped_with_the_clock_and_the_correlation_id()
    {
        await NewWriter().WriteAsync(
            new AuditEntry(_actorId, "admin", AuditActions.LoginSucceeded, "admin", "203.0.113.7", "agent", Succeeded: true),
            CancellationToken.None);

        var stored = await _context.AuditEvents.SingleAsync();
        Assert.Equal(_now, stored.OccurredAt);
        Assert.Equal("correlation-1", stored.CorrelationId);
    }

    [Fact]
    public async Task A_failed_login_is_recorded_with_no_actor_but_with_the_attempted_username()
    {
        await NewWriter().WriteAsync(
            new AuditEntry(null, "nosuchuser", AuditActions.LoginFailed, "nosuchuser", "203.0.113.7", "agent", Succeeded: false),
            CancellationToken.None);

        var stored = await _context.AuditEvents.SingleAsync();
        Assert.Null(stored.ActorUserId);
        Assert.Equal("nosuchuser", stored.ActorUsername);
        Assert.False(stored.Succeeded);
    }

    [Fact]
    public async Task Listing_returns_the_most_recent_events_first()
    {
        var handler = NewHandler();
        await WriteAt(_now, "first");
        await WriteAt(_now.AddMinutes(1), "second");

        var result = await handler.HandleAsync(new ListAuditEventsQuery(Limit: 50), CancellationToken.None);

        Assert.Equal(["second", "first"], result.Value.Select(e => e.Subject));
    }
```

- [ ] **Step 2: Run them to verify they fail, then implement**

Run: `cd backend && dotnet test tests/Maran.Modules.Identity.Tests --filter Audit`
Expected: compile failure.

`DatabaseAuditWriter` takes `IdentityDbContext`, `IClock` and `ICorrelationIdAccessor`, constructs the `AuditEvent` and saves it. Nothing in it can update or delete a row — the journal is append-only, and the absence of those methods is the enforcement.

`ListAuditEventsQueryHandler` orders by `OccurredAt` descending and takes `Limit` (validated by a `ListAuditEventsQueryValidator` to 1..500, so a caller cannot ask for the whole table).

- [ ] **Step 3: Register the writer**

In `IdentityModule.ConfigureServices`: `services.AddScoped<IAuditWriter, DatabaseAuditWriter>();`

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/Maran.Modules.Identity.Tests`
Expected: PASS.

- [ ] **Step 5: Checkpoint** — report, noting that no audit row can carry a password, a token or a recovery code because `AuditEntry` has no field one could travel in.

---

### Task 6: Login

**Files:**
- Create: `backend/src/Maran.Modules/Identity/Commands/Login/{LoginCommand,LoginCommandHandler,LoginCommandValidator}.cs`
- Create: `backend/src/Maran.Modules/Identity/Common/{LoginResultDto,AuthenticatedUserDto}.cs`
- Create: `backend/src/Maran.Modules/Identity/Controllers/AuthController.cs`, `Controllers/Requests/LoginRequest.cs`
- Create: `backend/src/Maran.Modules/Identity/Common/RefreshCookie.cs`
- Test: `backend/tests/Maran.Modules.Identity.Tests/Commands/Login/{LoginCommandHandlerTests,LoginCommandValidatorTests}.cs`; `backend/tests/Maran.Host.IntegrationTests/AuthEndpointTests.cs`

**Interfaces:**
- Consumes: `IPasswordHasher` (1), `User` (2), `IAccessTokenIssuer` (3), `ISessionService` (4), `IAuditWriter` (5).
- Produces: `POST /api/v1/auth/login` returning `LoginResultDto(string? AccessToken, DateTimeOffset? ExpiresAt, bool TwoFactorRequired, AuthenticatedUserDto? User)` and setting the refresh cookie; `RefreshCookie.Name = "maran_refresh"` with the attributes every endpoint sets it through.

- [ ] **Step 1: Write the failing handler tests**

```csharp
    [Fact]
    public async Task Logging_in_with_the_right_password_returns_an_access_token()
    {
        var result = await NewHandler().HandleAsync(new LoginCommand("admin", "correct horse battery staple", "203.0.113.7", "agent"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.AccessToken));
    }

    [Fact]
    public async Task Logging_in_with_a_wrong_password_fails_with_the_credentials_error()
    {
        var result = await NewHandler().HandleAsync(new LoginCommand("admin", "wrong", "203.0.113.7", "agent"), CancellationToken.None);

        Assert.Equal("InvalidCredentialsUnauthorized", result.Error!.Code);
    }

    [Fact]
    public async Task Logging_in_as_an_unknown_user_fails_with_the_same_error_as_a_wrong_password()
    {
        var unknown = await NewHandler().HandleAsync(new LoginCommand("nosuchuser", "wrong", "203.0.113.7", "agent"), CancellationToken.None);

        Assert.Equal("InvalidCredentialsUnauthorized", unknown.Error!.Code);
    }

    [Fact]
    public async Task A_user_with_two_factor_enabled_gets_no_access_token_yet()
    {
        var result = await NewHandlerWithTotpUser().HandleAsync(new LoginCommand("admin", "correct horse battery staple", "203.0.113.7", "agent"), CancellationToken.None);

        Assert.True(result.Value.TwoFactorRequired);
        Assert.Null(result.Value.AccessToken);
    }

    [Fact]
    public async Task A_successful_login_writes_an_audit_event()
    {
        await NewHandler().HandleAsync(new LoginCommand("admin", "correct horse battery staple", "203.0.113.7", "agent"), CancellationToken.None);

        Assert.Equal(AuditActions.LoginSucceeded, _audit.Written.Single().Action);
    }

    [Fact]
    public async Task A_failed_login_writes_an_audit_event_that_does_not_contain_the_attempted_password()
    {
        await NewHandler().HandleAsync(new LoginCommand("admin", "hunter2", "203.0.113.7", "agent"), CancellationToken.None);

        var entry = _audit.Written.Single();
        Assert.Equal(AuditActions.LoginFailed, entry.Action);
        Assert.DoesNotContain("hunter2", entry.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_login_by_a_user_whose_hash_uses_weaker_parameters_upgrades_the_stored_hash()
    {
        var handler = NewHandlerWithLegacyHash();

        await handler.HandleAsync(new LoginCommand("admin", "correct horse battery staple", "203.0.113.7", "agent"), CancellationToken.None);

        Assert.False(_hasher.NeedsRehash((await _context.Users.SingleAsync()).PasswordHash));
    }
```

`_audit` is a `RecordingAuditWriter` test double in `TestSupport/`, holding what it was asked to write.

- [ ] **Step 2: Run them to verify they fail**

Run: `cd backend && dotnet test tests/Maran.Modules.Identity.Tests --filter LoginCommandHandlerTests`
Expected: compile failure.

- [ ] **Step 3: Implement the handler**

The order matters:

1. Look up the user by username. **If not found, still run the password hasher against a fixed dummy hash** before returning `InvalidCredentials()` — otherwise the response time tells an attacker which usernames exist, and the identical error message would have been for nothing.
2. Verify the password; on failure write the `LoginFailed` audit entry and return `InvalidCredentials()`.
3. If `NeedsRehash`, store a fresh hash.
4. If `user.IsTotpEnabled`, return `LoginResultDto` with `TwoFactorRequired: true` and **no session and no token** — the second factor is verified by Task 8's endpoint before anything is issued.
5. Otherwise issue the session and the access token, `user.RecordLogin(clock.UtcNow)`, write `LoginSucceeded`, and return.

- [ ] **Step 4: Write the validator**

`LoginCommandValidator` — username 1..64, password 1..256 (a length cap so a megabyte password cannot turn Argon2id into a denial of service). Both `NotEmpty`.

- [ ] **Step 5: Write the controller**

```csharp
[Route("api/v1/auth")]
[Tags("Auth")]
[Produces("application/json")]
[AllowAnonymous]
public sealed class AuthController : BaseApiController
```

`POST login` is decorated `[EnableRateLimiting(LoginRateLimitPolicy.Name)]` (`rules/security.md`: rate limiting is mandatory on authentication). It binds `LoginRequest`, adds the caller's IP and user agent, dispatches the command, and on success sets the refresh cookie through `RefreshCookie.Append(Response, issued)`:

```csharp
    /// <summary>
    /// Writes the refresh token as a cookie the SPA's JavaScript can never read. HttpOnly stops
    /// an XSS from stealing it, Secure keeps it off plaintext HTTP, SameSite=Strict means no
    /// cross-site request can carry it, and the narrow Path means it is sent only to the two
    /// endpoints that rotate or revoke it — not on every API call (spec §10).
    /// </summary>
    public static void Append(HttpResponse response, IssuedSession session)
    {
        response.Cookies.Append(Name, session.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth",
            Expires = session.ExpiresAt,
        });
    }
```

`LoginRateLimitPolicy.BuildPartitionKey` reads the attempted username from the query string. Have the SPA call `POST /api/v1/auth/login?username=<name>` with the same name it puts in the body, and have the handler use the **body** value as the credential — the query value exists only to partition the limiter. Document that in the controller's doc comment.

- [ ] **Step 6: Write the integration test**

`backend/tests/Maran.Host.IntegrationTests/AuthEndpointTests.cs`, on the existing Testcontainers-PostgreSQL fixture: seed a user, `POST /api/v1/auth/login`, assert 200, assert the response body carries an access token, and assert the `Set-Cookie` header contains `HttpOnly`, `Secure` and `SameSite=Strict`. A second test asserts 401 and no `Set-Cookie` for a wrong password.

- [ ] **Step 7: Run the gates**

Run: `cd backend && dotnet test`
Expected: all green.

- [ ] **Step 8: Checkpoint** — report with the threat note: enumeration resistance (identical error, dummy hash on the miss), the password length cap, the cookie attributes, and what the rate limiter does and does not bound.

---

### Task 7: Refresh, logout, and session management

**Files:**
- Create: `backend/src/Maran.Modules/Identity/Commands/{RefreshSession,Logout,LogoutEverywhere,RevokeSession}/…`
- Create: `backend/src/Maran.Modules/Identity/Queries/ListSessions/{ListSessionsQuery,ListSessionsQueryHandler}.cs`, `Common/SessionDto.cs`
- Create: `backend/src/Maran.Modules/Identity/Controllers/SessionsController.cs`
- Modify: `backend/src/Maran.Modules/Identity/Controllers/AuthController.cs`
- Test: handler tests per command; `backend/tests/Maran.Host.IntegrationTests/SessionEndpointTests.cs`

**Interfaces:**
- Consumes: `ISessionService` (4), `IAuditWriter` (5), `RefreshCookie` (6), `ICurrentUser` (Task 9 supplies the real one; until then the tests inject a fake).
- Produces: `POST /api/v1/auth/refresh`, `POST /api/v1/auth/logout`, `POST /api/v1/auth/logout-all`, `GET /api/v1/sessions`, `DELETE /api/v1/sessions/{id}`.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async Task Refreshing_with_a_valid_cookie_returns_a_new_access_token_and_a_new_cookie()

    [Fact]
    public async Task Refreshing_with_no_cookie_at_all_returns_401_rather_than_500()

    [Fact]
    public async Task Refreshing_twice_with_the_same_cookie_returns_401_and_kills_the_session_family()

    [Fact]
    public async Task Logging_out_revokes_only_the_current_session()

    [Fact]
    public async Task Logging_out_everywhere_revokes_every_session_of_the_user()

    [Fact]
    public async Task Listing_sessions_returns_only_the_callers_own_sessions()

    [Fact]
    public async Task Listing_sessions_never_returns_a_token_hash()

    [Fact]
    public async Task A_customer_revoking_another_users_session_gets_404_rather_than_403()
```

That last one is the IDOR test `rules/testing.md` requires on every tenant-scoped endpoint, and 404 is the mandated answer: 403 would confirm the session id exists.

- [ ] **Step 2: Run them to verify they fail, then implement**

Run: `cd backend && dotnet test tests/Maran.Modules.Identity.Tests --filter Session`
Expected: FAIL.

`RefreshSessionCommandHandler` calls `ISessionService.RotateAsync` and, on success, issues a fresh access token for the session's user. `LogoutCommandHandler` revokes the session named by the `sid` claim. `LogoutEverywhereCommandHandler` calls `RevokeAllAsync`. Each writes its audit entry.

`ListSessionsQueryHandler` filters by `ICurrentUser.UserId` — never by a caller-supplied id — and maps to `SessionDto(Id, IssuedAt, ExpiresAt, IpAddress, UserAgent, IsCurrent)`. `SessionDto` has no token field of any kind; that is why the "never returns a token hash" test can be a compile-time certainty as well as a runtime one.

`RevokeSessionCommandHandler` loads the session **scoped to the caller's user id** and returns `Error.Of(nameof(ErrorMessages.SessionNotFound))` when the filter excludes it. An admin may additionally revoke any user's session; that path checks `ICurrentUser.IsAdmin` explicitly.

- [ ] **Step 3: Clear the cookie on logout**

Both logout endpoints call `RefreshCookie.Delete(Response)`, which appends the same cookie name with an expiry in the past and the identical `Path`, `Secure`, `HttpOnly` and `SameSite` attributes — a cookie deleted with different attributes is not deleted at all.

- [ ] **Step 4: Run the gates**

Run: `cd backend && dotnet test`
Expected: all green.

- [ ] **Step 5: Checkpoint** — report with the threat note on reuse detection reaching the HTTP surface, and on the 404-not-403 choice.

---

### Task 8: TOTP two-factor authentication and recovery codes

**Files:**
- Modify: `backend/Directory.Packages.props`
- Create: `backend/src/Maran.Modules/Identity/Common/Interfaces/{ITotpService,IRecoveryCodeService}.cs`, `Services/{TotpService,RecoveryCodeService}.cs`
- Create: `backend/src/Maran.Modules/Identity/Commands/{BeginTotpEnrolment,ConfirmTotpEnrolment,DisableTotp,VerifyTwoFactor}/…`
- Modify: `backend/src/Maran.Modules/Identity/Controllers/AuthController.cs`
- Test: `backend/tests/Maran.Modules.Identity.Tests/Services/{TotpServiceTests,RecoveryCodeServiceTests}.cs` + a handler test per command

**Interfaces:**
- Consumes: `User` (2), `IAccessTokenIssuer` (3), `ISessionService` (4), `IAuditWriter` (5), `IPasswordHasher` (1).
- Produces: `POST /api/v1/auth/two-factor` (completes a login), `POST /api/v1/auth/two-factor/enrol`, `POST /api/v1/auth/two-factor/confirm`, `POST /api/v1/auth/two-factor/disable`.

- [ ] **Step 1: Add the package**

```xml
    <!-- RFC 6238 TOTP. Otp.NET is a small, dependency-free implementation of exactly the
         algorithm; writing our own would violate rules/security.md item 9 ("no home-grown
         crypto") for no gain. -->
    <PackageVersion Include="Otp.NET" Version="1.4.1" />
```

- [ ] **Step 2: Write the failing service tests**

```csharp
    [Fact]
    public void A_generated_secret_is_base32_and_at_least_twenty_bytes()

    [Fact]
    public void The_code_for_the_current_window_verifies()

    [Fact]
    public void A_code_from_three_windows_ago_does_not_verify()

    [Fact]
    public void A_code_from_the_immediately_previous_window_still_verifies()

    [Fact]
    public void A_code_that_was_already_used_does_not_verify_a_second_time()

    [Fact]
    public void Ten_recovery_codes_are_generated_and_none_repeats()

    [Fact]
    public void A_recovery_code_verifies_once_and_then_never_again()

    [Fact]
    public void The_database_never_holds_a_plaintext_recovery_code()
```

The "immediately previous window" test is the clock-skew allowance — one step back, no more. The "already used" test is replay protection: `TotpService` records the last accepted window per user so a code observed on the wire cannot be replayed inside its own 30 seconds.

- [ ] **Step 3: Run them to verify they fail, then implement**

`TotpService` wraps `OtpNet.Totp` with `VerificationWindow(previous: 1, future: 0)` — accepting a future window would mean accepting a code the user cannot yet see, which only helps an attacker with a slow clock. Recovery codes are 10 codes of 10 base32 characters, hashed with `IPasswordHasher` (these *are* human-typed secrets, so Argon2id is right here where it was wrong for refresh tokens) and stored one row each.

Enrolment is two steps by design: `enrol` returns the secret and its `otpauth://` URI **without** enabling anything, and `confirm` enables it only after the user proves they can produce a valid code — so a user who scans the QR into nothing does not lock themselves out.

`VerifyTwoFactorCommandHandler` accepts either a TOTP code or a recovery code, and only then issues the session and access token that `LoginCommandHandler` deliberately withheld. It writes `TwoFactorEnabled`, `TwoFactorDisabled` or `RecoveryCodeUsed` as appropriate.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test tests/Maran.Modules.Identity.Tests`
Expected: PASS.

- [ ] **Step 5: Checkpoint** — report with the threat note on replay protection, the one-window skew, and why recovery codes are Argon2id-hashed.

---

### Task 9: The real `ICurrentUser`, CSRF, and closing the open endpoints

**Files:**
- Create: `backend/src/Maran.Host/Security/HttpContextCurrentUser.cs`
- Delete: `backend/src/Maran.Host/Security/UnauthenticatedCurrentUser.cs`
- Create: `backend/src/Maran.Host/Middleware/CsrfHeaderMiddleware.cs`, `Extensions/CsrfHeaderMiddlewareExtensions.cs`
- Create: `backend/src/Maran.Host/Extensions/SecurityHeadersExtensions.cs`
- Modify: `backend/src/Maran.Host/Extensions/SecurityExtensions.cs`, `Program.cs`, `backend/src/Maran.Modules/Accounts/Controllers/AccountsController.cs`
- Test: `backend/tests/Maran.Host.Tests/Security/HttpContextCurrentUserTests.cs`, `Middleware/CsrfHeaderMiddlewareTests.cs`

**Interfaces:**
- Consumes: `PanelClaimTypes` (3).
- Produces: `ICurrentUser` reading the request's claims; `CsrfHeaderMiddleware` requiring `X-Maran-Request: 1` on every cookie-bearing state change.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void An_unauthenticated_request_yields_an_empty_user_id_and_no_admin_rights()

    [Fact]
    public void An_admin_token_yields_is_admin_true_and_a_null_account_id()

    [Fact]
    public void A_customer_token_yields_the_account_id_from_its_claim()

    [Fact]
    public void A_role_claim_the_panel_does_not_know_is_not_treated_as_admin()
```

and for the middleware:

```csharp
    [Fact]
    public async Task A_post_carrying_the_refresh_cookie_without_the_custom_header_is_rejected_with_403()

    [Fact]
    public async Task A_post_carrying_the_custom_header_passes_through()

    [Fact]
    public async Task A_get_is_never_rejected_for_a_missing_header()

    [Fact]
    public async Task A_post_with_a_bearer_token_and_no_cookie_passes_without_the_header()
```

That last one states the boundary precisely: CSRF exists because browsers attach cookies automatically. A request authenticated purely by an `Authorization` header cannot be forged cross-site, so requiring the header there would only break API clients.

- [ ] **Step 2: Run them to verify they fail, then implement**

`HttpContextCurrentUser` takes `IHttpContextAccessor`, reads `PanelClaimTypes`, and returns the same least-privileged answers the deleted stub did when no claim is present — `Guid.Empty`, `null`, `false`. Its doc comment carries over the reasoning from `UnauthenticatedCurrentUser`: unknown means denied.

Register it in `SecurityExtensions.AddPanelSecurity` (replacing the stub registration) and add `services.AddHttpContextAccessor()`.

`SecurityHeadersExtensions.UseSecurityHeaders` sets `Content-Security-Policy: default-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, and `Permissions-Policy: geolocation=(), camera=(), microphone=()`. It runs first in the pipeline so even an error response carries them.

- [ ] **Step 3: Close the endpoints that were open**

In `AccountsController`, replace the deferral remark with `[Authorize(Policy = RolePolicies.AdminOnly)]` and delete the paragraph explaining why authorization was absent — it no longer is. Managing hosting accounts is an administrator action (spec §8); a customer's own view of their account arrives with the accounts-lifecycle plan.

- [ ] **Step 4: Run the gates**

Run: `bash scripts/check-structure.sh && cd backend && dotnet test`
Expected: `STRUCTURE-OK` and all green. `AccountsEndpointTests` must now authenticate; update it to obtain a token through the login endpoint rather than to assert anonymous access, and add the anonymous case as an explicit 401 assertion.

- [ ] **Step 5: Checkpoint** — report with the threat note on the fallback policy, the CSP, and the exact CSRF boundary.

---

### Task 10: First-administrator setup from the installer's one-time token

**Files:**
- Create: `backend/src/Maran.Host/Configuration/SetupOptions.cs`
- Create: `backend/src/Maran.Modules/Identity/Commands/CompleteSetup/{CompleteSetupCommand,CompleteSetupCommandHandler,CompleteSetupCommandValidator}.cs`
- Create: `backend/src/Maran.Modules/Identity/Queries/GetSetupState/{GetSetupStateQuery,GetSetupStateQueryHandler}.cs`, `Common/SetupStateDto.cs`
- Create: `backend/src/Maran.Modules/Identity/Controllers/SetupController.cs`, `Controllers/Requests/CompleteSetupRequest.cs`
- Test: handler and validator tests; `backend/tests/Maran.Host.IntegrationTests/SetupEndpointTests.cs`

**Interfaces:**
- Consumes: `IPasswordHasher` (1), `User` (2), `IAuditWriter` (5).
- Produces: `GET /api/v1/setup/state` → `SetupStateDto(bool IsComplete)`, `POST /api/v1/setup` creating the first administrator.

The installer already writes `Setup__Token` into `/etc/maran/panel.env` and prints the one-time URL to the operator's terminal (never to the install log). This task is the endpoint that token was always for.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async Task Completing_setup_on_an_empty_panel_creates_an_administrator()

    [Fact]
    public async Task Completing_setup_with_a_wrong_token_fails_and_creates_nobody()

    [Fact]
    public async Task Completing_setup_when_a_user_already_exists_is_refused_even_with_the_right_token()

    [Fact]
    public async Task The_setup_token_is_compared_in_constant_time()

    [Fact]
    public async Task A_weak_password_is_refused_before_any_user_is_created()

    [Fact]
    public async Task Completing_setup_writes_an_audit_event_that_does_not_contain_the_token()

    [Fact]
    public async Task The_setup_state_endpoint_reports_complete_once_any_user_exists()
```

The third is the one that matters most: the token is long-lived on disk, so "any user exists" — not "the token was already used" — is what closes this door. An operator who never deletes the token file still cannot be attacked through it once they have logged in once.

- [ ] **Step 2: Run them to verify they fail, then implement**

Run: `cd backend && dotnet test tests/Maran.Modules.Identity.Tests --filter Setup`
Expected: FAIL.

`SetupOptions` binds `Setup:Token` and is registered like `SecurityOptions`, but is **not** `[Required]` — a panel whose setup is finished has no token, and demanding one would refuse to boot.

`CompleteSetupCommandHandler`: if `Users.AnyAsync()` → `Error.Of(nameof(ErrorMessages.SetupAlreadyCompletedForbidden))`. Compare the supplied token to the configured one with `CryptographicOperations.FixedTimeEquals` over the UTF-8 bytes; a mismatch → `Error.Of(nameof(ErrorMessages.SetupTokenInvalidUnauthorized))`. Then hash the password, create the `Admin` user, and write `AuditActions.AdministratorCreated`.

`CompleteSetupCommandValidator` enforces the password policy: at least 12 characters, and not equal to the username. The message for a rejection is `PasswordTooWeak`, whose resx text states the rule in the user's language.

- [ ] **Step 3: Run the tests to verify they pass, then run the gates**

Run: `cd backend && dotnet test`
Expected: all green.

- [ ] **Step 4: Checkpoint** — report with the threat note: what the token grants, why "any user exists" is the gate, and the constant-time comparison.

---

### Task 11: The SPA's auth layer

**Files:**
- Create: `frontend/src/types/auth.ts`, `frontend/src/composables/apis/useAuthApi.ts`, `frontend/src/stores/auth.ts`, `frontend/src/router/authGuard.ts`
- Modify: `frontend/src/composables/useApi.ts`, `frontend/src/router/index.ts`, `frontend/src/locales/{en,ru,hy}/app.json`
- Test: covered by Task 13's Playwright specs (the SPA has no unit runner by design)

**Interfaces:**
- Consumes: every endpoint from Tasks 6–10.
- Produces: `useAuthStore()` exposing `isAuthenticated`, `user`, `login`, `verifyTwoFactor`, `logout`, `logoutEverywhere`, `refresh`, `loadSessions`, `revokeSession`; `createAuthGuard()`.

- [ ] **Step 1: Extend the low-level client**

`useApi` gains three things and nothing else:

1. `credentials: 'include'` on every request, so the refresh cookie travels.
2. The `X-Maran-Request: 1` header on every request, satisfying Task 9's CSRF middleware.
3. An `Authorization: Bearer <token>` header when the auth store holds a token.

The access token lives **only in the auth store's `ref`** — never `localStorage`, never a non-httpOnly cookie. A token in `localStorage` is readable by any successful XSS; one in a closure is not persisted anywhere an attacker can reach without already running in the page. The cost is a refresh on every page reload, which is what the refresh cookie is for.

- [ ] **Step 2: Add the 401 refresh-and-retry**

On a 401 from any call other than `/api/v1/auth/refresh` itself, the client calls refresh once and replays the original request. A second 401 clears the store and the guard sends the user to `/login`. Concurrent 401s share one in-flight refresh promise — without that, ten parallel calls after a page reload each rotate the refresh token, and nine of them present a token their sibling has already rotated, which Task 4 correctly treats as reuse and answers by killing the session.

- [ ] **Step 3: Write the store**

`stores/auth.ts` is the only caller of `useAuthApi` (`rules/vue.md`). It holds `accessToken`, `user`, `twoFactorPending`, `sessions`, `loading` and `errorMessage`, and stores the backend's already-localized `title`/`detail` verbatim on failure — it never composes error text of its own.

- [ ] **Step 4: Write the guard and the routes**

`router/authGuard.ts` sends an unauthenticated navigation to `/login` (preserving the intended path in the query), an authenticated one away from `/login`, and every navigation to `/setup` when `GET /api/v1/setup/state` reports the panel has no users yet. It runs **before** `createModuleAccessGuard` — asking whether a module is licensed for an anonymous visitor is a question with no meaning.

New routes on `AuthLayout`: `/login`, `/login/two-factor`, `/setup`. New routes on `DefaultLayout`: `/settings/sessions`, `/settings/two-factor`.

- [ ] **Step 5: Add the locale keys**

Every string in all three locale files, with real Russian and Armenian — never English copied into `ru`/`hy` (`ResourceKeyParityTests`' frontend counterpart is the reviewer). Every `label`, `aria-label`, `placeholder` and `alt` on the new screens comes from these keys.

- [ ] **Step 6: Run the gates**

Run: `cd frontend && npm run lint && npm run typecheck && npm run build`
Expected: all clean.

- [ ] **Step 7: Checkpoint** — report with the threat note on where the access token lives and why.

---

### Task 12: The five auth screens

**Files:**
- Create: `frontend/src/pages/auth/{LoginPage,TwoFactorPage,SetupPage}.vue`
- Create: `frontend/src/pages/settings/{SessionsPage,TwoFactorSettingsPage}.vue`
- Modify: `frontend/src/composables/useNavigation.ts`, `frontend/src/components/shell/ShellUserBlock.vue`
- Create as needed: `frontend/src/components/ui/UiPasswordInput.vue`

**Interfaces:**
- Consumes: `useAuthStore` (11).
- Produces: the screens; a logout action on the user block.

- [ ] **Step 1: Build the login page**

`UiForm` (always `novalidate`), `UiInput` for the username, the new `UiPasswordInput` for the password, `UiButton` for the submit, `UiAlert` for the backend's error text rendered verbatim. No raw `<form>`, `<input>` or `<button>` anywhere (`rules/vue.md`). On a `twoFactorRequired` response it routes to `/login/two-factor`.

- [ ] **Step 2: Build the two-factor page**

A six-digit code field plus a "use a recovery code instead" toggle that swaps the field's label, placeholder and `aria-label` — all three from the locale files, all three different from the TOTP variants.

- [ ] **Step 3: Build the setup page**

Token field (prefilled from the `?token=` query the installer's one-time URL carries), username, email, password and confirmation, with a strength indicator (spec §10) computed from length and character classes. The indicator is advice; the server's validator is the authority, and a mismatch between them is a bug in the client, never a reason to submit.

- [ ] **Step 4: Build the sessions page**

`UiTable` listing each session's device, IP, issued-at (through `date-fns`, as `utils/formatDate.ts` already does) and a "this device" marker, with a revoke action per row and one "sign out everywhere" button. Confirm before revoking — the user may be looking at their own current session.

- [ ] **Step 5: Build the two-factor settings page**

Enrol shows the `otpauth://` URI as a QR code and as copyable text (a QR the user cannot photograph is useless on the machine they are sitting at), takes a confirming code, and then shows the ten recovery codes **once**, with a copy action and a clear statement that they will not be shown again.

- [ ] **Step 6: Add logout to the shell**

`ShellUserBlock` gains a logout item using the existing `UiDropdownItem`, and shows the authenticated user's name from the store instead of its placeholder.

- [ ] **Step 7: Run the gates**

Run: `cd frontend && npm run lint && npm run typecheck && npm run build`
Expected: all clean.

- [ ] **Step 8: Checkpoint** — report; include a screenshot pass against the design canvas (`docs/design/ServerPanel.dc.html`) for the login and setup screens.

---

### Task 13: End-to-end coverage

**Files:**
- Create: `frontend/e2e/auth-login.spec.ts`, `auth-two-factor.spec.ts`, `auth-setup.spec.ts`, `auth-sessions.spec.ts`, `auth-guard.spec.ts`
- Modify: `frontend/e2e/fixtures/` — a shared authenticated-state fixture
- Modify: every existing spec that assumed an unauthenticated panel

**Interfaces:**
- Consumes: everything above.

- [ ] **Step 1: Write the authenticated fixture**

One fixture that stubs `POST /api/v1/auth/login` and `POST /api/v1/auth/refresh` and seeds the store, so the 31 existing specs — which know nothing about login — keep testing what they were written to test rather than each growing a login preamble.

- [ ] **Step 2: Write the flow specs**

- `auth-login.spec.ts`: a good password lands on the dashboard; a bad one shows the backend's message and stays put; the form is reachable and submittable by keyboard alone.
- `auth-two-factor.spec.ts`: a 2FA user is sent to the code screen and completes with a code, and separately with a recovery code.
- `auth-setup.spec.ts`: a panel reporting `isComplete: false` redirects every route to `/setup`; completing it lands on the dashboard.
- `auth-guard.spec.ts`: an anonymous visit to `/accounts` redirects to `/login` and returns to `/accounts` after login.
- `auth-sessions.spec.ts`: the list renders, revoking a row removes it, "sign out everywhere" returns to `/login`.

- [ ] **Step 3: Run everything**

Run: `bash scripts/preflight.sh && bash scripts/check-structure.sh`
Run: `cd backend && dotnet test`
Run: `cd frontend && npm run lint && npm run typecheck && npm run build && npx playwright test`
Expected: every gate green.

- [ ] **Step 4: Final checkpoint** — report the whole plan complete, with the consolidated threat note `rules/security.md` requires for a change of this kind, and the list of what deliberately did NOT ship (below).

---

## Deliberately out of scope

Named here so nobody mistakes their absence for an oversight:

- **API keys for hostpanel** (spec §10, §12) — they belong with the Provisioning API in roadmap item 7, and building them now would mean guessing at scopes no consumer has yet described.
- **nftables auto-banning of persistent brute force** (spec §10) — the rate limiter bounds attempts today; pushing bans down to the firewall needs the agent's `FirewallService`, which is roadmap item 5.
- **Forced 2FA for administrators** (spec §10 "enableable") — the enrolment machinery ships here; the policy switch that requires it arrives with the settings module.
- **IP allowlist for administrator login** (spec §10, explicitly optional).
- **Password reset by email** — the panel has no SMTP configuration until Monitoring (roadmap item 5). An administrator who loses their password recovers through a recovery code, or through the CLI, which is roadmap item 8.
- **The accounts lifecycle** — see the scope note at the top.

## Self-review

**Spec coverage (§10, the section this plan implements).** JWT 15 minutes ✅ Task 3. Refresh with rotation in an httpOnly/SameSite=Strict cookie ✅ Tasks 4, 6. Sessions in PostgreSQL, visible and revocable, including by an admin ✅ Tasks 4, 7. Reuse of a spent refresh token revokes the chain ✅ Task 4. TOTP with recovery codes ✅ Task 8. Argon2id ✅ Task 1. Strength indicator ✅ Task 12; configurable policy — partially: the rule is enforced in `CompleteSetupCommandValidator` but is not yet operator-configurable, which is listed above under the settings module. Anti-brute-force rate limiting on IP+login ✅ Task 6 (the existing `LoginRateLimitPolicy` finally gets its endpoint); nftables escalation — out of scope, listed. HTTPS-only and port 8443 — installer concerns, already shipped in Plan 1. API keys — out of scope, listed. Strict CSP, security headers, CSRF as SameSite plus a mandatory custom header ✅ Task 9. Append-only audit of logins and mutations with an admin screen ✅ Task 5 for the journal and the query; the admin *screen* is deliberately not in Task 12 — it belongs with the audit UI in the settings module, and the endpoint it needs exists.

**Roles (§8).** `Admin` and `Customer` ✅ Task 2; the `AccountId` claim ✅ Task 3; global query filters for tenant scoping are meaningful only once a tenant-owned entity exists, which is the accounts-lifecycle plan — noted there, not silently dropped.

**Type consistency.** `IssuedSession` is produced in Task 4 and consumed by name in Tasks 6 and 7. `AccessToken` is produced in Task 3 and consumed in 6, 7 and 8. `AuditEntry`/`AuditActions` are produced in Task 5 and consumed in 6, 7, 8 and 10. `PanelClaimTypes` is produced in Task 3 and consumed in 9. `RefreshCookie` is produced in Task 6 and consumed in 7. `IPasswordHasher` is produced in Task 1 and consumed in 6, 8 and 10.

**Ordering.** Task 9 deletes `UnauthenticatedCurrentUser`, which Tasks 6–8 still tolerate; their handler tests inject a fake `ICurrentUser` rather than depending on the registration, so nothing in 6–8 breaks when 9 lands. Task 3's `FallbackPolicy` is what forces Task 9's `[AllowAnonymous]` audit, and Step 11 of Task 3 does it immediately rather than leaving the test suite red across a task boundary.
