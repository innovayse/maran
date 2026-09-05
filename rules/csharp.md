# C# Rules

Normative. Enforced by `.editorconfig`, `Directory.Build.props` (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<Nullable>enable</Nullable>`) and review.

## Formatting & naming

- 4 spaces, 120 columns, LF, final newline. File-scoped namespaces only.
- `var` when the type is apparent from the right-hand side; explicit type otherwise.
- Braces always — for a single-line `if`, and for every member body. No expression-bodied members: a method, property, constructor, operator, indexer, accessor, or local function is written as a braced block, however short, so that growing a one-line body to two lines never changes its shape. Enforced mechanically in `backend/.editorconfig` (`csharp_prefer_braces`, `csharp_style_expression_bodied_*` and their `IDE0011`/`IDE0021`–`IDE0027` severities set to `error`) — note that this file, not the repository-root `.editorconfig`, is the one that binds backend code. `maran format` applies the fixes; `maran format --check` verifies without writing.

```csharp
// WRONG — an expression-bodied member
public string Name => _name;

// RIGHT
public string Name
{
    get { return _name; }
}
```

- `_camelCase` private INSTANCE fields; `PascalCase` for `const` and for `static readonly` alike — a cached delegate or a fixed array is a constant the language will not let us declare `const`, and the member-order rule below groups it with the constants, not the fields.
- `PascalCase` everything public, `IPascalCase` interfaces, `Async` suffix on async methods — including a Wolverine handler, which is `HandleAsync`, not `Handle`.
- These are build errors, not suggestions. `dotnet_naming_rule.*.severity` is honoured by the IDE and by `dotnet format` but NOT by the compiler, so `backend/.editorconfig` also sets `dotnet_diagnostic.IDE1006.severity = warning`, which `TreatWarningsAsErrors` turns into a failure. Without that line the naming rules are advisory and the tree drifts; it had drifted to 418 violations before the line existed.
- Types are `sealed` unless designed for inheritance. Commands, queries, and DTOs are `record`s.
- Package versions only in `Directory.Packages.props`.
- `using` directives: `System.*` first, then alphabetical, placed BEFORE the namespace declaration. No unused usings — `EnforceCodeStyleInBuild` plus `dotnet_diagnostic.IDE0005.severity = error` make an unused directive a build error, not an IDE hint. A file-level `using` duplicating a `GlobalUsings.cs` entry counts as unused.

## One type per file

- **One type per file, no exceptions**: a file contains exactly one class/record/interface/enum, and the file name equals the type name (`CreateSiteValidator.cs` holds `CreateSiteValidator` and nothing else). Nested private types are the only exemption, and only when they never leave the parent.
- **Interfaces never share a folder with models.** Every project collects its contracts in
  `Interfaces/` (`Maran.SharedKernel/Interfaces/IClock.cs`, `Maran.Sdk/Interfaces/IPanelModule.cs`);
  records, DTOs and entities live in their own folders (`Results/`, `Common/`, `Domain/`). A
  `record` sitting next to an `interface` is a review reject.
- **Generic and non-generic pairs**: `<Name>.cs` holds the non-generic type, `<Name>OfT.cs` holds the generic one (`Result.cs` → `Result`, `ResultOfT.cs` → `Result<T>`). They remain separate files, like any other two types.
- Folder path mirrors the namespace exactly (`Commands/CreateSite/` ⇔ `…Commands.CreateSite`). A type in the wrong folder is a review reject even if it compiles.
- No `Utils.cs`, `Helpers.cs`, `Extensions.cs` dumping grounds — every helper has a named home describing its single purpose. A helper shared by several modules gets that named home in `Maran.SharedKernel/Utilities/<Subject>/` (see "Cross-cutting infrastructure"), which is a map entry with rules, not an exemption from this one.

## Domain models are rich: every change of state is a method

An entity is not a bag of settable fields. Every property has a `private set`, and every way the
entity can change is a **method on the entity itself**. A handler orchestrates — load, call, save —
and never assigns a property.

Two kinds of method, and the name says which:

- **CRUD-shaped**, for a plain edit the domain has no opinion about: `Rename`, `ChangePlan`,
  `UpdateContact`. Named for the field group they replace.
- **Domain-logic**, for a transition with rules or consequences: `Suspend`, `Reactivate`, `Revoke`,
  `EnableTotp`, `Consume`. Named for what happens in the business, never for the field it touches —
  `Suspend()` says why; `SetStatus(AccountStatus.Suspended)` says nothing and lets a caller pass
  any value it likes.

```csharp
// RIGHT — the entity enforces its own rule, and the name states the intent
public void Revoke(DateTimeOffset at, SessionRevocationReason reason)
{
    if (RevokedAt is not null)
    {
        return;      // first reason survives; that is the entity's rule, not the caller's
    }

    RevokedAt = at;
    RevocationReason = reason;
}

// WRONG — anaemic model: the rule now lives in whichever handler remembers it
public DateTimeOffset? RevokedAt { get; set; }      // rejected in review
session.RevokedAt = clock.UtcNow;                   // and so is this
```

Creation is the same rule applied to the beginning: the public constructor (or a static factory
when creation can fail) is the only way an entity comes into existence in a valid state, and EF
Core's parameterless constructor is `private` so nothing else can reach it.

Do not add a method no use case calls yet. A rich model means every *existing* mutation is a named
method — not a speculative `Update…` for each property against the day something might need it
(YAGNI, rules/architecture.md).

## Domain enums live in `Domain/Enums/`

A module's enums go in `Domain/Enums/`, one per file, never loose beside the entities in `Domain/`.
The namespace follows the folder, as always: `Maran.Modules.Identity.Domain.Enums`.

```
Domain/
├── Session.cs              # the entity
├── User.cs
└── Enums/
    ├── SessionRevocationReason.cs
    ├── UserRole.cs
    └── AccountStatus.cs
```

Two reasons. An enum is a closed set of values, not a thing with behaviour and identity, so it does
not belong in the same list a reader scans to learn what the module models — mixed together, a
`Domain/` folder of fifteen files hides its four actual entities. And an enum is the member most
likely to be shared: statuses, roles and reasons are read by handlers, DTOs and EF configurations
alike, so having one predictable home spares every one of them a search.

The same separation the `Interfaces/`-never-beside-models rule makes for contracts.

## Member order — methods come last

Inside a type, members appear in this order, and a file that mixes them is a review reject:

1. Constants and `static readonly` fields
2. Instance fields
3. Properties
4. Constructors (public ones before the private EF Core one)
5. Methods (public before private)

```csharp
// RIGHT — an entity reads as "what it is", then "what it does"
public sealed class Session
{
    private const int TokenHashLength = 44;

    public Guid Id { get; private set; }

    public string TokenHash { get; private set; }

    public Session(Guid id, Guid userId, string tokenHash) { ... }

    private Session() { ... }

    public bool IsActive(DateTimeOffset now) { ... }

    public void Revoke(DateTimeOffset at, SessionRevocationReason reason) { ... }
}

// WRONG — a method between two properties
public sealed class Session
{
    public Guid Id { get; private set; }

    public bool IsActive(DateTimeOffset now) { ... }   // rejected in review

    public string TokenHash { get; private set; }
}
```

The reason is that a reader opening a domain model wants its **shape** first — what the thing is
made of — and its behaviour second. Interleaving the two means the shape can only be learned by
reading the whole file, and a property added later lands wherever the writer's cursor happened to
be. Applies to every type, not only entities: DTOs, options classes, services, controllers.

A constructor sits **below** the properties, not above them, even though it runs first. It is the
longest member of a typical entity — one assignment and one `<param>` line per property — so
placing it first buries the field list under a screen of ceremony, and the ceremony only makes
sense once the reader knows what is being assigned.

## Canonical backend layout — every file has one correct place

Nothing is filed "wherever it fits". A file whose path does not follow this map is a review reject, and so is a workaround that dodges the map instead of extending it.

```
backend/
├── Directory.Build.props · Directory.Packages.props · nuget.config · .editorconfig · Maran.sln
├── src/
│   ├── Maran.Host/                  # composition only — no business logic
│   │   ├── Program.cs                   # table of contents: Add* then Use*
│   │   ├── GlobalUsings.cs
│   │   ├── Modules/                     # ModuleRegistry.cs (explicit list — no assembly scanning),
│   │   │                                 #   ModulesEndpoint.cs + ModuleDto.cs (GET /api/v1/modules)
│   │   ├── Configuration/               # one options class per file + its validation
│   │   ├── Extensions/                  # EVERY *Extensions type: Add<Concern>/Use<Concern>,
│   │   │                                #   including each middleware's Use… method
│   │   ├── Middleware/                  # <X>Middleware.cs — the middleware itself, one per file
│   │   │                                #   (its Use… extension lives in Extensions/)
│   │   ├── RateLimiting/                # one named policy per file (login, api, provisioning)
│   │   ├── Resilience/                  # one pipeline per file (agent, acme, outbound http)
│   │   ├── Behaviors/                   # Wolverine message middleware (logging, tx, validation)
│   │   ├── Idempotency/                 # Idempotency-Key handling
│   │   ├── BackgroundServices/ · HealthChecks/ · Authorization/ · Serialization/
│   │   ├── Seeding/ · Filters/ · Security/ · Properties/
│   │   └── appsettings.json · appsettings.Development.json
│   ├── Maran.SharedKernel/          # primitives only; references nothing of ours
│   │   ├── DependencyInjection.cs       # AddSharedKernel — the project's registrations
│   │   ├── Interfaces/                  # ALL contracts: IClock, ICurrentUser, ICorrelationIdAccessor,
│   │   │                                 #   IEncryptionService, IErrorTextProvider
│   │   ├── Results/                     # Error.cs, Result.cs, ResultOfT.cs, PagedResult.cs
│   │   ├── Domain/                      # Entity, AggregateRoot, ValueObject, IDomainEvent
│   │   ├── Pagination/ · Persistence/ · Exceptions/ · Constants/ · Enums/ · Extensions/
│   │   ├── Localization/                # resx-backed IErrorTextProvider implementations (contract in Interfaces/)
│   │   ├── Security/                    # what holds a secret or states a policy: the encryption key
│   │   │                                #   and its EF converter, the password-hash parameters and
│   │   │                                #   hasher, the redaction floor, SensitiveString
│   │   │                                #   (contracts in Interfaces/; see "Security/, Utilities/
│   │   │                                #   and a module's Common/")
│   │   ├── Time/                        # SystemClock.cs
│   │   └── Utilities/<Subject>/         # pure rules and renderings over BCL primitives — no key,
│   │       ├── Mail/                    #   no salt, no configuration — grouped by subject; never
│   │       ├── Network/                 #   files directly under Utilities/
│   │       └── Tokens/                  #   Mail/: EmailAddressRule, MailHeaderTextRule
│   │                                    #   Network/: ClientAddress, HostNameRule, Ipv4MappedAddress
│   │                                    #   Tokens/: RefreshTokenHasher, PasswordResetTokenHasher
│   ├── Maran.Sdk/                   # the module contract consumed by paid modules too
│   │   ├── Interfaces/                  # IPanelModule.cs — the module contract — plus the
│   │   │                                #   read-only windows one module opens onto another's data,
│   │   │                                #   implemented by the OWNING module (IAccountDirectory,
│   │   │                                #   ISiteDirectory, IAuditWriter, IAlertRecipientDirectory).
│   │   │                                #   A seam holding a credential stays module-internal
│   │   │                                #   (rules/architecture.md "A shared facility's contract").
│   │   ├── Controllers/                 # BaseApiController (+ controller-scoped filters)
│   │   ├── Extensions/                  # ApiResultExtensions and other Sdk extensions
│   │   ├── Permissions/                 # permission constants per area, one file each
│   │   ├── Navigation/                  # menu contribution types
│   │   ├── Filters/                     # action filters modules may opt into
│   │   ├── Contracts/                   # cross-module contract types (incl. published messages
│   │   │                                #   such as SendMailRequested)
│   │   ├── Events/                      # integration-event base types (Base/ + per area)
│   ├── Maran.Agent.Client/          # the ONLY project generating agent gRPC code
│   │   ├── DependencyInjection.cs       # AddAgentClient — the project's registrations
│   │   ├── Channels/                    # AgentChannel.cs — unix-socket channel construction
│   │   ├── Errors/                      # AgentErrorTranslator.cs — the ONE place a wire AgentError
│   │   │                                #   becomes a code, and the ONE place the agent's own text
│   │   │                                #   is logged (and redacted). Shared by every service
│   │   │                                #   client, so a redaction is written and kept in one file
│   │   └── Services/<Proto>Service/      # one folder per proto service: client, seam, DTOs
│   │                                    # (SystemService/, AccountsService/, SitesService/,
│   │                                    #  SslService/, PhpService/, …)
│   └── Maran.Modules/               # grouping folder for all module projects
│       └── <Name>/                      # short folder (Sites/, Accounts/…); the project inside
│                                        #   is the full Maran.Modules.<Name>.csproj
│           ├── <Name>Module.cs          # IPanelModule: DI, permissions, menu, migrations. HTTP surface
│           │                            #   arrives through the module's own controllers, discovered
│           │                            #   by ASP.NET Core's controller model — IPanelModule has no
│           │                            #   endpoint-mapping member to implement.
│           ├── <Name>Manifest.cs        # module identity: id, display-name resource key,
│           │                            #   version, licence tier, dependencies
│           ├── Controllers/             # thin HTTP surface (+ External/ for outward APIs)
│           │   ├── <Resource>Controller.cs
│           │   └── Requests/            # request models bound from HTTP, one per file
│           ├── Commands/<Operation>/    # <Op>Command.cs, <Op>CommandHandler.cs, <Op>CommandValidator.cs
│           ├── Queries/<Operation>/     # <Op>Query.cs, <Op>QueryHandler.cs (+ <Op>QueryValidator.cs)
│           ├── Common/                  # *Dto.cs ONLY — the wire shapes, data with no logic.
│           │                            #   FLAT, no subfolders, no methods, no factories.
│           ├── Models/                   # data the module passes between its OWN layers and never
│           │                            #   puts on the wire: handler outcomes, carriers, frames
│           ├── Mappers/                  # <Thing>Mapper.cs — pure translation between a domain
│           │                            #   value and a wire shape. Never DECIDES, only restates.
│           │                            #   stateless pure rules over values — FLAT, no subfolders.
│           │                            #   NOT the default folder: a file lands here only after all
│           │                            #   four tests below say no — not shared (Utilities/), no DI
│           │                            #   lifetime (Services/), not a domain value object
│           │                            #   (Domain/), no effect on the HTTP surface (Controllers/)
│           ├── Interfaces/              # the module's own contracts — the seams it defines and
│           │                            #   injects (IMailer, IAcmeClient, ISessionService). Same
│           │                            #   place Maran.Sdk and Maran.SharedKernel put theirs;
│           │                            #   Domain/Interfaces/ is the one second home, for
│           │                            #   repository contracts beside their entities
│           ├── Options/                 # <Feature>Options — typed settings bound from
│           │                            #   configuration and validated at startup, as
│           │                            #   Maran.Host/Configuration/ holds the panel's. Inside
│           │                            #   the module this namespace shadows the BCL's
│           │                            #   Microsoft.Extensions.Options.Options, so wrap a test
│           │                            #   double as new OptionsWrapper<T>(…), not Options.Create
│           ├── Validators/              # validators for those options and for inputs shared across
│           │                            #   operations (an operation's own validator stays in
│           │                            #   Commands/<Operation>/ with the command it validates)
│           ├── IntegrationEvents/
│           │   ├── Events/              # events this module publishes for others
│           │   └── Handlers/            # handlers for other modules' events
│           ├── Services/                # domain services of this module
│           ├── Jobs/                    # scheduled and recurring work (Wolverine)
│           ├── Authorization/           # permission requirements and handlers
│           ├── Domain/
│           │   ├── Entities/            # EF-mapped, identity-bearing, one per file
│           │   ├── ValueObjects/        # values the business has rules about (AccessToken,
│           │   │                        #   IssuedSession, CidrRange), one per file
│           │   ├── Policies/            # stateless rules over those values (BanTtlPolicy,
│           │   │                        #   IpAddressNormalizer) — logic, so never Common/
│           │   ├── Enums/               # every domain enum (AccountStatus, UserRole, …)
│           │   ├── Events/              # domain events raised by those entities
│           │   └── Interfaces/          # domain-level contracts (e.g. repositories)
│           ├── Persistence/
│           │   ├── <Name>DbContext.cs   # + DesignTimeDbContextFactory.cs
│           │   ├── Configurations/      # <Entity>Configuration.cs — one per entity
│           │   ├── Interceptors/        # SaveChanges interceptors (audit, timestamps)
│           │   └── Migrations/          # EF-generated, module-scoped
│           ├── Seeders/                 # initial data owned by this module
│           ├── Resources/               # Messages.resx, Messages.ru.resx, Messages.hy.resx
│           └── Resources/               # the module's resx triples; ErrorMessages.resx also
│                                        #   DEFINES the module's error codes (see below)
└── tests/
    ├── Maran.<Project>.Tests/            # unit — mirrors src/ folder for folder
    ├── Maran.Modules.<Name>.Tests/       # one per module, mirrors the module
    ├── Maran.Host.IntegrationTests/      # HTTP/DB surface, Testcontainers
    └── Maran.ArchitectureTests/          # NetArchTest boundary rules
```

Feature-first layout (`Commands/`, `Queries/`, `Common/`, `IntegrationEvents/`, `Controllers/` + `Requests/`, `Persistence/` with `Configurations/`) applied inside module projects rather than inside shared layer projects. Two properties are non-negotiable for this product:

1. **The module is the project**, not a folder inside a shared layer project. Maran sells modules, so a module must be a physical, separately buildable, separately shippable unit with an enforced boundary.
2. **Each module owns its PostgreSQL schema and its own `DbContext`** — there is no application-wide `AppDbContext`. A module never reads another module's tables.

Rules that follow from the map:

- **One operation never spreads across folders.** Command, validator and handler of a single operation live together in `Commands/<Operation>/`; queries likewise. Splitting one operation across technical layers is the mess this map prevents.
- **A handler works with exactly ONE `DbContext`.** There are as many contexts in the process as
  there are modules, plus Wolverine's own tables in the `wolverine` schema, and a handler touching
  two of them has two transactions and no way to make them one. A handler that needs another
  module's data reads it through an `Sdk/Interfaces/` window or asks for the work by message
  (rules/architecture.md) — it does not open a second context.
- **A message is published AFTER the commit, and what must not be lost is enlisted in it.**
  Publishing before `SaveChangesAsync` means a handler for work that was then rolled back — a mail
  announcing a site that does not exist. Publishing after it means a window, microseconds wide, in
  which the process can die with the row written and the message never sent. Both existing
  publishers take the second trade deliberately and say so at the line
  (`RequestPasswordResetCommandHandler`, `BruteForceDetector`): a lost reset mail is asked for
  again, a lost ban is re-earned. **When the loss is NOT acceptable — the first message that drives
  the agent after a database write, where losing it means a row saying a site exists and no vhost on
  disk — the publish must be enlisted in the handler's transaction**, and the handler says which
  guarantee it is relying on in its own doc comment.
- **Queue durability is decided per message, and a message carrying a secret is never durable.**
  Making local queues durable wholesale looks like a free win — a restart stops losing published
  work — and it is not: Wolverine persists the envelope BODY, so `PasswordResetRequested` would
  leave a live password-reset token in a `wolverine` table that outlives the request. That is not a
  hypothetical; switching it on is what
  `PasswordResetEndpointTests.The_token_bearing_envelope_is_never_written_to_the_message_store` was
  written to catch, and it caught it. So a message that must survive a restart declares its own
  durable queue, and one that carries anything acting as a secret (rules/security.md item 8) stays
  in memory. Durability of the QUEUE is also not atomicity with the WRITE — a handler that confuses
  the two has an outage waiting in it.
- **Schema changes are expand-then-contract.** A migration never drops, renames, or narrows what the
  previous release still reads; the removal is a later release, after the code that read it is gone.
  `maran migrate guard` fails CI otherwise, and the exemption is a `// contract-phase:` line in the
  migration itself saying why no release reads it (rules/architecture.md).
- **Tenant scoping is the global query filter, and `IgnoreQueryFilters` is banned in `backend/src`.**
  `backend/src/BannedSymbols.txt` makes it a build error, so every deliberate bypass is an `RS0030`
  suppression carrying its reason on the line — unattended renewal, retention, startup
  reconciliation, account deletion, a server-wide uniqueness check. The ban is scoped to production
  code on purpose: a test bypasses the filter to PROVE it hides the row, and banning it there would
  put a suppression on the assertions that verify the rule.
- **Controllers stay thin**: bind the request, dispatch the command/query through Wolverine, translate the `Result` into an HTTP response. No business logic, no data access, no orchestration in a controller.
- **A module controller never overrides the route prefix.** `BaseApiController` fixes
  `api/v1/[controller]`; a module's `[Route]` states the full versioned path (`api/v1/accounts`)
  or is omitted entirely. Writing `[Route("accounts")]` silently drops the version and the prefix,
  and the mismatch surfaces as 404s in the SPA rather than as a build error.
- **Controller shape is fixed**: `sealed`, inherits `BaseApiController` (which owns the `IMessageBus Bus` and `ToActionResult`), kebab-case `[Route("site-backups")]`, `[Tags]`, `[Produces("application/json")]`, `[Authorize(Policy = …)]` at class level, a named `[EnableRateLimiting("<area>")]` policy, and per-action `[ProducesResponseType]` for every status the action can return. Action names end in `Async` (`GetAllAsync`, `GetByIdAsync`, `CreateAsync`). Example:

```csharp
[Route("audit-logs")]
[Tags("Audit Logs")]
[Produces("application/json")]
[Authorize(Policy = Permissions.Audit.Read)]
[EnableRateLimiting("api")]
public sealed class AuditLogsController : BaseApiController
{
    /// <summary>Lists audit entries with filters and pagination.</summary>
    /// <param name="query">Filter, sort, and paging parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AuditLogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAllAsync([FromQuery] GetAuditLogsQuery query, CancellationToken ct)
    {
        return Ok(await Bus.InvokeAsync<PagedResult<AuditLogDto>>(query, ct));
    }
}
```
- **Operation naming is fixed**: folder `Commands/CreateSite/` holds `CreateSiteCommand.cs`, `CreateSiteCommandHandler.cs`, `CreateSiteCommandValidator.cs`; folder `Queries/GetSite/` holds `GetSiteQuery.cs`, `GetSiteQueryHandler.cs` (+ `GetSiteQueryValidator.cs` when the query takes filters worth validating). Suffixes are always full: `…CommandHandler`, `…QueryHandler`, `…CommandValidator` — never a bare `…Handler`. Operation folders read as verb + subject: `GetSite`, `ListSites`, `ExportSiteLogs`.
- **Commands and queries are `record`s**, XML-documented with a `<param>` line for every field.
- **DTO naming and home**: outward-facing types end in `Dto` (`SiteDto`, `SiteDetailDto`, `SiteStatsDto`) and live in the module's `Common/` folder. There is no separate `DTOs/` folder.
- **Resource names are flat PascalCase, read as `<Subject><Problem>`**: `AccountNotFound`,
  `AccountNameTaken`, `AccountDomainTaken`, `PlanNotFound`, `EmailInvalidFormat`,
  `SiteNameMaxLength`. No dots, no snake_case, no prefixes repeating the module — the file already
  belongs to one.
- **That same string is the machine `code`** carried in the RFC 7807 payload. One identifier, not a
  code plus a separate resource key with a mapping table between them: a mapping is a place for the
  two to drift apart, and the first symptom of drift is an error rendering as its own code in front
  of a customer.
- **Every failure states its KIND, and the kind is what an HTTP status is derived from.** `Error` is
  `(string Code, ErrorType Type)` and there is no single-argument factory: the compiler requires the
  answer at every construction site, and `ApiResultExtensions` maps `ErrorType` to a status with one
  arm per value and no knowledge of any code. Inferring the status from the code's spelling is
  forbidden, and not as a matter of taste — it was the previous design and it failed twice. Once
  loudly: the suffixes were matched in a lower-case dotted form, the codes became PascalCase, and
  every missing account silently answered 400. Once quietly, for as long as it existed: eighteen
  codes naming a SERVER failure — `AcmeAuthorityUnreachable`, `DatabaseProvisioningFailed`,
  `MailDeliveryFailed`, `AgentSystemFailure` — matched no suffix, fell to the 400 default, and told
  customers their request was malformed while the server was the thing that had broken.
  `backend/tests/Maran.Sdk.Tests` holds the census that fails when a shipped code has no decided
  kind.
- **A resource another account owns is `ErrorType.NotFound`, never `ErrorType.Forbidden`.** A 403
  confirms the row exists, which is the whole of what an enumeration attack wants, and it
  contradicts the IDOR test every tenant entity carries (rules/security.md item 6). Tenant scoping
  produces this answer by itself — the query filter hides the row and the handler finds nothing — so
  reaching for `Forbidden` on a tenant resource means the filter was bypassed. `Forbidden` is for a
  refusal that discloses nothing about another tenant: setup already complete, two-factor already
  enabled.
- **Resources are reached through `IStringLocalizer<T>`**, where `T` is the class named after the
  resource file. Typed access beats passing a `ResourceManager` around: the localizer resolves the
  request culture on its own and the type makes the dependency visible in a constructor signature.
  For most files that class is an empty hand-written marker (`DisplayNames.cs` next to
  `DisplayNames.resx`). For `ErrorMessages` it is **generated from the resx itself**, so there is no
  hand-written `ErrorMessages.cs` — see the next rule.
- **`ErrorMessages.resx` is where error codes are defined, and there is no `<Name>Errors.cs`.** The
  project generates a strongly-typed class from it (`<Generator>MSBuild:Compile</Generator>` plus
  `StronglyTyped*` metadata on the `EmbeddedResource`, and `<NeutralLanguage>en</NeutralLanguage>`
  on the project), giving one member per key. Code raises a failure as
  `Error.Of(nameof(ErrorMessages.AccountNameTaken))`.

  ```csharp
  // RIGHT — the key must exist in the resx or this does not compile, and the KIND is stated
  return Result<AccountDto>.Fail(Error.Of(nameof(ErrorMessages.AccountNameTaken), ErrorType.Conflict));

  // WRONG — a hand-written factory duplicating the code, plus an English sentence nobody renders
  return Result<AccountDto>.Fail(AccountsErrors.NameTaken(command.Name));   // rejected in review
  ```

  A hand-written errors class was a second declaration of every code, carrying a second, untranslated
  description of the same failure; the pair could drift, and a reviewer could not tell which sentence
  a customer would see. Generating from the resx removes the duplicate and makes a missing
  translation a **build** error instead of a customer reading a machine code.
- **`Error` carries a code and nothing else.** There is no message field: the sentence lives in the
  resx, in three languages. Operator-facing diagnostic text that has no resx entry — the Rust
  agent's own error string, for instance — is **logged** at the boundary that receives it, never
  attached to the `Error` travelling outward.
- **That boundary is one type, not one per caller.** In `Maran.Agent.Client` it is
  `Errors/AgentErrorTranslator.cs`: every service client calls it, and no client writes its own
  wire-error-to-code mapping or its own log line. The rule exists because the mapping was once
  copied into five clients; they agreed on the day they were written, and a security control that
  lives in five files has to be fixed five times and stay fixed. Redaction of secret material
  (PEM blocks, and whatever follows) belongs there for the same reason.
- **A module writes its audit entries through its own `Services/<Module>AuditJournal.cs`, never by
  constructing an `AuditEntry` at a call site.** The journal is not a wrapper: it is where that
  module decides what an entry of its kind carries and what it must NOT — which identifiers are
  recorded, what is redacted, how a system actor is spelled. That decision has to live in one file
  to be reviewable, and every call site that builds its own entry is a copy of it that nobody
  reviewed. Ten modules do this; Identity, whose events matter most, once built entries inline in
  thirteen handlers and had no journal — which is how three different spellings of the system actor
  reached the same table, one of them filling `IpAddress`/`UserAgent` with the actor's name against
  `AuditEntry`'s own documentation. Ten journals are not duplication for the same reason: each is a
  different redaction policy, and merging them would erase the decision.

  **That defence answers a different question from the one about folders, and reading it as an
  answer to both is what misfiled all ten.** "Why does this type exist once per module?" is answered
  by the redaction policy above. "Which folder does it go in?" is answered only by
  "`Common/` versus `Services/`" below, and a journal is a registered, DbContext-holding service, so
  the answer is `Services/`. A true reason for a type's existence never settles its address; nothing
  about a per-module redaction policy implies `Common/`, and the two questions must be asked
  separately every time.

  `maran structure` enforces it (check 6c): `new AuditEntry(` under `backend/src` outside a
  `*AuditJournal.cs`. `Maran.Sdk/Contracts/SystemAuditEntry.cs` is exempt by construction — a
  constructor has to be called where the type is declared.
- **One resource file per purpose, named for it** — not one catch-all per module:
  `ErrorMessages` (domain failures surfaced as error codes), `ValidationMessages` (validator
  output), `DisplayNames` (module, plan and other user-facing names), `EmailTemplates`,
  `NotificationMessages`. A file appears when its purpose does; do not create empty ones.
- Each file is a triple in the module's `Resources/` folder: `X.resx` (English, the invariant),
  `X.ru.resx`, `X.hy.resx` — identical key sets, verified by a test. Entries stay minimal:
  `<data name="AccountNotFound"><value>…</value></data>`, no comments, no `xml:space` noise.
- **Standard file suffixes** (one concept, one suffix — no synonyms): `…Command/…Query`, `…CommandHandler/…QueryHandler`, `…CommandValidator/…QueryValidator`, `…Dto`, `…Controller`, `…Configuration` (EF), `…Options`, `…Service`, `…Seeder`, `…EventHandler`, `…Interceptor`, `…Middleware`, `…Policy`, `…Extensions`.
- **No dumping grounds inside a module** — no `Managers/`, `Helpers/`, `Misc/`, `Core/`. What the ban
  is about is a folder named for a *layer* with no stated purpose: nobody can say what does and does
  not belong in `Helpers/`, so everything does, and the folder is a bag. It is **not** a ban on
  `Services/`. `Services/` is in the canonical map above with a stated purpose — *domain services of
  this module* — and a stated, mechanical membership test: a type the module registers in
  `<Name>Module.cs` (see "`Common/` versus `Services/`"). A folder with a test that decides cases is
  the opposite of a dumping ground, and every module has used `Services/` from the first day.

  This entry once read "no `Services/`", which contradicted the map on the same page, and that
  contradiction is the direct cause of the misfiling it was meant to prevent: an agent believing
  `Services/` was banned filed `IdentityAuditJournal` and `SecurityPolicyCache` in `Common/` instead,
  along with eight more journals, `SmtpSettingsCache`, `SiteDirectory` and `CertificateInstaller`.
  A rule that forbids the only correct home does not stop the file being written; it only decides
  where the file goes wrong.

  `Common/` exists for types genuinely shared across operations of that one module, and holds only
  precisely-named types; a file named `Common.cs`, `Helpers.cs` or `Shared.cs` inside it is a review
  reject. Anything shared by two modules moves down to `SharedKernel` or `Sdk` — never sideways
  between modules.
- **Test projects mirror source paths exactly**: `Commands/CreateSite/CreateSiteHandlerTests.cs` sits at the same relative path as the code it covers.
- **Namespace = path, always**: `Maran.Modules.Sites.Commands.CreateSite`. No namespace shortcuts, no folder outside the namespace.
- **New shapes extend the map, they don't bypass it.** A genuinely new kind of file gets a named folder here first — inventing an ad-hoc location is rejected, and so is a temporary hack "until we tidy it later".

## Doc comments — mandatory for ALL code

XML docs are REQUIRED on **every type and every member — public, internal, protected, and private alike** — in all code, tests included. Not just the SDK surface: handlers, validators, private helpers, fields with non-obvious meaning. A test's summary restates its behaviour sentence, so the generated documentation carries the contract too (rules/testing.md). Say what the caller needs, not what the code does line by line.

```csharp
/// <summary>
/// Creates a hosting account: system user, home directory, quota, and the customer login.
/// Idempotent: returns <see cref="AccountError.AlreadyExists"/> for a duplicate name.
/// </summary>
/// <param name="command">Validated account parameters; see <see cref="CreateAccountValidator"/>.</param>
/// <returns>The created account id, or a typed error.</returns>
public Task<Result<AccountId>> HandleAsync(CreateAccount command, CancellationToken ct);
```

## Analyser rules are switched off in the open, never with `<NoWarn>`

No project file carries a `<NoWarn>`. It is invisible from the code it affects, it hides every future
violation of the rule as well as today's, and it silences the rule for a whole project when the reason
covers two files. A rule that genuinely must be off is turned off in `backend/.editorconfig`, in a
section scoped to the narrowest path that needs it, above a comment saying which rule wins and why:

```editorconfig
# Test names are behaviour sentences, which rules/testing.md mandates and xUnit requires to be public.
[tests/**/*.cs]
dotnet_diagnostic.CA1707.severity = none
```

A suppression whose comment does not name the rule it defers to is a review reject. `CS1591` (missing
XML comment) is never among them: see the doc-comment rule above.

## The backend owns all user-facing message text (.resx)

The frontend never translates server outcomes — it displays what we send. That makes localization a backend responsibility, enforced here:

- Every user-facing message lives in **`.resx` resource files inside its owning module** (`Resources/Messages.resx`, `Messages.ru.resx`, `Messages.hy.resx`). Invariant `.resx` is English.
- **Hardcoded user-facing strings in C# are a review reject.** A message reaching a customer or an administrator through the API comes from a resource lookup, never from a string literal in a handler.
- Requests carry the user's language (`Accept-Language`, falling back to the account's stored preference, then English). The localization middleware sets the culture; error responses are rendered in that culture.
- `Error.Code` stays machine-stable and untranslated (`SitesDomainTaken`) — it identifies the failure and the SPA branches on it. `Error.Type` classifies it, and is the only thing the HTTP status is read from. The resource entry keyed by the code supplies the human text placed in the RFC 7807 `title`/`detail`.
- Role-aware detail still applies (rules/security.md): the resource string a customer receives carries no paths, versions, or tool output; administrators may receive the diagnostic variant.
- Operator-facing log messages are English literals and are NOT localized — resources are for what users read.

