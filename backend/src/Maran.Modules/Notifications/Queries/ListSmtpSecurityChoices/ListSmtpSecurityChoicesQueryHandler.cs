using Maran.Modules.Notifications.Common;
using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Services;

namespace Maran.Modules.Notifications.Queries.ListSmtpSecurityChoices;

/// <summary>
/// Handles <see cref="ListSmtpSecurityChoicesQuery"/> by naming every <see cref="SmtpSecurity"/>
/// member for the request's culture.
/// </summary>
public sealed class ListSmtpSecurityChoicesQueryHandler
{
    /// <summary>Names each mode as an administrator reads it, in the request's culture.</summary>
    private readonly SmtpSecurityDisplayNames _securityNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="securityNames">Names a protection mode for the request's culture.</param>
    public ListSmtpSecurityChoicesQueryHandler(SmtpSecurityDisplayNames securityNames)
    {
        _securityNames = securityNames;
    }

    /// <summary>Returns every protection mode, named, in declaration order.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the choices; this operation never fails.</returns>
    public Task<Result<IReadOnlyList<SmtpSecurityChoiceDto>>> HandleAsync(
        ListSmtpSecurityChoicesQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SmtpSecurityChoiceDto> choices = Enum.GetValues<SmtpSecurity>()
            .Select(security =>
            {
                return new SmtpSecurityChoiceDto(security, _securityNames.Of(security));
            })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<SmtpSecurityChoiceDto>>.Ok(choices));
    }
}
