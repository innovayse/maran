using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Mappers;

/// <summary>
/// Puts a stored discovery warning on the wire with its sentence in the caller's language. The sentence is the
/// backend's: the interface shows <c>message</c> as received and holds no table of warning texts of its own.
/// </summary>
public static class DiscoveryWarningMapper
{
    /// <summary>The display-name key of the sentence for a kind. The sentence is about the kind; the subject travels beside it.</summary>
    /// <param name="kind">The warning's kind.</param>
    /// <returns>The key in <c>DisplayNames</c>.</returns>
    public static string KeyOf(DiscoveryWarningKind kind)
    {
        return kind switch
        {
            DiscoveryWarningKind.NameRefused => "DiscoveryWarningNameRefused",
            DiscoveryWarningKind.SiteUnreadable => "DiscoveryWarningSiteUnreadable",
            DiscoveryWarningKind.CronUnreadable => "DiscoveryWarningCronUnreadable",
            DiscoveryWarningKind.DatabasesUnreadable => "DiscoveryWarningDatabasesUnreadable",
            DiscoveryWarningKind.DiskUnreadable => "DiscoveryWarningDiskUnreadable",
            DiscoveryWarningKind.Truncated => "DiscoveryWarningTruncated",
            DiscoveryWarningKind.MailNotMigrated => "DiscoveryWarningMailNotMigrated",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown discovery warning kind."),
        };
    }

    /// <summary>Restates a stored warning for the wire.</summary>
    /// <param name="warning">The stored warning.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    /// <returns>The warning with its sentence.</returns>
    public static DiscoveryWarningDto From(DiscoveryWarning warning, IStringLocalizer<DisplayNames> names)
    {
        ArgumentNullException.ThrowIfNull(warning);
        ArgumentNullException.ThrowIfNull(names);

        return new DiscoveryWarningDto(warning.Kind, warning.Subject, names[KeyOf(warning.Kind)]);
    }
}