```csharp
/// <summary>Resolves localized text for a domain error code.</summary>
public interface IErrorTextProvider
{
    /// <summary>Returns the message for <paramref name="code"/> in the current request culture.</summary>
    string Resolve(string code, params object[] arguments);
}
```

## Cross-cutting infrastructure — one home per concern

Cross-cutting code is written once and reused by every module, including paid marketplace modules. Duplicating any of it inside a module is a review reject.

**What lives in `Maran.Sdk/`** (visible to every module, ours and third-party):

```
Maran.Sdk/
├── IPanelModule.cs
├── Controllers/
│   ├── BaseApiController.cs      # the base every module controller inherits
│   └── ApiResultExtensions.cs    # Result<T> -> IActionResult (ProblemDetails on failure)
├── Permissions/                  # permission constants per area, one file per area
├── Navigation/                   # menu contribution types
├── Filters/                      # action filters modules may opt into
├── Streaming/                    # the panel's one stream transport, shared by every module that
│   ├── EventStreamWriter.cs      #   streams: server-sent events over an HttpResponse — framing,
│   └── EventStreamFrame.cs       #   heartbeat, flushing, shutdown ordering; a module supplies only
│                                 #   its frame type, event name and payload
└── GlobalUsings.cs
```

`Streaming/` is in the Sdk and not in `SharedKernel/Utilities/` for a mechanical reason: an SSE
writer takes an `HttpResponse`, and `Maran.SharedKernel.csproj` carries no
`FrameworkReference Include="Microsoft.AspNetCore.App"` while `Maran.Sdk.csproj` does. A module that
streams **MUST NOT** write its own SSE pump — the wire shape is read by one SPA stream helper, and a
second implementation of it is a second thing to drift.

