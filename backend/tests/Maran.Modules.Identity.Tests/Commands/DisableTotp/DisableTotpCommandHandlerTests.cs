using Maran.Modules.Identity.Commands.DisableTotp;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using OtpNet;

namespace Maran.Modules.Identity.Tests.Commands.DisableTotp;

/// <summary>Behavioural contract of turning two-factor off.</summary>
public sealed class DisableTotpCommandHandlerTests : IDisposable
{
    /// <summary>The secret of the stored factor.</summary>
    private const string Secret = "JBSWY3DPEHPK3PXP";

    /// <summary>The instant every clock in this file reads.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The in-memory Identity context the handler reads.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>The clock shared by the handler's services.</summary>
    private readonly FakeClock _clock = new(Now);

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Turning off a factor that is not on is a state conflict, not a 403.</summary>
    [Fact]
    public async Task Disabling_with_two_factor_off_is_a_conflict()
    {
        var user = await SeedUserAsync(withTotp: false);

        var result = await NewHandler().HandleAsync(Disable(user.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(new Error("TwoFactorNotEnabled", ErrorType.Conflict), result.Error);
    }

    /// <summary>The inverse control: a user with two-factor on and a valid code turns it off.</summary>
    [Fact]
    public async Task Disabling_with_two_factor_on_and_a_valid_code_turns_it_off()
    {
        var user = await SeedUserAsync(withTotp: true);

        var result = await NewHandler().HandleAsync(Disable(user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(user.IsTotpEnabled);
    }

    /// <summary>Builds a disable request carrying a code valid at <see cref="Now"/>.</summary>
    /// <param name="userId">The caller.</param>
    /// <returns>The command.</returns>
    private static DisableTotpCommand Disable(Guid userId)
    {
        var code = new Totp(Base32Encoding.ToBytes(Secret)).ComputeTotp(Now.UtcDateTime);
        return new DisableTotpCommand(userId, code, "203.0.113.7", "agent");
    }

    /// <summary>Builds the handler over this fixture's context.</summary>
    /// <returns>The handler under test.</returns>
    private DisableTotpCommandHandler NewHandler()
    {
        return new DisableTotpCommandHandler(
            _context,
            new TotpService(_clock),
            new RecoveryCodeService(_context, new Argon2idPasswordHasher(), _clock),
            new IdentityAuditJournal(new RecordingAuditWriter(), new StubCurrentUser()),
            new TwoFactorCodeRejection(new StubClockSyncStatus()));
    }

    /// <summary>Stores a customer, with or without a second factor.</summary>
    /// <param name="withTotp">Whether the user already has two-factor on.</param>
    /// <returns>The stored user.</returns>
    private async Task<User> SeedUserAsync(bool withTotp)
    {
        var user = new User(Guid.NewGuid(), "owner-one", "owner@example.com", "hash", UserRole.Customer, Now);
        if (withTotp)
        {
            user.EnableTotp(Secret);
        }

        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }
}
