using Maran.Modules.Identity.Commands.ConfirmTotpEnrolment;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using OtpNet;

namespace Maran.Modules.Identity.Tests.Commands.ConfirmTotpEnrolment;

/// <summary>Behavioural contract of confirming a two-factor enrolment.</summary>
public sealed class ConfirmTotpEnrolmentCommandHandlerTests : IDisposable
{
    /// <summary>The secret both the stored factor and the confirmation use.</summary>
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

    /// <summary>Confirming while two-factor is already on is a state conflict, and nothing is reissued.</summary>
    [Fact]
    public async Task Confirming_with_two_factor_already_on_is_a_conflict()
    {
        var user = await SeedUserAsync(withTotp: true);

        var result = await NewHandler().HandleAsync(Confirm(user.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(new Error("TwoFactorAlreadyEnabled", ErrorType.Conflict), result.Error);
    }

    /// <summary>The inverse control: a user without two-factor confirms and is handed recovery codes.</summary>
    [Fact]
    public async Task Confirming_with_two_factor_off_enables_it()
    {
        var user = await SeedUserAsync(withTotp: false);

        var result = await NewHandler().HandleAsync(Confirm(user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.Codes);
        Assert.True(user.IsTotpEnabled);
    }

    /// <summary>Builds a confirmation carrying a code valid at <see cref="Now"/>.</summary>
    /// <param name="userId">The caller.</param>
    /// <returns>The command.</returns>
    private static ConfirmTotpEnrolmentCommand Confirm(Guid userId)
    {
        var code = new Totp(Base32Encoding.ToBytes(Secret)).ComputeTotp(Now.UtcDateTime);
        return new ConfirmTotpEnrolmentCommand(userId, Secret, code, "203.0.113.7", "agent");
    }

    /// <summary>Builds the handler over this fixture's context.</summary>
    /// <returns>The handler under test.</returns>
    private ConfirmTotpEnrolmentCommandHandler NewHandler()
    {
        return new ConfirmTotpEnrolmentCommandHandler(
            _context,
            new TotpService(_clock),
            new RecoveryCodeService(_context, new Argon2idPasswordHasher(), _clock),
            new IdentityAuditJournal(new RecordingAuditWriter(), new StubCurrentUser()));
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