`BaseApiController` carries what every module controller needs and nothing else: `[ApiController]`, the `api/v1/[controller]` route convention, the current user, the correlation id, and the `Result`→HTTP translation. **A module controller inherits it — never `ControllerBase` directly**, and never re-implements result translation.

**What lives in `Maran.SharedKernel/Utilities/`** (general-purpose helpers, visible to every project):

```
Maran.SharedKernel/Utilities/
├── Mail/
│   ├── EmailAddressRule.cs       # what the panel accepts as a bare e-mail address
│   └── MailHeaderTextRule.cs     # what may be written into a mail header
├── Network/
│   ├── ClientAddress.cs          # the one spelling of a caller's address
│   ├── HostNameRule.cs           # what the panel accepts as a DNS host name
│   └── Ipv4MappedAddress.cs      # unwrapping the mapped form a dual-stack listener reports
└── Tokens/
    ├── PasswordResetTokenHasher.cs  # mint a reset token, and the digest stored beside it
    └── RefreshTokenHasher.cs        # mint a refresh token, and the digest stored beside a session
```

A helper belongs here when it answers a **general** question the panel asks in more than one place —
"is this a valid e-mail address", "what is the caller's address", "is this a valid host name". It stays in its module when it is
feature-specific: a cron expression translator, a ban's time-to-live policy, an audit journal and
every DTO are that module's business and stay in the module, however reusable they look — in its
`Common/` if inert and nothing else claims it first (see the four tests below; the
journal is a service, the translator and the DTO are not).

