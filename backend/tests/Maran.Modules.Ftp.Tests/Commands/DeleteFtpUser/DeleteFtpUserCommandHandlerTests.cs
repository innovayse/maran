using Maran.Modules.Ftp.Commands.DeleteFtpUser;
using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Ftp.Tests.Commands.DeleteFtpUser;

/// <summary>
/// Removing a customer FTPS login: whose login it is, how a neighbour's is refused, and what the
/// agent is told.
/// </summary>
public sealed class DeleteFtpUserCommandHandlerTests
{
    /// <summary>The account whose logins the caller in these tests owns.</summary>
    private static readonly Guid CustomerA = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A different account, whose logins the caller must never be able to touch.</summary>
    private static readonly Guid CustomerB = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>Another account's login is not found rather than forbidden, and the host is untouched.</summary>
    /// <remarks>
    /// The refusal has to be INDISTINGUISHABLE from the refusal for an identifier that names nothing
    /// — a distinct answer would confirm a neighbouring tenant holds that login — which is why this
    /// asserts the same code the missing-login test asserts. The zero agent call is the destructive
    /// half: a cross-tenant delete that answered 404 after removing the login would satisfy a
    /// status-only assertion. This branch has already shipped that defect once, in the agent.
    /// </remarks>
    [Fact]
    public async Task Another_accounts_login_is_not_found_rather_than_forbidden()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var owner = Context(CustomerB, databaseName);
        var victim = await SeedAsync(owner, CustomerB, "files");

        using var attacker = Context(CustomerA, databaseName);
        attacker.Accounts.Add(CustomerA, "acme", maxFtpUsers: 5);
        attacker.Accounts.Add(CustomerB, "beta", maxFtpUsers: 5);

        var result = await attacker.DeleteUserHandler.HandleAsync(
            new DeleteFtpUserCommand(victim, "203.0.113.9", "agent"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.FtpUserNotFound, result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, attacker.Agent.DeleteUserCalls);
        Assert.Equal(1, await owner.Database.FtpUsers.CountAsync(CancellationToken.None));
    }

