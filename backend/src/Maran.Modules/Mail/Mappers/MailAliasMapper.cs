using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Mail.Mappers;

/// <summary>Translates an alias row into the shape a screen shows. Restates; decides nothing.</summary>
public static class MailAliasMapper
{
    /// <summary>Restates an alias for the wire.</summary>
    /// <param name="alias">The row, with its targets loaded.</param>
    /// <param name="domainName">The name of the domain the alias lives in.</param>
    /// <param name="ownerNames">The names of the owning accounts, by account id.</param>
    /// <param name="displayNames">The module's localized names of things that are not failures.</param>
    /// <returns>The wire shape, targets in the order the owner gave.</returns>
    public static MailAliasDto From(
        MailAlias alias,
        string domainName,
        IReadOnlyDictionary<Guid, string> ownerNames,
        IStringLocalizer<DisplayNames> displayNames)
    {
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(domainName);
        ArgumentNullException.ThrowIfNull(ownerNames);
        ArgumentNullException.ThrowIfNull(displayNames);

        var targets = alias.Targets
            .OrderBy(target =>
            {
                return target.Position;
            })
            .Select(target =>
            {
                return target.Address;
            })
            .ToList();

        return new MailAliasDto(
            alias.Id,
            alias.AccountId,
            ownerNames.GetValueOrDefault(alias.AccountId, string.Empty),
            alias.MailDomainId,
            MailboxAddressPolicy.Compose(alias.LocalPart, domainName),
            alias.Kind,
            displayNames[KindNameKey(alias.Kind)],
            targets,
            alias.CreatedAt);
    }

    /// <summary>The resource key of a kind's localized name.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The <c>DisplayNames</c> key that names it.</returns>
    private static string KindNameKey(MailAliasKind kind)
    {
        return kind switch
        {
            MailAliasKind.Alias => "MailAliasKindAlias",
            MailAliasKind.Forwarder => "MailAliasKindForwarder",
            MailAliasKind.CatchAll => "MailAliasKindCatchAll",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Undefined alias kind."),
        };
    }
}