### `Security/`, `Utilities/` and a module's `Common/`

Three folders have been mistaken for one another, and the mistake is always the same shape: a new
helper is filed where its author happened to look first. These are the three questions, in order.
**Ask them in order — the first one that answers "yes" is the home.**

**1. Is its correctness defined by one module's own tables, contract or vocabulary?** Then it stays
in that module, and no argument about how reusable it looks moves it. Which of the module's own
folders it lands in is a **second, separate question**, answered by test 2 below: `Common/` if it is
inert, `Services/` if it has a lifetime.

`Common/` is **not** a small `Utilities/`. It is the module's **inert internal furniture**: its DTOs,
its value objects and snapshots, its profiles and pure translators, its stream frames. Not one of
those is a utility, and the two folders share no idea beyond both being places a file can sit. Its
live counterpart is `Services/`, which holds what the container constructs — the audit journal, the
caches, the directories and installers. It is also **flat**: it holds files, never subfolders (see
"`Common/` is FLAT" below), so a type is never one directory deeper than the tests that judge it.

**What disqualifies a file from `Common/`** — the half that matters, because it is the sentence that
would have caught the token hashers:

> A file is misfiled in `Common/` when nothing about it is specific to that module: it takes only
> BCL types, returns only BCL types, reads none of the module's entities, options, resources or
> `DbContext`, and could be compiled with the module deleted. A second module could then adopt it
> unchanged — which is exactly the definition of a shared helper, and it belongs in
> `SharedKernel/Utilities/<Subject>/` instead.

