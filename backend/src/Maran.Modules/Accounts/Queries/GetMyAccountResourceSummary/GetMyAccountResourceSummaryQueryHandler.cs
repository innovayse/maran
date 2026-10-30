using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;

namespace Maran.Modules.Accounts.Queries.GetMyAccountResourceSummary;

/// <summary>
/// Handles <see cref="GetMyAccountResourceSummaryQuery"/>: the summary of the account the caller's
/// token names. An administrator owns no account and gets <c>AccountNotFound</c>, as on
/// <c>GET /api/v1/accounts/me</c>.
/// </summary>
public sealed class GetMyAccountResourceSummaryQueryHandler
{
    /// <summary>The authenticated principal of the current request.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Builds the summary.</summary>
    private readonly AccountResourceSummaryReader _reader;

    /// <summary>Creates the handler.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="reader">Builds the summary.</param>
    public GetMyAccountResourceSummaryQueryHandler(ICurrentUser currentUser, AccountResourceSummaryReader reader)
    {
        _currentUser = currentUser;
        _reader = reader;
    }

    /// <summary>Returns the caller's own account's summary.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The summary, or <c>AccountNotFound</c> when the caller owns no account.</returns>
    public async Task<Result<AccountResourceSummaryDto>> HandleAsync(
        GetMyAccountResourceSummaryQuery query,
        CancellationToken cancellationToken)
    {
        if (_currentUser.AccountId is not { } accountId)
        {
            return Result<AccountResourceSummaryDto>.Fail(
                Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        return await _reader.ReadAsync(accountId, cancellationToken);
    }
}
