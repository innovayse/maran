using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Queries.GetViewAs;

/// <summary>
/// Handles <see cref="GetViewAsQuery"/>: reports the live view-as session behind the caller's token,
/// or none (issues #317, #314).
/// </summary>
/// <remarks>
/// This is where the SPA's banner comes from, so the answer is read from the SESSION ROW, not echoed
/// from the token or from anything the SPA sent: a session that has ended answers "not viewing" even
/// to a token that still carries the markers. An own session answers "not viewing" because no view-as
/// row has its id.
/// </remarks>
public sealed class GetViewAsQueryHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Reads the live view-as session.</summary>
    private readonly ViewAsSessionVerifier _verifier;

    /// <summary>The tenant-scoped window onto the accounts module, read for the account's name.</summary>
    private readonly IAccountDirectory _accountDirectory;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="verifier">Reads the live view-as session.</param>
    /// <param name="accountDirectory">The tenant-scoped window onto the accounts module.</param>
    public GetViewAsQueryHandler(
        IdentityDbContext dbContext,
        ViewAsSessionVerifier verifier,
        IAccountDirectory accountDirectory)
    {
        _dbContext = dbContext;
        _verifier = verifier;
        _accountDirectory = accountDirectory;
    }

    /// <summary>Reports the caller's view-as session.</summary>
    /// <param name="query">The caller's session and login, stamped from their token.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The session as the banner shows it, or a state with no session; never a failure.</returns>
    public async Task<Result<ViewAsStateDto>> HandleAsync(GetViewAsQuery query, CancellationToken cancellationToken)
    {
        var session = await _verifier.FindLiveAsync(query.SessionId, cancellationToken);
        if (session is null
            || session.UserId != query.ViewedUserId
            || session.ActiveAccountId is not { } accountId
            || session.ViewAs is not { } origin)
        {
            return Result<ViewAsStateDto>.Ok(new ViewAsStateDto(null));
        }

        var viewedName = await UsernameOfAsync(session.UserId, cancellationToken);
        var operatorName = await UsernameOfAsync(origin.ActorUserId, cancellationToken);
        var account = await _accountDirectory.FindAsync(accountId, cancellationToken);

        var viewing = new ViewingAsDto(
            accountId,
            account?.Username ?? string.Empty,
            viewedName,
            operatorName,
            session.ExpiresAt);

        return Result<ViewAsStateDto>.Ok(new ViewAsStateDto(viewing));
    }

    /// <summary>Reads one login's name.</summary>
    /// <param name="userId">The login.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The name, or empty when the login no longer exists.</returns>
    private async Task<string> UsernameOfAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Username)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;
    }
}