Being the only caller today does not save it. `RefreshTokenHasher` and `PasswordResetTokenHasher`
lived in `Identity/Common/` for that reason alone: they take a `string`, return a `string`, and
Identity's schema appears nowhere in them. Compare `CronScheduleTranslator` and
`CidrRangeNormalizer`, which look every bit as general and are not — the normaliser encodes
Firewall's own policy on scope ids, and it stays.

**`Common/` holds `*Dto.cs` and nothing else, and a DTO carries no logic.** No methods, no
computed properties, no static factories, no private constructors — a positional record and nothing
more. The folder was corrected six times in a row, each round moving out the one shape somebody had
pointed at, and the reason it kept happening is that "inert module-shared type" described a
disposition rather than a file. `*Dto.cs` is a file name; a reviewer can check it without a
judgement.

Where the rest went, and the question each folder answers:

| Folder | What lives there | The question |
|---|---|---|
| `Common/` | `LoginResultDto`, `SessionDto` | is this a shape that goes on the wire? |
| `Models/` | `LoginOutcome`, `SmtpProfile`, `TaskFrame`, the ACME records | is this data the module passes between its own layers and never serialises? |
| `Mappers/` | `AuthenticatedSessionMapper`, `CertificateMapper` | does this translate a domain value into a wire shape? |
| `Domain/Entities/` | `User`, `Session`, `Certificate` | is this identity-bearing and EF-mapped? |
| `Domain/ValueObjects/` | `AccessToken`, `IssuedSession`, `CidrRange` | is this a value the business has rules about? |
| `Domain/Policies/` | `BanTtlPolicy`, `IpAddressNormalizer` | is this a stateless rule over those values? |
| `Services/` | every `AddScoped`/`AddSingleton` type | does the container construct it? |

**A mapper translates; it never decides.** If a mapper has to read a null to work out which case it
is in, the decision is being made in the wrong place — it should arrive already made, from the
handler that made it, and the mapper should only restate it on the wire. That is why
`AuthenticatedSessionMapper` takes an `AuthenticatedOutcome` and not four loose fields.

**A handler's return value is not domain.** The business has rules about a session and a token; it
has no rules about "what a handler returned". Those types are `Models/`.

**`Common/` is not the default.** It is the last answer, not the first. A file goes there only when
every other question below has been asked and answered "no" — and the folder has now been corrected
three times in a row precisely because each round asked only the question someone had pointed at.
"It is module-specific and the container does not construct it" is not a reason to file something in
`Common/`; it is two of four questions answered, and the remaining two are where the last three
rounds' mistakes were hiding.