    /// <summary>An identifier naming nothing gets exactly the same answer as a neighbour's login.</summary>
    /// <remarks>
    /// The other half of the sentence above, and the one that makes it a property rather than a
    /// coincidence: the two refusals are asserted to be the same code and the same kind, so a change
    /// that gave the cross-tenant case its own answer fails here as well.
    /// </remarks>
    [Fact]
    public async Task A_login_that_does_not_exist_is_refused_exactly_as_a_neighbours_is()
    {
        using var context = Context(CustomerA, Guid.NewGuid().ToString());

        var result = await context.DeleteUserHandler.HandleAsync(
            new DeleteFtpUserCommand(Guid.NewGuid(), "203.0.113.9", "agent"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.FtpUserNotFound, result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, context.Agent.DeleteUserCalls);
    }

    /// <summary>The caller's own login is removed, from the host first and then from the panel.</summary>
    /// <remarks>
    /// The INVERSE CONTROL for both refusals above: a handler that answered "not found" to
    /// everything would satisfy them and delete nothing anybody asked to delete.
    /// </remarks>
    [Fact]
    public async Task The_callers_own_login_is_removed_from_the_host_and_from_the_panel()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var context = Context(CustomerA, databaseName);
        context.Accounts.Add(CustomerA, "acme", maxFtpUsers: 5);
        var id = await SeedAsync(context, CustomerA, "files");

        var result = await context.DeleteUserHandler.HandleAsync(
            new DeleteFtpUserCommand(id, "203.0.113.9", "agent"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, context.Agent.DeleteUserCalls);
        Assert.Empty(await context.Database.FtpUsers.ToListAsync(CancellationToken.None));
    }

    /// <summary>The agent is addressed by the ROW'S OWN account and the bare suffix.</summary>
    /// <remarks>
    /// The panel's filter and the agent's jail check are two independent closures of the same hole,
    /// and this is what keeps the second one usable: the agent can only confirm the login's passwd
    /// home is the account's jail if it is told which account, and it can only apply the prefix
    /// itself if it is given a suffix. A fully-qualified name here would hand the decision back to
    /// name parsing, which has no unique decomposition.
    /// </remarks>
    [Fact]
    public async Task The_agent_is_addressed_by_the_rows_own_account_and_the_bare_suffix()
    {
        using var context = Context(CustomerA, Guid.NewGuid().ToString());
        context.Accounts.Add(CustomerA, "acme", maxFtpUsers: 5);
        var id = await SeedAsync(context, CustomerA, "files");

        await context.DeleteUserHandler.HandleAsync(
            new DeleteFtpUserCommand(id, "203.0.113.9", "agent"), CancellationToken.None);

        Assert.Equal(new FtpsUserCall("acme", "files"), context.Agent.LastDeleteUserRequest);
    }

    /// <summary>A refused agent delete leaves the row, so the login stays visible and retryable.</summary>
    /// <remarks>
    /// The order is what this asserts. A row removed while the login still authenticates is a live
    /// credential into a customer's home that nobody in the panel can see and nobody can now revoke
    /// — and the customer asked for exactly that access to end.
    /// </remarks>
    [Fact]
    public async Task A_refused_agent_delete_leaves_the_row_where_the_panel_can_still_see_it()
    {
        using var context = Context(CustomerA, Guid.NewGuid().ToString());
        context.Accounts.Add(CustomerA, "acme", maxFtpUsers: 5);
        var id = await SeedAsync(context, CustomerA, "files");
        context.Agent.NextDeleteUserError = Error.Of("AgentSystemFailure", ErrorType.Failure);

        var result = await context.DeleteUserHandler.HandleAsync(
            new DeleteFtpUserCommand(id, "203.0.113.9", "agent"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Equal(1, await context.Database.FtpUsers.CountAsync(CancellationToken.None));
    }

    /// <summary>A row whose account cannot be resolved is refused as a missing ACCOUNT, not sent on.</summary>
    /// <remarks>
    /// <para>
    /// The refusal a customer reads has to name the right thing. The handler used to substitute a
    /// placeholder user name for an account the directory could not resolve and call the agent with
    /// it; the agent's only possible answer was NOT_FOUND, which renders as "this no longer exists on
    /// your server, refresh the page" — wrong about the login, which is still there, and wrong about
    /// the remedy, since the refresh it advises brings back the row the customer just failed to
    /// remove. The placeholder was also a legal account name, so the panel was asking the agent to
    /// delete a login in whatever namespace that name happened to address.
    /// </para>
    /// <para>
    /// <c>DeleteUserCalls == 0</c> is the load-bearing half: a handler that answered
    /// <c>AccountNotFound</c> AFTER calling the agent would satisfy a code-only assertion while still
    /// having addressed a stranger's namespace.
    /// <see cref="The_agent_is_addressed_by_the_rows_own_account_and_the_bare_suffix"/> and
    /// <see cref="The_callers_own_login_is_removed_from_the_host_and_from_the_panel"/> are the
    /// inverse control: the same row with the account registered is deleted and the agent IS called,
    /// so a handler mutated to refuse every delete fails there.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_login_whose_account_cannot_be_resolved_is_refused_as_a_missing_account()
    {
        using var context = Context(CustomerA, Guid.NewGuid().ToString());
        var id = await SeedAsync(context, CustomerA, "files");

        var result = await context.DeleteUserHandler.HandleAsync(
            new DeleteFtpUserCommand(id, "203.0.113.9", "agent"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.AccountNotFound, result.Error!.Code);
        Assert.NotEqual(ErrorCodes.FtpUserNotFound, result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, context.Agent.DeleteUserCalls);
        Assert.Equal(1, await context.Database.FtpUsers.CountAsync(CancellationToken.None));
    }

    /// <summary>The unresolved-account refusal is journalled by the login's name, which IS known.</summary>
    /// <remarks>
    /// Unlike the probe below, the row was found, so the journal records the name rather than the
    /// identifier — an operator reading the trail sees which login could not be removed.
    /// </remarks>
    [Fact]
    public async Task An_unresolved_account_refusal_is_journalled_by_the_logins_name()
    {
        using var context = Context(CustomerA, Guid.NewGuid().ToString());
        var id = await SeedAsync(context, CustomerA, "files");

        await context.DeleteUserHandler.HandleAsync(
            new DeleteFtpUserCommand(id, "203.0.113.9", "agent"), CancellationToken.None);

        var entry = Assert.Single(context.Entries);
        Assert.Equal("FtpUserDeleted", entry.Action);
        Assert.Equal("files", entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A probe for a login the caller may not see leaves a trace naming what was probed for.</summary>
    /// <remarks>
    /// The journal is the ONLY place an operator's typo and an attacker's enumeration are told
    /// apart, because the answer to both is deliberately the same. The subject is the identifier the
    /// caller supplied, since no name is known.
    /// </remarks>
    [Fact]
    public async Task A_probe_for_a_login_the_caller_may_not_see_is_journalled_with_what_was_probed_for()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var owner = Context(CustomerB, databaseName);
        var victim = await SeedAsync(owner, CustomerB, "files");

        using var attacker = Context(CustomerA, databaseName);

        await attacker.DeleteUserHandler.HandleAsync(
            new DeleteFtpUserCommand(victim, "203.0.113.9", "agent"), CancellationToken.None);

        var entry = Assert.Single(attacker.Entries);
        Assert.Equal("FtpUserDeleted", entry.Action);
        Assert.Equal(victim.ToString(), entry.Subject);
        Assert.False(entry.Succeeded);
        Assert.Equal("203.0.113.9", entry.IpAddress);
    }

    /// <summary>A completed delete is journalled by the login's name, captured before the row goes.</summary>
    [Fact]
    public async Task A_completed_delete_is_journalled_by_the_logins_name()
    {
        using var context = Context(CustomerA, Guid.NewGuid().ToString());
        context.Accounts.Add(CustomerA, "acme", maxFtpUsers: 5);
        var id = await SeedAsync(context, CustomerA, "files");

        await context.DeleteUserHandler.HandleAsync(
            new DeleteFtpUserCommand(id, "203.0.113.9", "agent"), CancellationToken.None);

        var entry = Assert.Single(context.Entries);
        Assert.Equal("FtpUserDeleted", entry.Action);
        Assert.Equal("files", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Builds a context seen by one customer, over a named database two contexts can share.</summary>
    /// <param name="accountId">The account the principal owns.</param>
    /// <param name="databaseName">The in-memory database to attach to.</param>
    /// <returns>The assembled module, seen as that customer.</returns>
    private static FtpTestContext Context(Guid accountId, string databaseName)
    {
        return new FtpTestContext(new CustomerCurrentUser(accountId), databaseName);
    }

    /// <summary>Writes one login for an account straight into the database.</summary>
    /// <param name="context">The assembled module whose database receives the row.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="name">The login's suffix.</param>
    /// <returns>The new row's identity.</returns>
    private static async Task<Guid> SeedAsync(FtpTestContext context, Guid accountId, string name)
    {
        var id = Guid.NewGuid();
        var username = accountId == CustomerA ? "acme" : "beta";

        context.Database.FtpUsers.Add(
            new FtpUser(id, accountId, name, $"{username}_{name}", context.Clock.UtcNow));
        await context.Database.SaveChangesAsync(CancellationToken.None);

        return id;
    }
}
