using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Services;
/// <summary>Behavioural contract of recovery code service.</summary>

public sealed class RecoveryCodeServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly Guid _userId = Guid.NewGuid();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private RecoveryCodeService NewService()
    {
        return new RecoveryCodeService(_context, new Argon2idPasswordHasher(), new FakeClock(Now));
    }

    /// <summary>Ten recovery codes are generated and none repeats.</summary>
    [Fact]
    public async Task Ten_recovery_codes_are_generated_and_none_repeats()
    {
        var codes = await NewService().ReplaceAsync(_userId, CancellationToken.None);

        Assert.Equal(10, codes.Count);
        Assert.Equal(10, codes.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The database never holds a plaintext recovery code.</summary>
    [Fact]
    public async Task The_database_never_holds_a_plaintext_recovery_code()
    {
        var codes = await NewService().ReplaceAsync(_userId, CancellationToken.None);

        var stored = await _context.RecoveryCodes.Select(c => c.CodeHash).ToListAsync();
        Assert.All(codes, code =>
        {
            Assert.DoesNotContain(code, stored);
        });
    }

    /// <summary>A recovery code verifies once and then never again.</summary>
    [Fact]
    public async Task A_recovery_code_verifies_once_and_then_never_again()
    {
        var service = NewService();
        var codes = await service.ReplaceAsync(_userId, CancellationToken.None);

        Assert.True(await service.ConsumeAsync(_userId, codes[0], CancellationToken.None));
        Assert.False(await service.ConsumeAsync(_userId, codes[0], CancellationToken.None));
    }

    /// <summary>Spending one code leaves the others usable.</summary>
    [Fact]
    public async Task Spending_one_code_leaves_the_others_usable()
    {
        var service = NewService();
        var codes = await service.ReplaceAsync(_userId, CancellationToken.None);
        await service.ConsumeAsync(_userId, codes[0], CancellationToken.None);

        Assert.True(await service.ConsumeAsync(_userId, codes[1], CancellationToken.None));
    }

    /// <summary>A code belonging to another user is refused.</summary>
    [Fact]
    public async Task A_code_belonging_to_another_user_is_refused()
    {
        var service = NewService();
        var theirs = await service.ReplaceAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(await service.ConsumeAsync(_userId, theirs[0], CancellationToken.None));
    }

    /// <summary>Replacing the set invalidates every previous code.</summary>
    [Fact]
    public async Task Replacing_the_set_invalidates_every_previous_code()
    {
        var service = NewService();
        var first = await service.ReplaceAsync(_userId, CancellationToken.None);

        await service.ReplaceAsync(_userId, CancellationToken.None);

        Assert.False(await service.ConsumeAsync(_userId, first[0], CancellationToken.None));
    }

    /// <summary>Discarding leaves no code usable.</summary>
    [Fact]
    public async Task Discarding_leaves_no_code_usable()
    {
        var service = NewService();
        var codes = await service.ReplaceAsync(_userId, CancellationToken.None);

        await service.DiscardAsync(_userId, CancellationToken.None);

        Assert.False(await service.ConsumeAsync(_userId, codes[0], CancellationToken.None));
    }
}
