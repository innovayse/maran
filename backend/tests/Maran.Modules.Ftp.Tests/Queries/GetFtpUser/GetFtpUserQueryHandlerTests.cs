using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Queries.GetFtpUser;
using Maran.Modules.Ftp.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Ftp.Tests.Queries.GetFtpUser;

/// <summary>Reading one FTPS login, and what a neighbour's identifier is answered with.</summary>
public sealed class GetFtpUserQueryHandlerTests
{
    /// <summary>The account whose logins the caller in these tests owns.</summary>
    private static readonly Guid CustomerA = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A different account, whose logins the caller must not be able to read.</summary>
    private static readonly Guid CustomerB = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>The caller's own login reads back with everything a screen shows.</summary>
    [Fact]
    public async Task The_callers_own_login_reads_back_with_the_facts_a_screen_shows()
    {
        using var context = Context(CustomerA, Guid.NewGuid().ToString());
        var id = await SeedAsync(context, CustomerA, "acme", "files");

        var result = await context.GetUserHandler.HandleAsync(
            new GetFtpUserQuery(id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(id, result.Value.Id);
        Assert.Equal("files", result.Value.Name);
        Assert.Equal("acme_files", result.Value.FullName);
        Assert.Equal("Ftps", result.Value.Protocol);
    }

    /// <summary>Another account's login is not found, exactly as an identifier naming nothing is.</summary>
    /// <remarks>
    /// A 403 here would confirm the identifier names a real login, which turns the endpoint into an
    /// oracle for enumerating other customers' access. The two refusals are asserted to be the same
    /// code and kind, so the indistinguishability is a property rather than a coincidence.
    /// </remarks>
    [Fact]
    public async Task Another_accounts_login_is_not_found_exactly_as_a_missing_one_is()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var owner = Context(CustomerB, databaseName);
        var victim = await SeedAsync(owner, CustomerB, "beta", "secrets");

        using var context = Context(CustomerA, databaseName);

        var neighbour = await context.GetUserHandler.HandleAsync(
            new GetFtpUserQuery(victim), CancellationToken.None);
        var missing = await context.GetUserHandler.HandleAsync(
            new GetFtpUserQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.False(neighbour.IsSuccess);
        Assert.False(missing.IsSuccess);
        Assert.Equal(ErrorCodes.FtpUserNotFound, neighbour.Error!.Code);
        Assert.Equal(neighbour.Error.Code, missing.Error!.Code);
        Assert.Equal(ErrorType.NotFound, neighbour.Error.Type);
        Assert.Equal(neighbour.Error.Type, missing.Error.Type);
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
    /// <param name="username">The account's system user name, which prefixes the login.</param>
    /// <param name="name">The login's suffix.</param>
    /// <returns>The new row's identity.</returns>
    private static async Task<Guid> SeedAsync(
        FtpTestContext context,
        Guid accountId,
        string username,
        string name)
    {
        var id = Guid.NewGuid();
        context.Database.FtpUsers.Add(
            new FtpUser(id, accountId, name, $"{username}_{name}", context.Clock.UtcNow));
        await context.Database.SaveChangesAsync(CancellationToken.None);

        return id;
    }
}
