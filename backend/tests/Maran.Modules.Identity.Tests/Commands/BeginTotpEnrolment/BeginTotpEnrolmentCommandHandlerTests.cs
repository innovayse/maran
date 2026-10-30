using Maran.Modules.Identity.Commands.BeginTotpEnrolment;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Identity.Tests.Commands.BeginTotpEnrolment;

/// <summary>Behavioural contract of starting a two-factor enrolment.</summary>
public sealed class BeginTotpEnrolmentCommandHandlerTests : IDisposable
{
    /// <summary>The instant every clock in this file reads.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The in-memory Identity context the handler reads.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>A user who already has two-factor on is refused with a state conflict, not a 403.</summary>
    [Fact]
    public async Task Starting_an_enrolment_with_two_factor_already_on_is_a_conflict()
    {
        var user = await SeedUserAsync(withTotp: true);

        var result = await NewHandler().HandleAsync(new BeginTotpEnrolmentCommand(user.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(new Error("TwoFactorAlreadyEnabled", ErrorType.Conflict), result.Error);
    }

    /// <summary>The inverse control: a user without two-factor is handed a secret and its URI.</summary>
    [Fact]
    public async Task Starting_an_enrolment_with_two_factor_off_returns_a_secret()
    {
        var user = await SeedUserAsync(withTotp: false);

        var result = await NewHandler().HandleAsync(new BeginTotpEnrolmentCommand(user.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.StartsWith("otpauth://totp/", result.Value.ProvisioningUri, StringComparison.Ordinal);
    }

    /// <summary>Builds the handler over this fixture's context.</summary>
    /// <returns>The handler under test.</returns>
    private BeginTotpEnrolmentCommandHandler NewHandler()
    {
        return new BeginTotpEnrolmentCommandHandler(_context, new TotpService(new FakeClock(Now)));
    }

    /// <summary>Stores a customer, with or without a second factor.</summary>
    /// <param name="withTotp">Whether the user already has two-factor on.</param>
    /// <returns>The stored user.</returns>
    private async Task<User> SeedUserAsync(bool withTotp)
    {
        var user = new User(Guid.NewGuid(), "owner-one", "owner@example.com", "hash", UserRole.Customer, Now);
        if (withTotp)
        {
            user.EnableTotp("JBSWY3DPEHPK3PXP");
        }

        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }
}
