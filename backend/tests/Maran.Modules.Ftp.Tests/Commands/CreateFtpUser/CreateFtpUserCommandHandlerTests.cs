using System.Text.Json;
using Maran.Modules.Ftp.Commands.CreateFtpUser;
using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Maran.Modules.Ftp.Tests.Commands.CreateFtpUser;

/// <summary>
/// Creating a customer FTPS login: the plan limit, the shared host namespace, and the password that
/// is shown once and stored nowhere.
/// </summary>
public sealed class CreateFtpUserCommandHandlerTests
{
    /// <summary>The account every test in this file creates logins for.</summary>
    private static readonly Guid AccountId = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A login the plan does not allow is refused before the agent is called at all.</summary>
    /// <remarks>
    /// The two assertions are one claim. That the result failed says the limit is enforced; that the
    /// agent recorded ZERO creations says it is enforced BEFORE the host is touched, which is the
    /// half that matters — a limit checked after provisioning has already made the thing it was
    /// supposed to prevent.
    /// </remarks>
    [Fact]
    public async Task Creating_a_login_beyond_the_plans_limit_is_refused_before_the_agent_is_called()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, "acme", maxFtpUsers: 1);
        await SeedAsync(context, "files");

        var result = await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "second"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.FtpUserLimitReached, result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(0, context.Agent.CreateUserCalls);
    }

    /// <summary>A login within the plan's allowance is created and does reach the agent.</summary>
    /// <remarks>
    /// The INVERSE CONTROL the refusing test above owes (rules/testing.md): a limit check mutated to
    /// refuse everything would satisfy every assertion that only ever feeds it a case it must
    /// refuse. This feeds it the case it must ACCEPT and asserts the host was reached exactly once.
    /// </remarks>
    [Fact]
    public async Task A_login_within_the_plans_limit_is_created_and_reaches_the_agent()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, "acme", maxFtpUsers: 2);
        await SeedAsync(context, "files");

        var result = await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "second"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, context.Agent.CreateUserCalls);
        Assert.Equal(2, await context.Database.FtpUsers.CountAsync(CancellationToken.None));
    }

    /// <summary>A plan selling no FTPS login refuses the first one.</summary>
    /// <remarks>
    /// The boundary on the other side, asserted at the exact value rather than as a bound. Zero is
    /// also what an <c>AccountSnapshot</c> built without an FTPS allowance carries, so this is the
    /// test that says such a snapshot refuses rather than grants.
    /// </remarks>
    [Fact]
    public async Task A_plan_that_allows_no_ftps_logins_refuses_the_first_one()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, "acme", maxFtpUsers: 0);

        var result = await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "files"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.FtpUserLimitReached, result.Error!.Code);
        Assert.Equal(0, context.Agent.CreateUserCalls);
    }

    /// <summary>The generated password is returned once and stored nowhere.</summary>
    /// <remarks>
    /// Two axes, because one of them can go blind. The serialization probe asserts the value is not
    /// in the row that was written — but it could only ever fail if a property existed to hold it,
    /// so on its own it certifies an absence it cannot observe. The reflection assertion is the
    /// vacuity guard on that axis: it states outright that the entity has no password-shaped member,
    /// which is the thing that would silently stop being true.
    /// </remarks>
    [Fact]
    public async Task The_generated_password_is_returned_once_and_stored_nowhere()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, "acme", maxFtpUsers: 2);

        var created = await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "files"), CancellationToken.None);

        Assert.True(created.IsSuccess);
        var password = created.Value.Password.Reveal();
        Assert.False(string.IsNullOrEmpty(password));

        var row = await context.Database.FtpUsers.SingleAsync(CancellationToken.None);
        Assert.DoesNotContain(password, JsonSerializer.Serialize(row), StringComparison.Ordinal);

        Assert.DoesNotContain(
            typeof(FtpUser).GetProperties(),
            property =>
            {
                return property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase);
            });
    }

    /// <summary>The password the panel minted is the one the agent was given.</summary>
    /// <remarks>
    /// The other half of the sentence above: showing a value once is worthless if a different value
    /// reached the host, and the customer would then hold a credential the daemon refuses. Asserts
    /// the VALUE rather than that a password-shaped thing travelled.
    /// </remarks>
    [Fact]
    public async Task The_password_the_customer_is_shown_is_the_password_the_agent_installed()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, "acme", maxFtpUsers: 2);

        var created = await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "files"), CancellationToken.None);

        Assert.Equal(created.Value.Password.Reveal(), context.Agent.LastCreateUserPassword!.Reveal());
    }

    /// <summary>A login name the host already holds is reported as taken, not as a server failure.</summary>
    /// <remarks>
    /// The host's user namespace is shared with the Sftp module and with whatever an operator made
    /// by hand, so this collision cannot be seen by the panel's own duplicate check. Untranslated it
    /// reaches the customer as a sentence about the agent, for a condition whose whole content is
    /// "pick another name".
    /// </remarks>
    [Fact]
    public async Task A_login_name_the_host_already_holds_is_reported_as_taken_not_as_a_server_failure()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, "acme", maxFtpUsers: 2);
        context.Agent.NextCreateUserError = Error.Of("AgentAlreadyExists", ErrorType.Conflict);

        var result = await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "files"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.FtpUserNameTaken, result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(await context.Database.FtpUsers.ToListAsync(CancellationToken.None));
    }

    /// <summary>Every other agent refusal keeps its own code and kind.</summary>
    /// <remarks>
    /// The inverse control on the translation above. A re-reading that rewrote EVERY agent failure
    /// as "the name is taken" would satisfy the collision test and would tell an operator whose
    /// server is broken to choose a different name.
    /// </remarks>
    [Fact]
    public async Task An_agent_failure_that_is_not_a_collision_keeps_its_own_code()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, "acme", maxFtpUsers: 2);
        context.Agent.NextCreateUserError = Error.Of("AgentSystemFailure", ErrorType.Failure);

        var result = await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "files"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
    }

    /// <summary>A name this account already holds is refused before the agent is called.</summary>
    [Fact]
    public async Task A_name_this_account_already_holds_is_refused_before_the_agent_is_called()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, "acme", maxFtpUsers: 5);
        await SeedAsync(context, "files");

        var result = await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "files"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.FtpUserNameTaken, result.Error!.Code);
        Assert.Equal(0, context.Agent.CreateUserCalls);
    }

    /// <summary>An account the caller does not own is not found, and the agent is untouched.</summary>
    /// <remarks>
    /// The directory answers <c>null</c> for another tenant's account and for an account that does
    /// not exist alike, so the refusal cannot be used to confirm a neighbour exists.
    /// </remarks>
    [Fact]
    public async Task An_account_the_caller_does_not_own_is_not_found()
    {
        using var context = CustomerContext();

        var result = await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(Guid.NewGuid(), "files"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.AccountNotFound, result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, context.Agent.CreateUserCalls);
    }

    /// <summary>A suffix that overflows the host's name ceiling once prefixed is refused here.</summary>
    /// <remarks>
    /// Answered by the panel so the customer is told what is wrong with the name they typed, rather
    /// than being handed the agent's refusal. The account name is 26 characters, the separator one
    /// and the suffix six: thirty-three, one past what <c>useradd</c> accepts.
    /// </remarks>
    [Fact]
    public async Task A_name_too_long_once_the_account_prefix_is_added_is_refused_before_the_agent_is_called()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, new string('a', 26), maxFtpUsers: 5);

        var result = await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "backup"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.FtpUserNameTooLong, result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(0, context.Agent.CreateUserCalls);
    }

    /// <summary>A name that exactly fills the host's ceiling is accepted.</summary>
    /// <remarks>
    /// The inverse control for the length gate, at the exact boundary: twenty-five, one and six is
    /// thirty-two, which is what <c>useradd</c> accepts. Without it a gate refusing everything reads
    /// as a working ceiling.
    /// </remarks>
    [Fact]
    public async Task A_name_that_exactly_fills_the_hosts_ceiling_is_accepted()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, new string('a', 25), maxFtpUsers: 5);

        var result = await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "backup"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, context.Agent.CreateUserCalls);
    }

    /// <summary>The agent is addressed by the account and the bare suffix, never a full login name.</summary>
    /// <remarks>
    /// The agent applies the prefix itself, so a request cannot express another tenant's login rather
    /// than merely being refused one. A panel that sent a fully-qualified name would give that
    /// property away.
    /// </remarks>
    [Fact]
    public async Task The_agent_is_addressed_by_the_account_and_the_bare_suffix()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, "acme", maxFtpUsers: 2);

        await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "files"), CancellationToken.None);

        Assert.Equal(new FtpsUserCall("acme", "files"), context.Agent.LastCreateUserRequest);
    }

    /// <summary>The row records the login name the agent reported, not one the panel assembled.</summary>
    /// <remarks>
    /// Rebuilding the full name here would make the row's truth depend on the panel and the agent
    /// agreeing about a separator forever, and the day they disagreed the panel would show the
    /// customer a user name their client is refused with.
    /// </remarks>
    [Fact]
    public async Task The_row_records_the_login_name_the_agent_reported()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, "acme", maxFtpUsers: 2);

        var created = await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "files"), CancellationToken.None);

        Assert.Equal("acme_files", created.Value.FullName);
        Assert.Equal("acme_files", (await context.Database.FtpUsers.SingleAsync(CancellationToken.None)).FullName);
    }

    /// <summary>The created login carries the protocol label the backend owns.</summary>
    /// <remarks>
    /// The merged "File transfer" screen renders this fact rather than deriving one from the URL it
    /// called, so the value has to be on the wire. Asserted as the exact token, because the SPA
    /// refuses a token it does not recognise and renders it as absence.
    /// </remarks>
    [Fact]
    public async Task The_created_login_carries_the_backend_owned_protocol_label()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, "acme", maxFtpUsers: 2);

        var created = await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "files"), CancellationToken.None);

        Assert.Equal("Ftps", created.Value.Protocol);
    }

    /// <summary>Creating a login records an audit entry naming it, and never its password.</summary>
    [Fact]
    public async Task Creating_a_login_records_an_audit_entry_naming_it_and_never_its_password()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, "acme", maxFtpUsers: 2);

        var created = await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "files"), CancellationToken.None);

        var entry = Assert.Single(context.Entries);
        Assert.Equal("FtpUserCreated", entry.Action);
        Assert.Equal("files", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain(created.Value.Password.Reveal(), entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A refused creation is journalled as a refusal, not left unrecorded.</summary>
    /// <remarks>
    /// Failures are the half of the journal worth reading: a plan limit hit and a cross-tenant probe
    /// are precisely the events an operator later needs, and they are the entries every early
    /// <c>return</c> walks past when the write is inline.
    /// </remarks>
    [Fact]
    public async Task A_refused_creation_records_a_failure_entry()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, "acme", maxFtpUsers: 0);

        await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "files"), CancellationToken.None);

        var entry = Assert.Single(context.Entries);
        Assert.Equal("FtpUserCreated", entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A creation that loses the race to the unique index leaves the winner's login alive.</summary>
    /// <remarks>
    /// The half of the concurrent-create story that IS closed. Two callers can both read room for
    /// one more login, and both reach the host; the second one's insert then hits the unique index
    /// on the fully-qualified name and arrives here as <c>23505</c>. The winner's row owns the
    /// login on the host and the winner has already been shown its password, which nothing can show
    /// again — so the assertion that matters is not the error code but
    /// <c>DeleteUserCalls == 0</c>: this is the ONE post-provisioning failure that must not
    /// compensate, because compensating would revoke a credential a customer already holds.
    /// Its opposite number is the test below, which asserts a delete DOES happen when nothing owns
    /// the login; the two are each other's controls, and a handler that compensated always, or
    /// never, fails one of them.
    /// </remarks>
    [Fact]
    public async Task A_creation_that_loses_the_race_to_the_unique_index_leaves_the_winners_login_alive()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, "acme", maxFtpUsers: 2);
        context.SaveFailure.Next = new DbUpdateException("insert failed", UniqueViolation());

        var result = await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "files"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.FtpUserNameTaken, result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(1, context.Agent.CreateUserCalls);
        Assert.Equal(0, context.Agent.DeleteUserCalls);
    }

    /// <summary>A creation whose row cannot be written removes the login it had already made.</summary>
    /// <remarks>
    /// Every database failure that is NOT a unique violation means no row anywhere owns the login
    /// the agent has just created — and this module keeps no copy of the password, so a retry cannot
    /// repair it either: the agent reports the second creation as already existing and deliberately
    /// leaves the credential alone. Left alone the login would sit in the host's passwd file for
    /// ever, counted against no plan, usable by nobody, and still holding a key into the account's
    /// home. The delete is asserted by its ADDRESS as well as its count, because a compensating call
    /// that named the wrong account would be a cross-tenant delete wearing the shape of a cleanup.
    /// </remarks>
    [Fact]
    public async Task A_creation_whose_row_cannot_be_written_removes_the_login_it_had_already_made()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, "acme", maxFtpUsers: 2);
        context.SaveFailure.Next = new DbUpdateException(
            "the connection was lost", new InvalidOperationException("the connection was lost"));

        var result = await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "files"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.FtpUserProvisioningFailed, result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        Assert.Equal(1, context.Agent.DeleteUserCalls);
        Assert.Equal(new FtpsUserCall("acme", "files"), context.Agent.LastDeleteUserRequest);
    }

    /// <summary>A creation the slot gate refuses is removed from the host and reported as the plan filling up.</summary>
    /// <remarks>
    /// The branch a concurrent creation produces: the pre-agent check saw room, the login was made,
    /// and by the time the row was written another request had committed the account's last slot. The
    /// login nothing owns must go — asserted by the delete's ADDRESS as well as its count, because a
    /// compensating call naming the wrong account would be a cross-tenant delete wearing the shape of
    /// a cleanup — and no row may be left behind. The CODE is the other half of the claim: this
    /// refusal is not <c>FtpUserNameTaken</c>, because the name is not what is wrong and a customer
    /// told to pick another name would retype their way nowhere.
    /// </remarks>
    [Fact]
    public async Task A_creation_the_slot_gate_refuses_is_removed_from_the_host_and_reported_as_the_plan_filling_up()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, "acme", maxFtpUsers: 2);
        context.SlotGate.RefuseNextClaim = true;

        var result = await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "files"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.FtpUserLimitReachedConcurrently, result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(1, context.Agent.CreateUserCalls);
        Assert.Equal(1, context.Agent.DeleteUserCalls);
        Assert.Equal(new FtpsUserCall("acme", "files"), context.Agent.LastDeleteUserRequest);
        Assert.Equal(0, await context.Database.FtpUsers.CountAsync(CancellationToken.None));
    }

    /// <summary>A creation the slot gate accepts writes exactly one row and deletes nothing.</summary>
    /// <remarks>
    /// The INVERSE CONTROL for the test above (rules/testing.md): a gate mutated to refuse everything,
    /// or a handler that compensated on every path, would satisfy every assertion that only ever feeds
    /// it the refusing case. The zero delete count is the load-bearing half — a customer whose login
    /// was created and then silently removed would be shown a password for an account that no longer
    /// exists.
    /// </remarks>
    [Fact]
    public async Task A_creation_the_slot_gate_accepts_writes_one_row_and_deletes_nothing()
    {
        using var context = CustomerContext();
        context.Accounts.Add(AccountId, "acme", maxFtpUsers: 2);

        var result = await context.CreateUserHandler.HandleAsync(
            new CreateFtpUserCommand(AccountId, "files"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, context.Agent.DeleteUserCalls);
        Assert.Equal(1, await context.Database.FtpUsers.CountAsync(CancellationToken.None));
    }

    /// <summary>Builds the database failure PostgreSQL raises for a duplicate key.</summary>
    /// <returns>A <see cref="PostgresException"/> carrying SqlState <c>23505</c>.</returns>
    /// <remarks>
    /// The SqlState is the whole of what the handler reads, and it is written here as
    /// <see cref="PostgresErrorCodes.UniqueViolation"/> rather than as the literal <c>"23505"</c> so
    /// that this test and the production branch cannot come to disagree about which string that is.
    /// </remarks>
    private static PostgresException UniqueViolation()
    {
        return new PostgresException(
            "duplicate key value violates unique constraint \"IX_FtpUsers_FullName\"",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation);
    }

    /// <summary>Builds a context whose principal is the customer that owns <see cref="AccountId"/>.</summary>
    /// <returns>The assembled module, seen as that customer.</returns>
    private static FtpTestContext CustomerContext()
    {
        return new FtpTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
    }

    /// <summary>Writes one existing login for <see cref="AccountId"/> straight into the database.</summary>
    /// <param name="context">The assembled module whose database receives the row.</param>
    /// <param name="name">The login's suffix.</param>
    private static async Task SeedAsync(FtpTestContext context, string name)
    {
        context.Database.FtpUsers.Add(
            new FtpUser(Guid.NewGuid(), AccountId, name, $"acme_{name}", context.Clock.UtcNow));
        await context.Database.SaveChangesAsync(CancellationToken.None);
    }
}
