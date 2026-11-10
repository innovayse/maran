using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Mappers;

/// <summary>Restates a discovered account, with its sites and databases, as its wire shapes.</summary>
public static class DiscoveredAccountMapper
{
    /// <summary>The separator the stored domain and alias texts are joined with.</summary>
    private const char TextSeparator = ' ';

    /// <summary>Restates an account for the list.</summary>
    /// <param name="account">The stored account.</param>
    /// <returns>The list shape.</returns>
    public static DiscoveredAccountDto ToDto(DiscoveredAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new DiscoveredAccountDto(
            account.Id,
            account.Name,
            account.Home,
            account.DiskBytes,
            account.DiskKnown,
            account.SiteCount,
            account.DatabaseCount,
            account.CronEntryCount,
            account.MailboxCount,
            Words(account.DomainsText));
    }

    /// <summary>Restates an account in full.</summary>
    /// <param name="account">The stored account, with its sites and databases loaded.</param>
    /// <param name="names">The module's display names, in the caller's language.</param>
    /// <returns>The detail shape.</returns>
    public static DiscoveredAccountDetailDto ToDetail(DiscoveredAccount account, IStringLocalizer<DisplayNames> names)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(names);

        var sites = account.Sites
            .OrderBy(
                site =>
                {
                    return site.Domain;
                },
                StringComparer.Ordinal)
            .Select(site =>
            {
                return new DiscoveredSiteDto(
                    site.Domain,
                    Words(site.AliasesText),
                    site.Backend,
                    MigrationEnumNames.Of(names, site.Backend),
                    site.PhpVersion,
                    site.DocumentRoot);
            })
            .ToList();

        var databases = account.Databases
            .OrderBy(
                database =>
                {
                    return database.Name;
                },
                StringComparer.Ordinal)
            .Select(database =>
            {
                return new DiscoveredDatabaseDto(database.Name, database.SizeBytes, database.SizeKnown);
            })
            .ToList();

        return new DiscoveredAccountDetailDto(
            account.Id,
            account.Name,
            account.Home,
            account.DiskBytes,
            account.DiskKnown,
            account.SiteCount,
            account.DatabaseCount,
            account.CronEntryCount,
            account.MailboxCount,
            Words(account.DomainsText),
            sites,
            databases);
    }

    /// <summary>Splits a stored space-joined text back into its words.</summary>
    /// <param name="text">The stored text.</param>
    /// <returns>The words, in stored order; empty for an empty text.</returns>
    private static string[] Words(string text)
    {
        return text.Split(TextSeparator, StringSplitOptions.RemoveEmptyEntries);
    }
}
