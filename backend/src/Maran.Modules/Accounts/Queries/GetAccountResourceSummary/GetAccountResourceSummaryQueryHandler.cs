using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Services;

namespace Maran.Modules.Accounts.Queries.GetAccountResourceSummary;

/// <summary>
/// Handles <see cref="GetAccountResourceSummaryQuery"/>: the named account's usage beside its plan's
/// limits, built by <see cref="AccountResourceSummaryReader"/>.
/// </summary>
public sealed class GetAccountResourceSummaryQueryHandler
{
    /// <summary>Builds the summary.</summary>
    private readonly AccountResourceSummaryReader _reader;

    /// <summary>Creates the handler.</summary>
    /// <param name="reader">Builds the summary.</param>
    public GetAccountResourceSummaryQueryHandler(AccountResourceSummaryReader reader)
    {
        _reader = reader;
    }

    /// <summary>Returns the account's summary.</summary>
    /// <param name="query">The account to summarise.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The summary, or <c>AccountNotFound</c>.</returns>
    public async Task<Result<AccountResourceSummaryDto>> HandleAsync(
        GetAccountResourceSummaryQuery query,
        CancellationToken cancellationToken)
    {
        return await _reader.ReadAsync(query.AccountId, cancellationToken);
    }
}
