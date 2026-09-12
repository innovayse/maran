using Maran.Modules.Ftp.Commands.CreateFtpUser;
using Maran.Modules.Ftp.Commands.DeleteFtpUser;
using Maran.Modules.Ftp.Commands.DisableFtps;
using Maran.Modules.Ftp.Commands.EnableFtps;
using Maran.Modules.Ftp.Commands.ResetFtpUserPassword;
using Maran.Modules.Ftp.IntegrationEvents.Handlers;
using Maran.Modules.Ftp.Persistence;
using Maran.Modules.Ftp.Queries.GetFtpsStatus;
using Maran.Modules.Ftp.Queries.GetFtpUser;
using Maran.Modules.Ftp.Queries.ListFtpUsers;
using Maran.Modules.Ftp.Services;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Ftp.Tests.TestSupport;

/// <summary>
/// One assembled Ftp module for a test: an in-memory database, a recording agent, a recording
/// journal, a stub site directory and a fixed clock, with every handler built over them.
/// </summary>
/// <remarks>
/// The database name is unique per instance, so tests share no rows and can run in any order
/// (rules/testing.md "Determinism"). Nothing here is a mock with expectations: each double RECORDS,
/// and the assertions read what was recorded — a handler that stops calling the agent fails on the
/// recording being empty rather than on a verification step somebody remembered to write.
/// </remarks>
public sealed class FtpTestContext : IDisposable
{
    /// <summary>The module's database, in memory and private to this test.</summary>
    public FtpDbContext Database { get; }

    /// <summary>The agent double, which records what the panel sent.</summary>
    public RecordingFtpsAgent Agent { get; } = new();

    /// <summary>The audit journal's backing writer, which records every entry.</summary>
    public RecordingAuditWriter Audit { get; } = new();

    /// <summary>The site directory double, which decides whether the panel serves a hostname.</summary>
    public StubSiteDirectory Sites { get; } = new();

    /// <summary>The account directory double: system user names and FTPS-login allowances.</summary>
    public StubAccountDirectory Accounts { get; } = new();

    /// <summary>The in-memory database this context reads and writes, so a second one can join it.</summary>
    /// <remarks>
    /// Exposed because the tenant filter is only measurable across TWO principals over ONE database:
    /// customer A's rows have to be visible to A's context and absent from B's, and a private
    /// database per context would make every such test pass vacuously.
    /// </remarks>
    public string DatabaseName { get; }

    /// <summary>The fixed clock every write is stamped from.</summary>
    public FixedClock Clock { get; } = new();

    /// <summary>The save interceptor a test arms to make the next database write fail.</summary>
    /// <remarks>
    /// Disarmed unless a test sets <see cref="ArmedSaveFailureInterceptor.Next"/>, so every other
    /// test in this project is unaffected by its presence. It is here because the create handler's
    /// two post-provisioning branches — a lost race, and a write that failed for any other reason —
    /// cannot be reached over the in-memory provider by arranging rows.
    /// </remarks>
    public ArmedSaveFailureInterceptor SaveFailure { get; } = new();

    /// <summary>The slot gate stand-in the create handler claims through.</summary>
    /// <remarks>
    /// A test sets <see cref="LocklessFtpUserSlotGate.RefuseNextClaim"/> to reach the handler's late
    /// refusal — the branch a concurrent creation produces in production. What the real gate adds is
    /// stated on the stand-in and is measured against real PostgreSQL elsewhere.
    /// </remarks>
    public LocklessFtpUserSlotGate SlotGate { get; }

    /// <summary>The enable handler under test.</summary>
    public EnableFtpsCommandHandler EnableHandler { get; }

    /// <summary>The disable handler under test.</summary>
    public DisableFtpsCommandHandler DisableHandler { get; }

    /// <summary>The status query handler under test.</summary>
    public GetFtpsStatusQueryHandler StatusHandler { get; }

    /// <summary>The certificate-driven reloader under test.</summary>
    public FtpsTlsReloader Reloader { get; }

    /// <summary>The login-creation handler under test.</summary>
    public CreateFtpUserCommandHandler CreateUserHandler { get; }

    /// <summary>The login-deletion handler under test.</summary>
    public DeleteFtpUserCommandHandler DeleteUserHandler { get; }

    /// <summary>The password-reset handler under test.</summary>
    public ResetFtpUserPasswordCommandHandler ResetPasswordHandler { get; }

    /// <summary>The login listing handler under test.</summary>
    public ListFtpUsersQueryHandler ListUsersHandler { get; }

    /// <summary>The single-login read handler under test.</summary>
    public GetFtpUserQueryHandler GetUserHandler { get; }

    /// <summary>The account-deletion row cleanup under test.</summary>
    public AccountDeletingHandler AccountDeletingHandler { get; }

    /// <summary>The audit entries written so far.</summary>
    public IReadOnlyList<AuditEntry> Entries
    {
        get
        {
            return Audit.Entries;
        }
    }

    /// <summary>Builds the module as an ADMINISTRATOR, with its own private in-memory database.</summary>
    /// <remarks>
    /// The default because the module's server-level half is <c>AdminOnly</c> and a customer
    /// principal could never have reached those handlers. The login half is reached by customers and
    /// is exercised through the other constructor.
    /// </remarks>
    public FtpTestContext()
        : this(new TestCurrentUser(), Guid.NewGuid().ToString())
    {
    }

    /// <summary>Builds the module as one principal over one named in-memory database.</summary>
    /// <param name="principal">
    /// Who the request is from. The tenant query filter on the login rows closes over this, so it is
    /// what decides which rows the handlers built here can see at all.
    /// </param>
    /// <param name="databaseName">
    /// Which in-memory database to attach to. Two contexts given the same name share rows, which is
    /// how a cross-tenant test arranges customer B's login and then asks customer A for it.
    /// </param>
    public FtpTestContext(ICurrentUser principal, string databaseName)
    {
        DatabaseName = databaseName;

        var options = new DbContextOptionsBuilder<FtpDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(SaveFailure)
            .Options;

        Database = new FtpDbContext(options, principal);

        var journal = new FtpAuditJournal(Audit, principal);
        SlotGate = new LocklessFtpUserSlotGate(Database);

        EnableHandler = new EnableFtpsCommandHandler(Database, Sites, Agent, journal, Clock);
        DisableHandler = new DisableFtpsCommandHandler(Database, Agent, journal, Clock);
        StatusHandler = new GetFtpsStatusQueryHandler(Database, Agent);
        Reloader = new FtpsTlsReloader(Database, Agent, journal, NullLogger<FtpsTlsReloader>.Instance);

        CreateUserHandler = new CreateFtpUserCommandHandler(
            Database, Accounts, Agent, SlotGate, journal, Clock, NullLogger<CreateFtpUserCommandHandler>.Instance);
        DeleteUserHandler = new DeleteFtpUserCommandHandler(Database, Accounts, Agent, journal);
        ResetPasswordHandler = new ResetFtpUserPasswordCommandHandler(Database, Accounts, Agent, journal);
        ListUsersHandler = new ListFtpUsersQueryHandler(Database);
        GetUserHandler = new GetFtpUserQueryHandler(Database);
        AccountDeletingHandler = new AccountDeletingHandler(Database);
    }

    /// <summary>Disposes the in-memory database.</summary>
    public void Dispose()
    {
        Database.Dispose();
    }
}
