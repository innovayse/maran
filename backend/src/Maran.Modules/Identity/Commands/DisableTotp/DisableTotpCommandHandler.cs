using Maran.Modules.Identity.Common.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Commands.DisableTotp;

/// <summary>Handles <see cref="DisableTotpCommand"/> by removing the second factor.</summary>
public sealed class DisableTotpCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Verifies the proving code.</summary>
    private readonly ITotpService _totpService;

    /// <summary>Verifies a recovery code, and discards the rest afterwards.</summary>
    private readonly IRecoveryCodeService _recoveryCodeService;

    /// <summary>Records the change.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="totpService">Verifies the proving code.</param>
    /// <param name="recoveryCodeService">Verifies a recovery code and discards the set.</param>
    /// <param name="auditWriter">Records the change.</param>
    public DisableTotpCommandHandler(
        IdentityDbContext dbContext,
        ITotpService totpService,
        IRecoveryCodeService recoveryCodeService,
        IAuditWriter auditWriter)
    {
        _dbContext = dbContext;
        _totpService = totpService;
        _recoveryCodeService = recoveryCodeService;
        _auditWriter = auditWriter;
    }

    /// <summary>Removes the factor, but only for someone who can still use it.</summary>
    /// <param name="command">Who, and the code proving it.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// Success, or a typed failure. A code is required to turn the factor OFF as well as on: a
    /// stolen session with only the password behind it must not be able to strip the protection
    /// that would have stopped it.
    /// </returns>
    public async Task<Result<bool>> HandleAsync(DisableTotpCommand command, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.UserNotFound)));
        }

        if (!user.IsTotpEnabled || user.TotpSecret is null)
        {
            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.TwoFactorNotEnabledForbidden)));
        }

        var accepted = _totpService.Verify(user.TotpSecret, command.Code, user.LastTotpWindow, out var window);
        if (accepted)
        {
            user.RecordTotpWindow(window);
        }
        else if (!await _recoveryCodeService.ConsumeAsync(user.Id, command.Code, cancellationToken))
        {
            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.InvalidTwoFactorCodeUnauthorized)));
        }

        user.DisableTotp();
        await _dbContext.SaveChangesAsync(cancellationToken);

        // The codes only exist to rescue this factor; leaving them behind would let a stale sheet
        // of paper satisfy a factor enrolled later with a different secret.
        await _recoveryCodeService.DiscardAsync(user.Id, cancellationToken);

        await _auditWriter.WriteAsync(
            new AuditEntry(
                user.Id,
                user.Username,
                AuditActions.TwoFactorDisabled,
                user.Username,
                command.IpAddress,
                command.UserAgent,
                Succeeded: true),
            cancellationToken);

        return Result<bool>.Ok(true);
    }
}