**There are four disqualification tests, and they answer four different questions.** A file must
pass **all four**; passing one says nothing whatever about the others. Test 1 separates `Common/`
from `SharedKernel/Utilities/` — *is this specific to the module at all*. Test 2 separates it from
the module's own `Services/` — *is this an inert value or a live service*. Test 3 separates it from
`Domain/` — *is this a value object of the module's domain*. Test 4 separates it from `Controllers/`
and `Maran.Host/` — *does this act on the HTTP surface*. Ten audit journals and two caches sat in
`Common/` because test 1 was applied, answered "yes, Identity-specific", and was read as a verdict
on the folder. It was not: `SecurityPolicyCache` is as Identity-specific as `SecurityPolicyDto`, so
test 1 cannot tell them apart, and only test 2 can. The same failure then repeated with
`SecurityPolicySnapshot` (tests 1 and 2 both said `Common/`; test 3 says `Domain/`) and with
`RefreshCookie` (tests 1, 2 and 3 all said `Common/`; test 4 says `Controllers/`).

| | Test 1 — vs `SharedKernel/Utilities/` | Test 2 — vs `Services/` | Test 3 — vs `Domain/` | Test 4 — vs `Controllers/` |
|---|---|---|---|---|
| Question | Is anything about it specific to this module? | Does it hold state or have a DI lifetime? | Is it a value object or entity of this module's domain? | Does it act on the HTTP surface? |
| Measurement | does it read the module's entities, options, resources or `DbContext`; would it compile with the module deleted | is it registered in `<Name>Module.cs` — `AddScoped`, `AddSingleton` or `AddTransient` | does it mirror an entity's fields, take its defaults from that entity's constants, or carry domain behaviour | does it name `HttpResponse`, `HttpRequest`, `HttpContext`, `CookieOptions` or a cookie/header collection |
| "No" means | it is a shared helper → `SharedKernel/Utilities/<Subject>/` | it is inert → `Common/` | it is a carrier, not a model → `Common/` | it computes rather than acts → `Common/` |
| "Yes" means | it is this module's business → keep it here, whatever it looks like | it is a service → `Services/` | it is domain → `Domain/` | it is HTTP behaviour → `Controllers/`, or `Maran.Host/` if panel-wide |

> **Test 1, stated to be applied — and the refinement the `Ssl` audit added:** "would it compile
> with the module deleted" is a proxy for the real question, "is anything about it specific to this
> module", and a proxy can be satisfied while the thing it stands for is not. A type whose
> **correctness is defined by an external specification that only one module is answerable for** is
> specific to that module even when its signature is BCL-in, BCL-out and it would compile anywhere.
> `Ssl/Common/JsonObjectValue` is the worked example: it serializes a `Dictionary<string, string>` to
> a `string`, so the compile-without-the-module measurement says "generic, move it" — but what it
> computes is not "a JSON object", it is *the exact canonical byte sequence RFC 7638 requires for a
> JWK thumbprint*, a form the ACME protocol defines and only `Ssl` ever has occasion to produce. The
> external spec is `Ssl`'s to answer, not the panel's: no other module issues certificates, and a
> "generic JSON canonicaliser" in `SharedKernel/Utilities/` would invite a second, subtly different
> canonicaliser the day some other module needs *a* canonical JSON form for *its own* purpose and
> reasonably assumes the shared one is safe to reuse — which it is not, because RFC 7638's ordering
> and escaping rules are not "canonical JSON" in general, only canonical for a thumbprint. Two
> spec-bound types that merely resemble each other by signature are a worse outcome than one
> module-owned type with an unglamorous name. This does **not** loosen test 1 into an escape hatch:
> it applies only when (a) an identifiable external specification governs the output, not house
> style or a value judgment, and (b) exactly one module has business that spec serves — a type an
> RFC governs but that every module needs (`EmailAddressRule` against RFC 5322, `Ipv4MappedAddress`
> against RFC 4291) still answers "no" and still moves to `SharedKernel/Utilities/<Subject>/`,
> because there the second qualifier fails: the spec is not any one module's to own.

> **Test 2, stated to be applied:** a file is misfiled in `Common/` when it holds state or has a
> dependency-injection lifetime — when its type name appears inside
> `services.AddScoped<…>()`, `AddSingleton<…>()` or `AddTransient<…>()` in the module's
> `<Name>Module.cs`, or when it holds an injected `DbContext`, cache, clock or client. That type is
> a **service**, and it belongs in the module's `Services/`.

The registration is the measurement because it is not a matter of opinion: it is a line of code a
reviewer can point at, and it decides every case that adjectives could not. Every audit journal
holds its module's `DbContext` and is `services.AddScoped<…AuditJournal>()`; `SecurityPolicyCache`
and `SmtpSettingsCache` hold state across requests and are `services.AddSingleton<…>()`;
`SiteDirectory` and `CertificateInstaller` are `AddScoped`. All twelve are services, and all twelve
live in their module's `Services/`. `maran structure` checks this half mechanically (check 6d).

> **Test 3, stated to be applied:** a file is misfiled in `Common/` when it models a thing the
> module's business has rules about — when it mirrors an entity's fields, takes its defaults from
> that entity's own constants, or carries behaviour derived from those fields. That is a value
> object, and it belongs in the module's `Domain/` beside the entity whose rules it restates.

A record is **not** automatically a value object, and this is the half the test gets wrong if it is
read as "records go to `Domain/`". The question is whether the module's business has rules about the
thing modelled, or whether the type merely carries data from one layer to the next.
`IssuedSession` and `LoginOutcome` are carriers: a handler builds one, a controller unpacks it, and
nothing in Identity's business is stated by their shape — they stay in `Common/`.

`AccessToken` looks like a third carrier and is not, and the difference is worth stating because a
first reading of this test put it in the carrier list. It binds three fields — the compact JWT, its
`ExpiresAt`, and `RequiresTwoFactorSetup` — and it exists **so that they cannot disagree**: the flag
travels beside the token rather than being recomputed by each caller precisely so the response body
and the claim inside the token are, by construction, one decision. A body saying "you are free" over
a token the authorization handler refuses everywhere is an unexplainable 403 on every screen, and
this type's shape is what forbids it. **A type that exists to make two facts inseparable is stating
a rule, not carrying data** — the fifteen-minute lifetime and the enrolment gate are Identity's
business, not a transport detail — so it belongs in `Domain/`. The general form: ask not only "does
it mirror an entity" but "would splitting this type into its fields lose a guarantee?" If yes, the
guarantee is the domain rule and the type is a value object. `SmtpProfile` is a
carrier too: it is the decrypted material `SmtpMailer` needs, states no rule, and mirrors nothing
(the entity holds ciphertext). `SecurityPolicySnapshot` is the other thing entirely: it mirrors the
`SecurityPolicy` entity field for field, its `Default` is built from that entity's own constants,
and `LockoutDuration()` is a rule about a locked account. Left in `Common/` it is a second place the
panel's lockout policy is stated, which is exactly what `Domain/` exists to prevent. It moved.

A near-empty `Domain/` beside a full `Common/` is **evidence to look, not proof of a mistake.**
Several modules genuinely own no domain: `Cron`, `Files`, `Ftp`, `Backups`, `Provisioning` and
`Licensing` hold no entity because the **agent** owns the state and the module only transports
requests to it. Their `Common/` is legitimately all DTOs and translators. `Identity`, `Sites`,
`Ssl`, `Firewall`, `Monitoring` and `Accounts` do own a domain, and those are the `Common/` folders
to read twice.

> **Test 4, stated to be applied — and this is the sentence the first three rounds were missing:**
> **inert means NO EFFECT, not merely no DI registration.** Test 2's measurement is a registration,
> and a `static` class never has one — so a static class escapes test 2 automatically, whatever it
> does. A pure function from values to values is inert however many callers it has; a method that
> writes to an `HttpResponse`, touches the filesystem, launches a process, makes a network call or
> reads the ambient clock is **not** inert, even when it is `static`, takes no injected dependency
> and appears in no registration.

