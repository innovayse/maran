using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Whether a durable job may keep acting as the administrator who started it (issue #127): the user is read back
/// from the table every time, so a person who stopped being an administrator stops being one for the job too.
/// </summary>
public sealed class AdministratorCallerSourceTests
{
    /// <summary>The moment the users are made.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The inverse control: an active administrator is found and acts as themselves.</summary>
    [Fact]
    public async Task An_active_administrator_is_found()
    {
        await using var database = IdentityTestContext.Create();
        var admin = new User(Guid.NewGuid(), "root", "root@example.com", "hash", UserRole.Admin, Now);
        database.Users.Add(admin);
        await database.SaveChangesAsync();

        var caller = await new AdministratorCallerSource(database).FindActiveAsync(admin.Id, CancellationToken.None);

        Assert.NotNull(caller);
        Assert.Equal(admin.Id, caller!.UserId);
    }

    /// <summary>A customer is not an administrator, whatever id the job carries.</summary>
    [Fact]
    public async Task A_customer_is_not_found_as_an_administrator()
    {
        await using var database = IdentityTestContext.Create();
        var customer = new User(Guid.NewGuid(), "alice", "alice@example.com", "hash", UserRole.Customer, Now);
        database.Users.Add(customer);
        await database.SaveChangesAsync();

        Assert.Null(await new AdministratorCallerSource(database).FindActiveAsync(customer.Id, CancellationToken.None));
    }

    /// <summary>A user who does not exist is not an administrator.</summary>
    [Fact]
    public async Task A_missing_user_is_not_found()
    {
        await using var database = IdentityTestContext.Create();

        Assert.Null(await new AdministratorCallerSource(database).FindActiveAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
