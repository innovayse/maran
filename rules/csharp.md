# C# Rules

Normative. Enforced by `.editorconfig`, `Directory.Build.props` (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<Nullable>enable</Nullable>`) and review.

## Formatting & naming

- 4 spaces, 120 columns, LF, final newline. File-scoped namespaces only.
- `var` when the type is apparent from the right-hand side; explicit type otherwise.
- Braces always, even for single-line `if`.
- `_camelCase` private fields, `PascalCase` everything public, `IPascalCase` interfaces, `Async` suffix on async methods.
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
- Folder path mirrors the namespace exactly (`Features/CreateSite/` ⇔ `…Features.CreateSite`). A type in the wrong folder is a review reject even if it compiles.
- No `Utils.cs`, `Helpers.cs`, `Extensions.cs` dumping grounds — every helper has a named home describing its single purpose.

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
│   │   ├── Security/                    # encryption implementations + EF value converters (contracts in Interfaces/)
│   │   └── Time/                        # SystemClock.cs
│   ├── Maran.Sdk/                   # the module contract consumed by paid modules too
│   │   ├── Interfaces/                  # IPanelModule.cs — the module contract
│   │   ├── Controllers/                 # BaseApiController (+ controller-scoped filters)
│   │   ├── Extensions/                  # ApiResultExtensions and other Sdk extensions
│   │   ├── Permissions/                 # permission constants per area, one file each
│   │   ├── Navigation/                  # menu contribution types
│   │   ├── Filters/                     # action filters modules may opt into
│   │   ├── Contracts/                   # cross-module contract types
│   │   ├── Events/                      # integration-event base types (Base/ + per area)
│   ├── Maran.Agent.Client/          # the ONLY project generating agent gRPC code
│   │   ├── DependencyInjection.cs       # AddAgentClient — the project's registrations
│   │   ├── Channels/                    # AgentChannel.cs — unix-socket channel construction
│   │   └── Services/<Proto>Service/      # one folder per proto service: client, seam, DTOs
│   │                                    # (SystemService/, SitesService/, SslService/, …)
│   └── Maran.Modules/               # grouping folder for all module projects
│       └── <Name>/                      # short folder (Sites/, Accounts/…); the project inside
│                                        #   is the full Maran.Modules.<Name>.csproj
│           ├── <Name>Module.cs          # IPanelModule: DI, controllers, permissions, menu, migrations
│           ├── <Name>Manifest.cs        # module identity: id, display-name resource key,
│           │                            #   version, licence tier, dependencies
│           ├── Controllers/             # thin HTTP surface (+ External/ for outward APIs)
│           │   ├── <Resource>Controller.cs
│           │   └── Requests/            # request models bound from HTTP, one per file
│           ├── Commands/<Operation>/    # <Op>Command.cs, <Op>CommandHandler.cs, <Op>CommandValidator.cs
│           ├── Queries/<Operation>/     # <Op>Query.cs, <Op>QueryHandler.cs (+ <Op>QueryValidator.cs)
│           ├── Common/                  # *Dto.cs + precisely-named module-shared types
│           │   ├── Interfaces/          # module-internal contracts
│           │   ├── Options/             # <Feature>Options — typed settings, validated at startup
│           │   └── Validators/          # validators for options and shared inputs
│           ├── IntegrationEvents/
│           │   ├── Events/              # events this module publishes for others
│           │   └── Handlers/            # handlers for other modules' events
│           ├── Services/                # domain services of this module
│           ├── Jobs/                    # scheduled and recurring work (Wolverine)
│           ├── Authorization/           # permission requirements and handlers
│           ├── Domain/                  # entities and value objects, one per file
│           │   ├── Events/              # domain events raised by those entities
│           │   └── Interfaces/          # domain-level contracts (e.g. repositories)
│           ├── Persistence/
│           │   ├── <Name>DbContext.cs   # + DesignTimeDbContextFactory.cs
│           │   ├── Configurations/      # <Entity>Configuration.cs — one per entity
│           │   ├── Interceptors/        # SaveChanges interceptors (audit, timestamps)
│           │   └── Migrations/          # EF-generated, module-scoped
│           ├── Seeders/                 # initial data owned by this module
│           ├── Resources/               # Messages.resx, Messages.ru.resx, Messages.hy.resx
│           └── Errors/                  # <Name>Errors.cs — the module's error codes
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
    public async Task<IActionResult> GetAllAsync([FromQuery] GetAuditLogsQuery query, CancellationToken ct) =>
        Ok(await Bus.InvokeAsync<PagedResult<AuditLogDto>>(query, ct));
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
- **Resources are reached through `IStringLocalizer<T>`**, where `T` is an empty marker class named
  after the resource file (`ErrorMessages.cs` next to `ErrorMessages.resx`). Typed access beats
  passing a `ResourceManager` around: the localizer resolves the request culture on its own and the
  marker makes the dependency visible in a constructor signature.
- **One resource file per purpose, named for it** — not one catch-all per module:
  `ErrorMessages` (domain failures surfaced as error codes), `ValidationMessages` (validator
  output), `DisplayNames` (module, plan and other user-facing names), `EmailTemplates`,
  `NotificationMessages`. A file appears when its purpose does; do not create empty ones.
- Each file is a triple in the module's `Resources/` folder: `X.resx` (English, the invariant),
  `X.ru.resx`, `X.hy.resx` — identical key sets, verified by a test. Entries stay minimal:
  `<data name="AccountNotFound"><value>…</value></data>`, no comments, no `xml:space` noise.
- **Standard file suffixes** (one concept, one suffix — no synonyms): `…Command/…Query`, `…CommandHandler/…QueryHandler`, `…CommandValidator/…QueryValidator`, `…Dto`, `…Controller`, `…Configuration` (EF), `…Options`, `…Service`, `…Seeder`, `…EventHandler`, `…Interceptor`, `…Middleware`, `…Policy`, `…Extensions`.
- **No dumping grounds inside a module** — no `Services/`, `Managers/`, `Helpers/`, `Misc/`, `Core/`. `Common/` exists for types genuinely shared across operations of that one module, and holds only precisely-named types (`PagedSiteResult.cs`, `SitePathBuilder.cs`); a file named `Common.cs`, `Helpers.cs` or `Shared.cs` inside it is a review reject. Anything shared by two modules moves down to `SharedKernel` or `Sdk` — never sideways between modules.
- **Test projects mirror source paths exactly**: `Commands/CreateSite/CreateSiteHandlerTests.cs` sits at the same relative path as the code it covers.
- **Namespace = path, always**: `Maran.Modules.Sites.Commands.CreateSite`. No namespace shortcuts, no folder outside the namespace.
- **New shapes extend the map, they don't bypass it.** A genuinely new kind of file gets a named folder here first — inventing an ad-hoc location is rejected, and so is a temporary hack "until we tidy it later".

## Doc comments — mandatory for ALL code

XML docs are REQUIRED on **every type and every member — public, internal, protected, and private alike** — in all production code. Not just the SDK surface: handlers, validators, private helpers, fields with non-obvious meaning. Test code is exempt (the behavior-sentence test name is its documentation, see rules/testing.md). Say what the caller needs, not what the code does line by line.

```csharp
/// <summary>
/// Creates a hosting account: system user, home directory, quota, and the customer login.
/// Idempotent: returns <see cref="AccountError.AlreadyExists"/> for a duplicate name.
/// </summary>
/// <param name="command">Validated account parameters; see <see cref="CreateAccountValidator"/>.</param>
/// <returns>The created account id, or a typed error.</returns>
public Task<Result<AccountId>> HandleAsync(CreateAccount command, CancellationToken ct);
```

## The backend owns all user-facing message text (.resx)

The frontend never translates server outcomes — it displays what we send. That makes localization a backend responsibility, enforced here:

- Every user-facing message lives in **`.resx` resource files inside its owning module** (`Resources/Messages.resx`, `Messages.ru.resx`, `Messages.hy.resx`). Invariant `.resx` is English.
- **Hardcoded user-facing strings in C# are a review reject.** A message reaching a customer or an administrator through the API comes from a resource lookup, never from a string literal in a handler.
- Requests carry the user's language (`Accept-Language`, falling back to the account's stored preference, then English). The localization middleware sets the culture; error responses are rendered in that culture.
- `Error.Code` stays machine-stable and untranslated (`sites.domain_taken`) — it drives behavior. The resource entry keyed by that code supplies the human text placed in the RFC 7807 `title`/`detail`.
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
└── GlobalUsings.cs
```

`BaseApiController` carries what every module controller needs and nothing else: `[ApiController]`, the `api/v1/[controller]` route convention, the current user, the correlation id, and the `Result`→HTTP translation. **A module controller inherits it — never `ControllerBase` directly**, and never re-implements result translation.

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

## Vertical slice shape

One use case = one folder `Features/<UseCase>/` = request + validator + handler + endpoint. Small enough to read whole.

```csharp
namespace Maran.Modules.Sites.Features.CreateSite;

/// <summary>Creates a site under an account and provisions its nginx vhost.</summary>
public sealed record CreateSite(AccountId AccountId, string Domain, PhpVersion Php);

public sealed class CreateSiteValidator : AbstractValidator<CreateSite>
{
    public CreateSiteValidator()
    {
        RuleFor(x => x.Domain).MustBeValidDomainName();
    }
}

public static class CreateSiteEndpoint
{
    /// <summary>POST /api/v1/sites — permission: sites.create, scoped to the caller's account.</summary>
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/sites", async (CreateSite cmd, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<SiteId>>(cmd, ct)).ToHttpResult())
            .RequirePermission(Permissions.Sites.Create);
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
