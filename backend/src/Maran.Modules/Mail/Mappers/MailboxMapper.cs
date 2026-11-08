using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Mail.Mappers;

/// <summary>Restates a <see cref="Mailbox"/> as the wire shape a screen reads.</summary>
/// <remarks>
/// The one place a <see cref="MailboxDto"/> is built, so the list and every create and change answer
/// cannot drift apart. A mapper translates; it never decides (rules/csharp.md): the status arrives
/// already derived by the handler from the owner's switch and the account's state, and the usage arrives
/// already read from the mail host. This only restates them, and names the status in the caller's
/// language.
/// </remarks>
public static class MailboxMapper
{
    /// <summary>Bytes in one mebibyte, the unit the quota and the usage are shown in.</summary>
    private const ulong BytesPerMib = 1_048_576;

    /// <summary>Builds the outward view of one mailbox.</summary>
    /// <param name="mailbox">The recorded mailbox.</param>
    /// <param name="domainName">The name of the domain the mailbox lives in, which makes its address.</param>
    /// <param name="ownerNames">
    /// Account names the account directory answered for the rows being mapped, tenant-scoped. An account
    /// it did not name is absent, and the row's owner name is then empty.
    /// </param>
    /// <param name="usedMib">The usage in MiB, or <c>null</c> when the mail host reported none.</param>
    /// <param name="status">The mailbox's status, already derived.</param>
    /// <param name="names">The display names of the module, which name the status.</param>
    /// <returns>The wire shape carrying exactly what the row holds, and its owner's name.</returns>
    public static MailboxDto From(
        Mailbox mailbox,
        string domainName,
        IReadOnlyDictionary<Guid, string> ownerNames,
        long? usedMib,
        MailboxStatus status,
        IStringLocalizer<DisplayNames> names)
    {
        ArgumentNullException.ThrowIfNull(mailbox);
        ArgumentNullException.ThrowIfNull(domainName);
        ArgumentNullException.ThrowIfNull(ownerNames);
        ArgumentNullException.ThrowIfNull(names);

        return new MailboxDto(
            mailbox.Id,
            mailbox.AccountId,
            ownerNames.GetValueOrDefault(mailbox.AccountId, string.Empty),
            mailbox.MailDomainId,
            string.Concat(mailbox.LocalPart, "@", domainName),
            mailbox.QuotaMib,
            usedMib,
            mailbox.SendLimitPerHour,
            status,
            names[NameKeyOf(status)],
            mailbox.Enabled,
            mailbox.CreatedAt);
    }

    /// <summary>Converts a byte count the mail host reported into whole mebibytes, rounded up.</summary>
    /// <param name="bytes">Bytes used.</param>
    /// <returns>The mebibytes, never fewer than the bytes occupy.</returns>
    public static long MibOf(ulong bytes)
    {
        return (long)((bytes / BytesPerMib) + (bytes % BytesPerMib == 0 ? 0UL : 1UL));
    }

    /// <summary>The display-name key that spells a status.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The key in <c>DisplayNames.resx</c>.</returns>
    private static string NameKeyOf(MailboxStatus status)
    {
        return status switch
        {
            MailboxStatus.Active => "MailboxStatusActive",
            MailboxStatus.Suspended => "MailboxStatusSuspended",
            MailboxStatus.Disabled => "MailboxStatusDisabled",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown mailbox status."),
        };
    }
}
