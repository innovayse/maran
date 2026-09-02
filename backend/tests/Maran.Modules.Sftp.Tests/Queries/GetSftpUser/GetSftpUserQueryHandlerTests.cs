using Maran.Modules.Sftp.Queries.GetSftpUser;
using Maran.Modules.Sftp.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sftp.Tests.Queries.GetSftpUser;

/// <summary>What one read answers for a row of one's own, and for a row of somebody else's.</summary>
public sealed class GetSftpUserQueryHandlerTests
{
    private static readonly Guid OwnerAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>Reading another tenants sftp user answers not found rather than forbidden.</summary>
    [Fact]
    public async Task Reading_another_tenants_sftp_user_answers_not_found_rather_than_forbidden()
    {
        // 403 would confirm the identifier names a real login, which turns this endpoint into an
        // oracle for enumerating other customers' access. The handler makes no such distinction: the
        // query filter means the row genuinely is not in the result set.
        var shared = Guid.NewGuid().ToString();
        var strangerId = await SeedAsync(shared);

        using var context = SftpTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), shared);
        var result = await new GetSftpUserQueryHandler(context)
            .HandleAsync(new GetSftpUserQuery(strangerId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SftpUserNotFound", result.Error!.Code);
    }

    /// <summary>An identifier that names nothing answers exactly as another tenants does.</summary>
    [Fact]
    public async Task An_identifier_that_names_nothing_answers_exactly_as_another_tenants_does()
    {
        // The two must be indistinguishable, or the difference between them IS the oracle.
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = SftpTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), shared);
        var result = await new GetSftpUserQueryHandler(context)
            .HandleAsync(new GetSftpUserQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SftpUserNotFound", result.Error!.Code);
    }

    /// <summary>A customer reading their own sftp user is answered with it.</summary>
    [Fact]
    public async Task A_customer_reading_their_own_sftp_user_is_answered_with_it()
    {
        // Guards the two above from passing for the wrong reason: if the read were simply broken,
        // "not found" would be true of every request.
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var read = SftpTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), shared);
        var own = await read.SftpUsers.SingleAsync();

        var result = await new GetSftpUserQueryHandler(read)
            .HandleAsync(new GetSftpUserQuery(own.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("deploy", result.Value.Name);
        Assert.Equal("alice_deploy", result.Value.FullName);
    }

    /// <summary>Seeds one login for each account and returns the stranger's identifier.</summary>
    /// <param name="databaseName">The shared in-memory database.</param>
    private static async Task<Guid> SeedAsync(string databaseName)
    {
        using var seed = SftpTestContext.Create(FakeCurrentUser.Admin(), databaseName);
        seed.SftpUsers.Add(SftpTestContext.Row(OwnerAccountId, "alice", "deploy"));
        var stranger = SftpTestContext.Row(StrangerAccountId, "bob", "backup");
        seed.SftpUsers.Add(stranger);
        await seed.SaveChangesAsync();

        return stranger.Id;
    }
}
