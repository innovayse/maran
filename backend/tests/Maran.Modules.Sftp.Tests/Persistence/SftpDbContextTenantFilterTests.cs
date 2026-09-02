using Maran.Modules.Sftp.Domain;
using Maran.Modules.Sftp.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sftp.Tests.Persistence;

/// <summary>
/// The module's authorisation mechanism, tested on its own rather than only through the handlers
/// that lean on it — plus the two columns this table must never grow.
/// </summary>
public sealed class SftpDbContextTenantFilterTests
{
    private static readonly Guid OwnerAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>
    /// The words a column that holds a credential would plausibly be named with. Deliberately wider
    /// than "password": the failure to catch is somebody storing the value under a name that sounds
    /// safe, and "not plaintext, it is a hash" is exactly the reasoning that would produce one.
    /// </summary>
    private static readonly string[] SecretShapedWords = ["password", "secret", "credential", "hash"];

    /// <summary>
    /// The words a caller-supplied jail directory would be named with. Its absence is a security
    /// property, not an omission: OpenSSH chroots every login here with a fixed
    /// <c>ChrootDirectory %h</c> that the agent derives from the account, so a column here would
    /// mean the panel had started letting a customer name the directory they are confined to.
    /// </summary>
    private static readonly string[] JailShapedWords = ["chroot", "jail", "homedirectory", "path"];

    /// <summary>A customers context returns only their own rows.</summary>
    [Fact]
    public async Task A_customers_context_returns_only_their_own_rows()
    {
        var store = await SeedAsync();

        using var context = SftpTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), store);

        Assert.Equal(["deploy"], await context.SftpUsers.Select(row => row.Name).ToListAsync());
    }

    /// <summary>An administrators context is narrowed by nothing.</summary>
    [Fact]
    public async Task An_administrators_context_is_narrowed_by_nothing()
    {
        var store = await SeedAsync();

        using var context = SftpTestContext.Create(FakeCurrentUser.Admin(), store);

        Assert.Equal(2, await context.SftpUsers.CountAsync());
    }

    /// <summary>A customer cannot reach another tenants row by its identifier.</summary>
    [Fact]
    public async Task A_customer_cannot_reach_another_tenants_row_by_its_identifier()
    {
        // This is why the module answers 404 and not 403: the row is not in the result set at all,
        // so nothing in a handler has to remember to make the distinction.
        var store = await SeedAsync();

        using var admin = SftpTestContext.Create(FakeCurrentUser.Admin(), store);
        var stranger = await admin.SftpUsers.SingleAsync(row => row.AccountId == StrangerAccountId);

        using var context = SftpTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), store);

        Assert.Null(await context.SftpUsers.SingleOrDefaultAsync(row => row.Id == stranger.Id));
    }

    /// <summary>The sftp user entity carries no password shaped property of any kind.</summary>
    [Fact]
    public void The_sftp_user_entity_carries_no_password_shaped_property_of_any_kind()
    {
        // Structural rather than a spot check, and asserted at the ENTITY so that a column added by
        // any route — plaintext, encrypted, hashed — fails here. The panel mints a password, shows
        // it once and forgets it; a stored copy is a copy that can be read out of the panel's
        // database, and a panel that can read back every customer's SFTP password is a single theft
        // away from every customer's files.
        var suspicious = typeof(SftpUser).GetProperties()
            .Select(property =>
            {
                return property.Name;
            })
            .Where(name =>
            {
                return SecretShapedWords.Any(word =>
                {
                    return name.Contains(word, StringComparison.OrdinalIgnoreCase);
                });
            })
            .ToList();

        Assert.True(
            suspicious.Count == 0,
            "SftpUser must hold no password of any kind — not plaintext, not encrypted, not a hash: "
            + string.Join(", ", suspicious));
    }

    /// <summary>The mapped columns carry no password shaped name either.</summary>
    [Fact]
    public void The_mapped_columns_carry_no_password_shaped_name_either()
    {
        // The entity check above would miss a shadow property, which EF can map without any C#
        // property to name it.
        using var context = SftpTestContext.Create(FakeCurrentUser.Admin());
        var columns = MappedColumns(context);

        Assert.NotEmpty(columns);
        Assert.DoesNotContain(
            columns,
            name =>
            {
                return SecretShapedWords.Any(word =>
                {
                    return name.Contains(word, StringComparison.OrdinalIgnoreCase);
                });
            });
    }

    /// <summary>The mapped columns hold no caller supplied jail directory.</summary>
    [Fact]
    public void The_mapped_columns_hold_no_caller_supplied_jail_directory()
    {
        // The plan's F5, pinned rather than described. An earlier draft of this module carried a
        // chroot path the customer chose and the panel validated against a site's document root —
        // more restrictive than the agent, coupled to another module, and a path the customer names
        // is a path an attacker names. The jail is `%h`, derived by the agent from the account, so
        // there is nothing here to store and re-adding one must fail HERE rather than in review.
        using var context = SftpTestContext.Create(FakeCurrentUser.Admin());
        var columns = MappedColumns(context);

        Assert.NotEmpty(columns);
        Assert.DoesNotContain(
            columns,
            name =>
            {
                return JailShapedWords.Any(word =>
                {
                    return name.Contains(word, StringComparison.OrdinalIgnoreCase);
                });
            });
    }

    /// <summary>Reads the names EF actually maps, shadow properties included.</summary>
    /// <param name="context">A context whose model has been built.</param>
    private static List<string> MappedColumns(Modules.Sftp.Persistence.SftpDbContext context)
    {
        return context.Model.FindEntityType(typeof(SftpUser))!
            .GetProperties()
            .Select(property =>
            {
                return property.Name;
            })
            .ToList();
    }

    /// <summary>Seeds one row for each account into a fresh shared store.</summary>
    /// <returns>The name of the store the tests open.</returns>
    private static async Task<string> SeedAsync()
    {
        var store = Guid.NewGuid().ToString();

        using var seed = SftpTestContext.Create(FakeCurrentUser.Admin(), store);
        seed.SftpUsers.Add(SftpTestContext.Row(OwnerAccountId, "alice", "deploy"));
        seed.SftpUsers.Add(SftpTestContext.Row(StrangerAccountId, "bob", "backup"));
        await seed.SaveChangesAsync();

        return store;
    }
}
