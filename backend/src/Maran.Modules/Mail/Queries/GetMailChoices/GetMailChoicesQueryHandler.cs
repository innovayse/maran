using System.Text.Json;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Mail.Queries.GetMailChoices;

/// <summary>
/// Handles <see cref="GetMailChoicesQuery"/>: the localized lists the mail screens select from and the
/// numeric bounds their forms validate against.
/// </summary>
/// <remarks>
/// <para>
/// The bounds depend on who asks. A customer is held to their own plan: the default quota and send
/// limit are the plan's, the send ceiling is the plan's send limit, and the quota ceiling is the
/// smaller of the plan's pool and the module's hard maximum. Anyone else (an administrator creating
/// on a customer's behalf, a reseller) is offered the module's defaults and hard maxima, because the
/// plan that will apply is the target account's and the create operation enforces it.
/// </para>
/// <para>
/// These are hints for the form. The create and update operations re-check every bound on the server.
/// </para>
/// </remarks>
public sealed class GetMailChoicesQueryHandler
{
    /// <summary>The smallest mailbox quota the module accepts, in MiB.</summary>
    private const int MinimumQuotaMib = 1;

    /// <summary>The window onto the caller's plan.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Tells the handler who is asking.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>The module's localized display names.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="accounts">Reads the calling customer's plan.</param>
    /// <param name="currentUser">Says who is asking.</param>
    /// <param name="names">The module's localized display names.</param>
    public GetMailChoicesQueryHandler(
        IAccountDirectory accounts,
        ICurrentUser currentUser,
        IStringLocalizer<DisplayNames> names)
    {
        _accounts = accounts;
        _currentUser = currentUser;
        _names = names;
    }

    /// <summary>Builds the choices for the caller.</summary>
    /// <param name="query">The (parameterless) query.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The choices, or <c>AccountNotFound</c> when a customer's account cannot be read.</returns>
    public async Task<Result<MailChoicesDto>> HandleAsync(
        GetMailChoicesQuery query,
        CancellationToken cancellationToken)
    {
        MailFormLimitsDto limits;
        if (_currentUser.Caller is CustomerCaller customer)
        {
            var account = await _accounts.FindAsync(customer.AccountId, cancellationToken);
            if (account is null)
            {
                return Result<MailChoicesDto>.Fail(
                    Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
            }

            limits = LimitsOf(
                account.MailLimits.DefaultMailboxQuotaMib,
                Math.Min(account.MailLimits.MailboxQuotaPoolMib, MailDefaults.MaxMailboxQuotaMib),
                account.MailLimits.MailboxSendLimitPerHour,
                account.MailLimits.MailboxSendLimitPerHour);
        }
        else
        {
            limits = LimitsOf(
                MailLimitsSnapshot.Default.DefaultMailboxQuotaMib,
                MailDefaults.MaxMailboxQuotaMib,
                MailLimitsSnapshot.Default.MailboxSendLimitPerHour,
                MailDefaults.MaxSendLimitPerHour);
        }

        return Result<MailChoicesDto>.Ok(new MailChoicesDto(
            ChoicesOf<MailDomainStatus>("MailDomainStatus"),
            ChoicesOf<MailboxStatus>("MailboxStatus"),
            ChoicesOf<MailAliasKind>("MailAliasKind"),
            limits));
    }

    /// <summary>Assembles the bounds from the four figures that differ between callers.</summary>
    /// <param name="defaultQuotaMib">The quota a new mailbox is offered.</param>
    /// <param name="maximumQuotaMib">The largest quota the caller may choose.</param>
    /// <param name="defaultSendLimit">The hourly send limit a new mailbox is offered.</param>
    /// <param name="maximumSendLimit">The highest hourly send limit the caller may choose.</param>
    /// <returns>The bounds.</returns>
    private static MailFormLimitsDto LimitsOf(
        int defaultQuotaMib,
        int maximumQuotaMib,
        int defaultSendLimit,
        int maximumSendLimit)
    {
        return new MailFormLimitsDto(
            MinimumQuotaMib,
            maximumQuotaMib,
            defaultQuotaMib,
            MailDefaults.PasswordMinLength,
            maximumSendLimit,
            defaultSendLimit,
            MailDefaults.MaxAliasTargets);
    }

    /// <summary>Lists every member of an enum as a value and a localized label.</summary>
    /// <typeparam name="TEnum">The enum whose members are listed.</typeparam>
    /// <param name="keyPrefix">The display-name resource prefix; the member's name completes the key.</param>
    /// <returns>One choice per member, in declaration order.</returns>
    private List<MailChoiceDto> ChoicesOf<TEnum>(string keyPrefix)
        where TEnum : struct, Enum
    {
        return Enum.GetValues<TEnum>()
            .Select(member =>
            {
                return new MailChoiceDto(
                    JsonNamingPolicy.CamelCase.ConvertName(member.ToString()),
                    _names[string.Concat(keyPrefix, member.ToString())].Value);
            })
            .ToList();
    }
}
