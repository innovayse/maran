using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Common.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;

namespace Maran.Modules.Identity.Commands.BeginTotpEnrolment;

/// <summary>Handles <see cref="BeginTotpEnrolmentCommand"/> by minting a secret to be confirmed.</summary>
public sealed class BeginTotpEnrolmentCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Generates the secret and its provisioning URI.</summary>
    private readonly ITotpService _totpService;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="totpService">Generates the secret.</param>
    public BeginTotpEnrolmentCommandHandler(IdentityDbContext dbContext, ITotpService totpService)
    {
        _dbContext = dbContext;
        _totpService = totpService;
    }

    /// <summary>Generates a secret for the user to scan. Nothing is enabled and nothing is stored.</summary>
    /// <param name="command">Who is enrolling.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The secret and its URI, or a typed failure. The secret is NOT written to the user row here:
    /// storing it before it is confirmed would leave a half-enrolled account that neither has a
    /// working factor nor can be told apart from one that does.
    /// </returns>
    public async Task<Result<TotpEnrolmentDto>> HandleAsync(
        BeginTotpEnrolmentCommand command,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return Result<TotpEnrolmentDto>.Fail(Error.Of(nameof(ErrorMessages.UserNotFound)));
        }

        if (user.IsTotpEnabled)
        {
            return Result<TotpEnrolmentDto>.Fail(Error.Of(nameof(ErrorMessages.TwoFactorAlreadyEnabledForbidden)));
        }

        var secret = _totpService.GenerateSecret();
        return Result<TotpEnrolmentDto>.Ok(new TotpEnrolmentDto(
            secret,
            _totpService.BuildProvisioningUri(secret, user.Username)));
    }
}