`RefreshCookie` is the worked example: it was Identity-specific (test 1 said stay), never registered
(test 2 said stay) and modelled nothing the business has rules about (test 3 said stay) — and it
still did not belong, because it took an `HttpResponse` and **mutated** it, owning the refresh
cookie's name, path, `HttpOnly`/`Secure`/`SameSite` flags and expiry. That is behaviour on the HTTP
surface, and the HTTP surface is `Controllers/` (or `Maran.Host/` when the concern is panel-wide
middleware rather than one module's endpoints). It moved to `Identity/Controllers/`, next to the one
controller that calls it. `maran structure` check 6e enforces the HTTP half of this mechanically.

One deliberate non-effect: emitting a structured log line through a logger the caller passes in is
not an effect for this test. `CronAgentErrorTranslator.Translate` writes a breadcrumb and is still
`Common/`, because a log line is observability — nothing in the program reads it back, and the
method's answer to its caller is a pure function of its arguments. An `HttpResponse` mutation is the
opposite: it *is* the program's output.

**What legitimately STAYS in `Common/`, and why.** This is not a rule for emptying the folder — a
module with an empty `Common/` has usually just moved its DTOs somewhere worse. `Common/` keeps
everything that passes all four tests:

- **Every `*Dto.cs`**, and the factories that are pure mappings over them
  (`CertificateDtoFactory`, `MetricsChartDtoFactory`, `PanelTaskDtoFactory`, `SiteDescriptorFactory`).
- **Carrier records the module passes between its own layers** — transport, not models:
  `IssuedSession`, `LoginOutcome`, `SmtpProfile`, `SiteLogTailTarget`,
  `MetricBucketRow`, `NetworkRate`, `IssuedCertificate`, `AcmeOrderRequest`, `AcmeRegistration`,
  `AcmeAttempt`, `AcmeResponse`, `AcmeProblem`. Each carries data; none states a rule.
- **Stream frames**: `SiteLogFrame`, `TaskFrame` — one element of a server-sent stream, with named
  constructors for its two shapes. A frame is a wire shape, not a domain concept.
- **Stateless pure rules and translators over values**: `BanTtlPolicy`, `IpAddressNormalizer`,
  `CidrRange`, `CidrRangeNormalizer`, `FirewallRuleSubject`, `CronScheduleTranslator`,
  `CronAgentErrorTranslator`, `NetworkRateCalculator`, `ChartWindow`. Each encodes its module's own
  policy, so test 1 keeps it out of `SharedKernel/Utilities/`; none holds state, takes an injected
  dependency or appears in a DI registration, so test 2 keeps it out of `Services/`; none models a
  thing with business rules, so test 3 keeps it out of `Domain/`; and none has an effect, so test 4
  keeps it out of `Controllers/`. `Ssl/Common/JsonObjectValue` belongs here too, by the refinement
  above: its signature is generic (`IReadOnlyDictionary<string, string>` in, `string` out) but its
  correctness is RFC 7638's JWK-thumbprint canonicalisation, which only `Ssl` has occasion to
  produce — test 1 keeps it out of `SharedKernel/Utilities/` for the same reason as the rest of this
  list, even though its measurement (would it compile with the module deleted) alone says "yes".
  keeps it out of `Controllers/`. Same input, same output, forever.
**`Common/` is FLAT — it has no subfolders, and the three it used to have are module-root folders
now.** `Interfaces/`, `Options/` and `Validators/` are `<Module>/Interfaces/`, `<Module>/Options/`,
`<Module>/Validators/`, exactly where `Maran.Sdk/Interfaces/`, `Maran.SharedKernel/Interfaces/` and
`Maran.Host/Configuration/` already put the same three kinds of file. They were nested for one
round and the nesting was wrong twice over.

It was wrong by the definition above: `Common/` is **inert internal furniture**, and neither a
contract nor a settings record is furniture. An interface is the module's public seam — the thing
the container binds an implementation to; an options class is configuration an operator edits in
`appsettings.json`. Filing a contract inside the furniture drawer is what made `Common/` read as a
catch-all, which is the disease the four disqualification tests exist to treat: every previous
round moved something OUT of `Common/`, and this arrangement was quietly moving things in.

It was also wrong mechanically. Those three folders had to be **exempted by name** from check 6d,
because the seam, the settings record and the options validator are precisely what a DI
registration mentions — so the check that enforces test 2 stopped at `Common/`'s top level, and any
registered type filed one folder deeper was invisible to it. A folder whose contents must be
excused from the folder's own rule is not a subfolder of it. With the three hoisted, nothing
registered by design sits under `Common/`, so **checks 6d and 6e now scan the whole `Common/`
subtree** and the carve-out is gone.

**One consequence, stated so it is not rediscovered as a bug.** A module folder named `Options/`
gives the module a namespace `Maran.Modules.<X>.Options`, which **shadows the BCL's
`Microsoft.Extensions.Options.Options`** for every file in that module's namespace tree and its test
project's. `Options.Create(new JwtOptions { … })` therefore stops compiling with
`CS0234: 'Create' does not exist in the namespace 'Maran.Modules.Identity.Options'`. Write
`new OptionsWrapper<T>(…)` instead — it is the same object, it needs no qualification, and it reads
as what a test is doing. Fully qualifying (`Microsoft.Extensions.Options.Options.Create`) also
works and is what `FirewallTestContext` had already been driven to under the old nesting, which is
the point: the collision is not new, it was there under `Common/Options/` too and someone had
already paid for it silently. Twelve call sites were converted with the hoist.

The counter-argument, weighed and rejected: `Common/Options/` did group a module's settings beside
the DTOs those settings shape, and hoisting adds top-level folders to a module. But options are not
shaped by DTOs — they are bound from configuration and read by services, and the DTO adjacency was
proximity, not kinship. As for the folder count, the nesting cost more than it saved: forty-seven
`Common/{Interfaces,Options,Validators}` folders existed across sixteen modules and **thirty-six of
them were empty `.gitkeep` scaffolding**, pre-created by `maran module` for modules that have no
seam and no settings. Eleven real folders at the module root are fewer directories than
forty-seven, and empty scaffolding is how a layout spreads without anyone deciding it — the next
person to implement `Backups` finds `Common/Options/` already there and files into it without ever
asking the four tests. The scaffold no longer creates them; a module grows the folder with its
first real file.

**Two modules may hold a same-named file, and that is not a collision.** `Sites/Common/SiteDescriptorFactory`
and `Ssl/Common/SiteDescriptorFactory` both build the agent contract's `SiteDescriptor`; the name
names the thing produced, which is the same thing, and the signatures differ because the *sources*
differ — Sites owns the `Site` entity and maps from it, Ssl may only see a `SiteSnapshot` through
the cross-module seam. Renaming either to encode its input would name the argument rather than the
result, and the modules cannot share one file: a cross-module import is banned, which is the point.

**2. Does it hold a secret, or state a policy an operator or a stored row depends on?** Then it is
`Security/`.

`Security/` is not "the folder for crypto" and it is not "the folder for anything sensitive". It is
the folder for **the panel's one answer to a question a second answer would make a defect**:
the encryption key and the converter that carries it (`AesGcmEncryptionService`,
`EncryptedStringConverter`), the Argon2id cost parameters and the rehash path they drive
(`PasswordHashParameters`, `Argon2idPasswordHasher`), the length floor the minters and the redactor
must agree on (`SecretRedactionPolicy`, `ProvisionedPasswordGenerator`), and the types that keep a
secret out of a log (`SensitiveString`). Its contracts live in `Interfaces/`; its implementations
are registered in `DependencyInjection.cs`.

**3. Otherwise it is a pure rule or rendering over BCL primitives** — `Utilities/<Subject>/`.

**The one question that keeps being got wrong: given a new hasher, which folder?** "It hashes" is
not the answer, because both `Security/Argon2idPasswordHasher` and `Utilities/Tokens/RefreshTokenHasher`
hash. The line is **whether the output depends on anything but the input**:

| | `Security/` | `Utilities/` |
|---|---|---|
| Argon2id over a password | keyed by a salt, priced by `PasswordHashParameters`, upgraded per row by `NeedsRehash`, resolved through `IPasswordHasher` | — |
| SHA-256 over a 256-bit random token | — | no key, no salt, no parameter, no registration; the same input always gives the same output |

So: a hasher with a **dial on it** — a salt, a key, a cost, anything an operator or a future edit
can turn — is policy, and policy lives in `Security/` where the one setting of the dial is visible.
A hasher that is a **pure deterministic digest** is a utility. The test generalises past hashing:
`AesGcmEncryptionService` is `Security/` because it holds a key; `Ipv4MappedAddress` is `Utilities/`
because it holds nothing.

**A move must not change a byte.** Every type in this paragraph produces something that is stored or
compared against something stored. Relocating one is a namespace change and nothing else; if a move
tempts an edit to the encoding, the digest or the length, that edit is a separate change with a
migration attached to it, and it is not made in passing.

Three rules keep it from becoming the dumping ground the name invites:

- **`Utilities/` never holds a file directly — only subject folders.** `Mail/`, `Network/`, and the
  next one a real need produces. A folder that reads as a subject can be judged: a reviewer can say
  whether a type belongs in `Mail/`. A flat `Utilities/` cannot be judged at all, which is exactly
  how the folders this repository bans got their reputation.
- **One named type per file, as everywhere else.** `EmailAddressRule.cs`, not `MailUtils.cs`. The
  ban on `Utils.cs`/`Helpers.cs` file names is unchanged and `maran structure` still enforces it —
  this map entry names a place, it does not license a junk-drawer name inside it.
- **A new subject folder is added to the map above in the same PR that adds it.** Two entries do not
  make a pattern; the third one that lands without appearing here is where the drift starts.

The reason the folder exists at all: a module may not reference another module (rules/architecture.md),
so a general helper written inside one is a helper every other module must rewrite. That is not a
hypothetical — the panel once carried three definitions of a valid e-mail address, one per module,
and eleven controllers each spelling the caller's address out again, without the normalisation the
one considered version performs. A cross-module need moves DOWN to SharedKernel, never sideways.

**One spelling of the caller's address, mechanically.** `ClientAddress.Of` is the only place
production code turns a connection's peer into text, and `maran structure` fails the build on any
file under `backend/src` that writes `RemoteIpAddress?.ToString()` itself. The check exists because
the duplicate is invisible in review — every copy looks correct, and each one silently drops the
IPv4-mapped normalisation that splits a brute-force counter in half.

**What lives in `Maran.Host/`** (composition root; modules never reference it):

```
Maran.Host/
├── Program.cs                    # reads as a table of contents: Add* then Use*
├── ModuleRegistry.cs
├── GlobalUsings.cs
├── Configuration/                # one options class per file + its validator
├── Extensions/                   # one file per concern, named for it
│   ├── AuthenticationExtensions.cs
│   ├── AuthorizationExtensions.cs
│   ├── RateLimitingExtensions.cs
│   ├── ResilienceExtensions.cs
│   ├── LocalizationExtensions.cs
│   ├── PersistenceExtensions.cs
│   ├── MessagingExtensions.cs    # Wolverine + durable PostgreSQL queues
│   ├── ObservabilityExtensions.cs
│   ├── OpenApiExtensions.cs
│   └── ModuleExtensions.cs       # loads ModuleRegistry: services, then endpoints
├── Middleware/                   # one middleware per file, each with an Use* extension
│   ├── ExceptionMiddleware.cs        # last-resort handler -> RFC 7807, never leaks internals
│   ├── CorrelationIdMiddleware.cs    # accepts or mints the id, flows it to logs and gRPC
│   ├── RequestLocalizationMiddleware.cs  # Accept-Language -> culture for .resx lookup
│   └── AuditMiddleware.cs            # records mutating requests
├── RateLimiting/                 # one policy per file
│   ├── LoginRateLimitPolicy.cs       # per IP + username, progressive lockout
│   ├── ApiRateLimitPolicy.cs         # per authenticated account
│   └── ProvisioningRateLimitPolicy.cs# per API key
└── Resilience/                   # one pipeline per file
    ├── AgentCallPipeline.cs          # timeout + limited retry on transient agent failures
    ├── AcmePipeline.cs               # retry with backoff, respects rate limits
    └── OutboundHttpPipeline.cs       # licence server, S3, SMTP
```

Additional cross-cutting concerns, all mandatory:

- **Serilog with request logging**, configured in `ObservabilityExtensions`, every entry enriched with the correlation id. `Console.WriteLine` is forbidden.
- **Named HTTP clients with an explicit timeout AND a resilience pipeline** — registered centrally in `Extensions/HttpClientExtensions.cs`, so no outbound call can hang or fail without a policy.
- **Secret encryption at rest** — an AES-GCM `IEncryptionService` plus an EF value converter, key supplied by configuration (`/etc/maran/panel.env`, never a committed appsettings), validated at startup so a missing key fails the boot rather than the first request.
- **Options validated at startup** — every options class binds with `ValidateDataAnnotations().Validate(...).ValidateOnStart()`. A misconfigured server must not start.

Rules for this layer:

- **`Program.cs` contains no logic** — only `builder.Services.AddX()` and `app.UseX()` calls in order. Anything longer than a line moves into the matching `Extensions/` file. If `Program.cs` stops reading like a table of contents, it is wrong.
- **One concern per extension file**, named after the concern, exposing one `Add<Concern>` and/or `Use<Concern>` method. No `ServiceCollectionExtensions.cs` catch-all.
- **Every `*Extensions` type lives in `Extensions/`, always.** No exceptions, in any project: `Middleware/ExceptionMiddleware.cs` holds the middleware, `Extensions/ExceptionMiddlewareExtensions.cs` holds its `Use…` method; `Sdk/Extensions/ApiResultExtensions.cs` likewise. The suffix decides the folder, and a `static class` of extensions is a type like any other under the one-type-per-file law.
- **Rate limiting is mandatory** on authentication, the provisioning API, and any expensive operation; policies live in `RateLimiting/`, never inline in an endpoint. Repeated offenders are escalated to a firewall ban through the agent (rules/security.md).
- **Every outbound call goes through a named resilience pipeline** from `Resilience/` — the agent, ACME, the licence server, S3, SMTP. Raw `HttpClient` calls with no timeout policy are rejected; a timeout is always set.
- **Every project exposes its registrations through one `DependencyInjection.cs`** at its root, with a single `Add<Project>` extension method (`AddSharedKernel`, `AddAgentClient`). A project never asks the Host to new up its types, and the Host never registers another project's internals — that is how `Program.cs` stays a table of contents.
- **`GlobalUsings.cs` exists in every project** and holds only genuinely universal namespaces for that project (its own core namespaces, `System.*` basics, `Microsoft.Extensions.DependencyInjection` where apt). It is not a place to hide module-specific imports, and it never contains aliases that make code ambiguous to a reader.
- **Exception handling is a last resort, not a control flow.** `ExceptionMiddleware` maps anything that escapes to a 500 `ProblemDetails` with the correlation id and a localized generic message — it never returns stack traces, paths or tool output to a customer (rules/security.md).

## Errors: Result, not exceptions

Domain and agent failures flow as `Result<T>`/`Error` from `SharedKernel`. Exceptions are for bugs and infrastructure faults only.

```csharp
// RIGHT
public async Task<Result<SiteId>> HandleAsync(CreateSite cmd, CancellationToken ct)
{
    if (await _sites.DomainExistsAsync(cmd.Domain, ct))
    {
        return SiteError.DomainTaken(cmd.Domain);
    }
    ...
}

// WRONG — control flow by exception
public async Task<SiteId> HandleAsync(CreateSite cmd, CancellationToken ct)
{
    if (await _sites.DomainExistsAsync(cmd.Domain, ct))
    {
        throw new DomainTakenException(cmd.Domain); // rejected in review
    }
    ...
}
```

## Database naming: PascalCase everywhere

- **Tables and columns are PascalCase**, matching the entity and property names exactly:
  `Accounts`, `AccountId`, `PrimaryDomain`, `CreatedAt`. No snake_case, no lowercase, no
  pluralisation surprises — a reader moving between C# and `psql` sees the same identifiers.
- Schemas are the one exception: a schema is named after its module in lowercase (`accounts`,
  `sites`), because it is an infrastructure boundary rather than a mapped name.
- `ToTable("Accounts")` is written explicitly in the entity configuration; never rely on the
  provider's default. PostgreSQL folds unquoted identifiers to lowercase, so EF Core quotes
  PascalCase names — meaning hand-written SQL must quote them too (`select * from accounts."Accounts"`).
  Approved `// raw-sql:` blocks quote identifiers for this reason.
- Constraint and index names follow the same casing and state their purpose:
  `IX_Accounts_Name`, `PK_Accounts`, `FK_Accounts_Plans_PlanId`.

## Data & tenancy

- EF Core only; raw SQL requires a review-approved comment `// raw-sql: <reason>` and MUST be parameterized.
- Every tenant-owned entity carries `AccountId`; global query filters scope Customer contexts. New entities MUST be added to the filter test fixture.
- Migrations live in the owning module and target its schema only.

## Forbidden

- `#region`, partial classes (except source-generated), static mutable state, `DateTime.Now` (use `IClock`), `Task.Result`/`.Wait()`, swallowing exceptions, `dynamic`.
