using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Commands.ConfirmTotpEnrolment;

/// <summary>Handles <see cref="ConfirmTotpEnrolmentCommand"/> by enabling the second factor.</summary>
public sealed class ConfirmTotpEnrolmentCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Verifies the proving code.</summary>
    private readonly ITotpService _totpService;

    /// <summary>Issues the recovery codes that come with a working second factor.</summary>
    private readonly IRecoveryCodeService _recoveryCodeService;

    /// <summary>Records the change.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="totpService">Verifies the proving code.</param>
    /// <param name="recoveryCodeService">Issues the recovery codes.</param>
    /// <param name="journal">Records the change.</param>
    public ConfirmTotpEnrolmentCommandHandler(
        IdentityDbContext dbContext,
        ITotpService totpService,
        IRecoveryCodeService recoveryCodeService,
        IdentityAuditJournal journal)
    {
        _dbContext = dbContext;
        _totpService = totpService;
        _recoveryCodeService = recoveryCodeService;
        _journal = journal;
    }

    /// <summary>Enables the factor and hands over the recovery codes.</summary>
    /// <param name="command">The secret and a code proving it works.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The recovery codes, or a typed failure.</returns>
    public async Task<Result<RecoveryCodesDto>> HandleAsync(
        ConfirmTotpEnrolmentCommand command,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return Result<RecoveryCodesDto>.Fail(Error.Of(nameof(ErrorMessages.UserNotFound), ErrorType.NotFound));
        }

        if (user.IsTotpEnabled)
        {
            return Result<RecoveryCodesDto>.Fail(Error.Of(nameof(ErrorMessages.TwoFactorAlreadyEnabledForbidden), ErrorType.Forbidden));
        }

        if (!_totpService.Verify(command.Secret, command.Code, user.LastTotpWindow, out var window))
        {
            return Result<RecoveryCodesDto>.Fail(Error.Of(nameof(ErrorMessages.InvalidTwoFactorCodeUnauthorized), ErrorType.Unauthorized));
        }

        user.EnableTotp(command.Secret);
        user.RecordTotpWindow(window);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var codes = await _recoveryCodeService.ReplaceAsync(user.Id, cancellationToken);

        await _journal.RecordClaimAsync(
            user.Id,
            user.Username,
            AuditActions.TwoFactorEnabled,
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result<RecoveryCodesDto>.Ok(new RecoveryCodesDto(codes));
    }
}
