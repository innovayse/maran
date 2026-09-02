using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sftp.Queries.ListSftpUsers;
using Maran.Modules.Sftp.Tests.TestSupport;

namespace Maran.Modules.Sftp.Tests.Queries.ListSftpUsers;

/// <summary>Where a listing's rows come from, and — just as importantly — where they do not.</summary>
public sealed class ListSftpUsersQueryHandlerTests
{
    private static readonly Guid OwnerAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>Listing sftp users returns the panels own tenant rows and never asks the agent to enumerate.</summary>
    [Fact]
    public async Task Listing_sftp_users_returns_the_panels_own_tenant_rows_and_never_asks_the_agent_to_enumerate()
    {
        // The host has no notion of a tenant, so an answer derived from /etc/passwd is derived from
        // a prefix scan — and `alice_` is a prefix of account `alice_bob`'s logins too, because
        // account names may contain the separator. Authorisation is the panel's own tenant-filtered
        // rows.
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = SftpTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), shared);
        var result = await new ListSftpUsersQueryHandler(context)
            .HandleAsync(new ListSftpUsersQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["deploy"], result.Value.Select(row =>
        {
            return row.Name;
        }));

        // Asserted STRUCTURALLY, over the type rather than over a call counter on a double the
        // handler was never given. A counter on an unwired double reads as proof and is not: it
        // stays at zero however the handler is written, so it could never have gone red. What can go
        // red is this — the handler cannot ask the agent anything, because it cannot be handed one.
        Assert.DoesNotContain(
            typeof(ListSftpUsersQueryHandler).GetConstructors()
                .SelectMany(constructor =>
                {
                    return constructor.GetParameters();
                }),
            parameter =>
            {
                return parameter.ParameterType == typeof(IAgentSftpClient);
            });
    }

    /// <summary>No query handler in this module can be handed the agent at all.</summary>
    [Fact]
    public void No_query_handler_in_this_module_can_be_handed_the_agent_at_all()
    {
        // The same guarantee widened past the one handler the test above names, so a NEW read that
        // reached for the host's own user database would fail here rather than ship. Reads in this
        // module are answered from the panel's tenant-filtered rows and from nothing else.
        var readers = typeof(ListSftpUsersQueryHandler).Assembly.GetTypes()
            .Where(type =>
            {
                return type.Namespace?.StartsWith(
                    "Maran.Modules.Sftp.Queries", StringComparison.Ordinal) == true;
            })
            .Where(type =>
            {
                return type.GetConstructors().SelectMany(constructor =>
                {
                    return constructor.GetParameters();
                }).Any(parameter =>
                {
                    return parameter.ParameterType == typeof(IAgentSftpClient);
                });
            })
            .Select(type =>
            {
                return type.Name;
            })
            .ToList();

        Assert.True(
            readers.Count == 0,
            "A read in this module must be answered from the panel's own rows, never from the host's "
            + "own user database: " + string.Join(", ", readers));
    }

    /// <summary>A listing shows a customer only their own rows.</summary>
    [Fact]
    public async Task A_listing_shows_a_customer_only_their_own_rows()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = SftpTestContext.Create(FakeCurrentUser.Customer(StrangerAccountId), shared);
        var result = await new ListSftpUsersQueryHandler(context)
            .HandleAsync(new ListSftpUsersQuery(), CancellationToken.None);

        Assert.Equal(["backup"], result.Value.Select(row =>
        {
            return row.Name;
        }));
    }

    /// <summary>An administrator sees every accounts rows.</summary>
    [Fact]
    public async Task An_administrator_sees_every_accounts_rows()
    {
        // Guards the two tests above from passing for the wrong reason: if the seed never ran, or the
        // handler simply returned nothing, "only their own" would be true of an empty result.
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = SftpTestContext.Create(FakeCurrentUser.Admin(), shared);
        var result = await new ListSftpUsersQueryHandler(context)
            .HandleAsync(new ListSftpUsersQuery(), CancellationToken.None);

        Assert.Equal(2, result.Value.Count);
    }

    /// <summary>A listing carries the login a customer signs in with and no password.</summary>
    [Fact]
    public async Task A_listing_carries_the_login_a_customer_signs_in_with_and_no_password()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = SftpTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), shared);
        var result = await new ListSftpUsersQueryHandler(context)
            .HandleAsync(new ListSftpUsersQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value);
        Assert.Equal("alice_deploy", row.FullName);

        // Structural, not a spot check: the DTO must have no password-shaped member at all.
        Assert.DoesNotContain(
            typeof(Modules.Sftp.Common.SftpUserDto).GetProperties(),
            property =>
            {
                return property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase);
            });
    }

    /// <summary>Seeds one login for each of the two accounts into a shared store.</summary>
    /// <param name="databaseName">The shared in-memory database.</param>
    private static async Task SeedAsync(string databaseName)
    {
        // Written through a context whose principal is an administrator, so the seed itself does no
        // tenant separating — the filter under test is the only thing that can.
        using var seed = SftpTestContext.Create(FakeCurrentUser.Admin(), databaseName);
        seed.SftpUsers.Add(SftpTestContext.Row(OwnerAccountId, "alice", "deploy"));
        seed.SftpUsers.Add(SftpTestContext.Row(StrangerAccountId, "bob", "backup"));
        await seed.SaveChangesAsync();
    }
}
