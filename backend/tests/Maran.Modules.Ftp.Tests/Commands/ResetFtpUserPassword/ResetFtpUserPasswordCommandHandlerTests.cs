using Maran.Modules.Ftp.Commands.ResetFtpUserPassword;
using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Ftp.Tests.Commands.ResetFtpUserPassword;

/// <summary>
/// Re-credentialling a customer FTPS login: the only recovery a lost password has, and the endpoint
/// that would hand out a working credential if it could be pointed at somebody else's row.
/// </summary>
public sealed class ResetFtpUserPasswordCommandHandlerTests
{
    /// <summary>The account whose logins the caller in these tests owns.</summary>
    private static readonly Guid CustomerA = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A different account, whose logins the caller must never be able to re-credential.</summary>
    private static readonly Guid CustomerB = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>Another account's login is not found rather than forbidden, and no password is set.</summary>
    /// <remarks>
    /// The most dangerous of the module's endpoints to get wrong: pointed at a neighbour's row it
    /// would not merely disclose that the login exists, it would hand the caller a working
    /// credential into that neighbour's home directory. So both halves are asserted — the answer is
    /// "not found", and the agent was never asked to install anything.
    /// </remarks>
    [Fact]
    public async Task Another_accounts_login_is_not_found_rather_than_forbidden()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var owner = Context(CustomerB, databaseName);
        var victim = await SeedAsync(owner, CustomerB, "files");

        using var attacker = Context(CustomerA, databaseName);
        attacker.Accounts.Add(CustomerB, "beta", maxFtpUsers: 5);

        var result = await attacker.ResetPasswordHandler.HandleAsync(
            new ResetFtpUserPasswordCommand(victim, "203.0.113.9", "agent"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.FtpUserNotFound, result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, attacker.Agent.SetPasswordCalls);
    }

    /// <summary>The caller's own login is re-credentialled and the new value returned once.</summary>
    /// <remarks>
    /// The inverse control: a handler refusing everything satisfies the test above and leaves every
    /// customer who lost a password with no way back.
    /// </remarks>
    [Fact]
    public async Task The_callers_own_login_is_recredentialled_and_the_value_returned_once()
    {
        using var context = Context(CustomerA, Guid.NewGuid().ToString());
        context.Accounts.Add(CustomerA, "acme", maxFtpUsers: 5);
        var id = await SeedAsync(context, CustomerA, "files");

        var result = await context.ResetPasswordHandler.HandleAsync(
            new ResetFtpUserPasswordCommand(id, "203.0.113.9", "agent"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(id, result.Value.Id);
        Assert.Equal("acme_files", result.Value.FullName);
        Assert.False(string.IsNullOrEmpty(result.Value.Password.Reveal()));
        Assert.Equal(1, context.Agent.SetPasswordCalls);
    }

    /// <summary>The value the customer is shown is the value the agent installed.</summary>
    /// <remarks>
    /// Asserts the VALUE, not that a password-shaped thing travelled: a reset that returned one
    /// string and installed another would leave the customer holding a credential the daemon
    /// refuses, and every count-based assertion would still be green.
    /// </remarks>
    [Fact]
    public async Task The_value_the_customer_is_shown_is_the_value_the_agent_installed()
    {
        using var context = Context(CustomerA, Guid.NewGuid().ToString());
        context.Accounts.Add(CustomerA, "acme", maxFtpUsers: 5);
        var id = await SeedAsync(context, CustomerA, "files");

        var result = await context.ResetPasswordHandler.HandleAsync(
            new ResetFtpUserPasswordCommand(id, "203.0.113.9", "agent"), CancellationToken.None);

        Assert.Equal(result.Value.Password.Reveal(), context.Agent.LastSetPasswordValue!.Reveal());
    }

    /// <summary>The agent is addressed by the row's own account and the bare suffix.</summary>
    [Fact]
    public async Task The_agent_is_addressed_by_the_rows_own_account_and_the_bare_suffix()
    {
        using var context = Context(CustomerA, Guid.NewGuid().ToString());
        context.Accounts.Add(CustomerA, "acme", maxFtpUsers: 5);
        var id = await SeedAsync(context, CustomerA, "files");

        await context.ResetPasswordHandler.HandleAsync(
            new ResetFtpUserPasswordCommand(id, "203.0.113.9", "agent"), CancellationToken.None);

        Assert.Equal(new FtpsUserCall("acme", "files"), context.Agent.LastSetPasswordRequest);
    }

    /// <summary>The journal records the login's name and never the password that was set.</summary>
    /// <remarks>
    /// An entry naming which credential was installed would be the journal keeping the copy this
    /// whole module takes such trouble not to keep — and the journal is never deleted.
    /// </remarks>
    [Fact]
    public async Task The_journal_records_the_logins_name_and_never_the_password()
    {
        using var context = Context(CustomerA, Guid.NewGuid().ToString());
        context.Accounts.Add(CustomerA, "acme", maxFtpUsers: 5);
        var id = await SeedAsync(context, CustomerA, "files");

        var result = await context.ResetPasswordHandler.HandleAsync(
            new ResetFtpUserPasswordCommand(id, "203.0.113.9", "agent"), CancellationToken.None);

        var entry = Assert.Single(context.Entries);
        Assert.Equal("FtpUserPasswordReset", entry.Action);
        Assert.Equal("files", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain(result.Value.Password.Reveal(), entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A reset writes no row: there is no password column for it to write to.</summary>
    /// <remarks>
    /// Stated as an assertion rather than left to the reader of the handler, because "nothing was
    /// written" is the observable form of "no column holds a password". The row's creation stamp is
    /// the witness: a reset that had touched the row would have had to change something.
    /// </remarks>
    [Fact]
    public async Task A_reset_changes_nothing_in_the_panels_own_row()
    {
        using var context = Context(CustomerA, Guid.NewGuid().ToString());
        context.Accounts.Add(CustomerA, "acme", maxFtpUsers: 5);
        var id = await SeedAsync(context, CustomerA, "files");
        var before = await context.Database.FtpUsers.AsNoTracking().SingleAsync(CancellationToken.None);

        await context.ResetPasswordHandler.HandleAsync(
            new ResetFtpUserPasswordCommand(id, "203.0.113.9", "agent"), CancellationToken.None);

        var after = await context.Database.FtpUsers.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.FullName, after.FullName);
        Assert.Equal(before.CreatedAt, after.CreatedAt);
    }

    /// <summary>A refused install leaves the old password live and says the reset failed.</summary>
    [Fact]
    public async Task A_refused_install_is_reported_and_leaves_the_old_password_live()
    {
        using var context = Context(CustomerA, Guid.NewGuid().ToString());
        context.Accounts.Add(CustomerA, "acme", maxFtpUsers: 5);
        var id = await SeedAsync(context, CustomerA, "files");
        context.Agent.NextSetPasswordError = Error.Of("AgentNotFound", ErrorType.NotFound);

        var result = await context.ResetPasswordHandler.HandleAsync(
            new ResetFtpUserPasswordCommand(id, "203.0.113.9", "agent"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
        Assert.False(Assert.Single(context.Entries).Succeeded);
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
