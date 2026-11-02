using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Mappers;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;

namespace Maran.Modules.Identity.Commands.ChangeSubUserGrants;

/// <summary>
/// Handles <see cref="ChangeSubUserGrantsCommand"/>: replaces the features one of the caller's
/// sub-users may use.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only the caller's own team is reachable.</b> The membership is looked up by the sub-user's id AND
/// the caller's active account, and only a <see cref="MembershipRole.Member"/> row matches. A sub-user
/// of another account, the caller themselves, and an id that is no login at all are therefore one
/// answer — <c>SubUserNotFound</c> (404) — so the endpoint cannot be used to find out who belongs to
/// whom.
/// </para>
/// <para>
/// <b>The change takes effect on the sub-user's next request.</b> Grants are read from the row on every
/// call to a gated endpoint (<c>IAccountMemberGrants</c>), not copied into the access token, so there
/// is no token to wait out and no session to revoke when a feature is withdrawn.
/// </para>
/// </remarks>
public sealed class ChangeSubUserGrantsCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Establishes that the caller owns the account they are acting for.</summary>
    private readonly SubUserOwnerGuard _guard;

    /// <summary>Records the change.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The authenticated owner — the actor the journal entry names.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="guard">Establishes that the caller owns the account they are acting for.</param>
    /// <param name="journal">Records the change.</param>
    /// <param name="currentUser">The authenticated owner.</param>
    public ChangeSubUserGrantsCommandHandler(
        IdentityDbContext dbContext,
        SubUserOwnerGuard guard,
        IdentityAuditJournal journal,
        ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _guard = guard;
        _journal = journal;
        _currentUser = currentUser;
    }

    /// <summary>Replaces the sub-user's features.</summary>
    /// <param name="command">Whose features, and the new set.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The sub-user as they now stand, or a typed refusal.</returns>
    public async Task<Result<SubUserDto>> HandleAsync(
        ChangeSubUserGrantsCommand command,
        CancellationToken cancellationToken)
    {
        var owned = await _guard.RequireOwnedAccountAsync(cancellationToken);
        if (!owned.IsSuccess)
        {
            return await RefuseAsync(command, owned.Error!, cancellationToken);
        }

        var membership = await _dbContext.AccountMemberships.SingleOrDefaultAsync(
            candidate => candidate.UserId == command.UserId
                && candidate.AccountId == owned.Value
                && candidate.Role == MembershipRole.Member,
            cancellationToken);
        var user = membership is null
            ? null
            : await _dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);

        if (membership is null || user is null)
        {
            return await RefuseAsync(
                command, Error.Of(nameof(ErrorMessages.SubUserNotFound), ErrorType.NotFound), cancellationToken);
        }

        membership.ChangeGrants(command.Features.ToSet());
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordIdentifiedAsync(
            SignedInCallerPolicy.Require(_currentUser).UserId,
            AuditActions.SubUserGrantsChanged,
            user.Username,
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result<SubUserDto>.Ok(SubUserMapper.From(user, membership));
    }

    /// <summary>Journals a refused attempt and answers with the refusal.</summary>
    /// <param name="command">The refused request.</param>
    /// <param name="error">Why it was refused.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<SubUserDto>> RefuseAsync(
        ChangeSubUserGrantsCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        // The subject is the id the caller named: a refused attempt on somebody else's sub-user still
        // leaves a trace of what was probed.
        await _journal.RecordIdentifiedAsync(
            SignedInCallerPolicy.Require(_currentUser).UserId,
            AuditActions.SubUserGrantsChanged,
            command.UserId.ToString("D"),
            command.IpAddress,
            command.UserAgent,
            succeeded: false,
            cancellationToken);

        return Result<SubUserDto>.Fail(error);
    }
}
