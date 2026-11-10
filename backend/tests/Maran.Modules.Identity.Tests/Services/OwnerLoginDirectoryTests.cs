using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Behavioral contract of <see cref="OwnerLoginDirectory"/>: it names the value another login already
/// holds, and reports nothing when both are free (issue #485).
/// </summary>
public sealed class OwnerLoginDirectoryTests : IDisposable
{
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Seeds one existing login.</summary>
    /// <returns>Resolves when saved.</returns>
    private async Task SeedAsync()
    {
        _context.Users.Add(User.Invite(Guid.NewGuid(), "existing", "taken@example.com", DateTimeOffset.UnixEpoch));
        await _context.SaveChangesAsync();
    }

    /// <summary>An e-mail address another login holds is reported as taken.</summary>
    [Fact]
    public async Task An_email_address_another_login_holds_is_reported_taken()
    {
        await SeedAsync();

        var conflict = await new OwnerLoginDirectory(_context)
            .FindConflictAsync("fresh", "taken@example.com", CancellationToken.None);

        Assert.Equal(OwnerLoginConflict.EmailTaken, conflict);
    }

    /// <summary>A name another login holds is reported as taken, before the address is looked at.</summary>
    [Fact]
    public async Task A_name_another_login_holds_is_reported_taken()
    {
        await SeedAsync();

        var conflict = await new OwnerLoginDirectory(_context)
            .FindConflictAsync("existing", "fresh@example.com", CancellationToken.None);

        Assert.Equal(OwnerLoginConflict.UsernameTaken, conflict);
    }

    /// <summary>The inverse control: a free name and a free address report no clash.</summary>
    [Fact]
    public async Task A_free_name_and_a_free_address_report_no_conflict()
    {
        await SeedAsync();

        var conflict = await new OwnerLoginDirectory(_context)
            .FindConflictAsync("fresh", "fresh@example.com", CancellationToken.None);

        Assert.Equal(OwnerLoginConflict.None, conflict);
    }
}
