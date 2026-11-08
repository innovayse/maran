using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Queries.GetPhpSettings;

/// <summary>Handles <see cref="GetPhpSettingsQuery"/>.</summary>
/// <remarks>
/// The account is looked up through the tenant-scoped directory, so an account the caller does not
/// own is simply not found: 404 and never 403, which would confirm the account exists.
/// </remarks>
public sealed class GetPhpSettingsQueryHandler
{
    /// <summary>The account's plan bounds.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Builds the screen's view of the settings.</summary>
    private readonly PhpSettingsReader _reader;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">The account's plan bounds.</param>
    /// <param name="reader">Builds the screen's view of the settings.</param>
    public GetPhpSettingsQueryHandler(IAccountDirectory accounts, PhpSettingsReader reader)
    {
        _accounts = accounts;
        _reader = reader;
    }

    /// <summary>Reads the settings with the plan's bounds beside them.</summary>
    /// <param name="query">The account to read, stamped by the action.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The settings, or <c>AccountNotFound</c>.</returns>
    public async Task<Result<PhpSettingsDto>> HandleAsync(GetPhpSettingsQuery query, CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(query.AccountId, cancellationToken);
        if (account is null)
        {
            return Result<PhpSettingsDto>.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        return Result<PhpSettingsDto>.Ok(await _reader.ReadAsync(account, cancellationToken));
    }
}
