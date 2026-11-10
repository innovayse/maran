using System.Text.Json;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Models;
using Maran.Modules.ServerMigration.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;

namespace Maran.Modules.ServerMigration.Mappers;

/// <summary>
/// Restates what the discovery found for one account, and what the administrator mapped it to, as the sections the
/// modules' importers read (issue #127's contract, reused by issue #197).
/// </summary>
/// <remarks>
/// <para>
/// <b>The section vocabulary is the modules'.</b> This module cannot reference Accounts, Sites or Databases, so the
/// payloads are written here as the JSON shapes those modules read (<c>accounts</c>, <c>sites</c>,
/// <c>databases</c>, each version 1), and the importer of each judges its own section again: the same validators run,
/// so an import from a server is never a weaker way of creating things than the screen.
/// </para>
/// <para>
/// <b>It records, it does not repair.</b> A database whose name cannot be created under that name
/// (<see cref="ImportedDatabaseNamePolicy"/>) is left out of its section and reported as a conflict; a site served
/// by a reverse proxy has no upstream in the inventory and is reported and left out; cron entries are only counted
/// by the discovery and so are reported, not carried; mail is counted and not carried (decision D16). The administrator
/// reads each in the dry run before anything is created.
/// </para>
/// </remarks>
public static class MigrationSectionMapper
{
    /// <summary>The module id the Accounts module's importer answers to.</summary>
    private const string AccountsModuleId = "accounts";

    /// <summary>The module id the Sites module's importer answers to.</summary>
    private const string SitesModuleId = "sites";

    /// <summary>The module id the Databases module's importer answers to.</summary>
    private const string DatabasesModuleId = "databases";

    /// <summary>The version of every section's payload this mapper writes.</summary>
    private const int SectionVersion = 1;

    /// <summary>The sections for one account, with what they could not hold.</summary>
    /// <param name="account">The job's row for the account.</param>
    /// <returns>The sections and the findings.</returns>
    public static MigrationSectionComposition Compose(MigrationJobAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var inventory = ReadInventory(account);
        var findings = new List<MigrationFinding>();
        var sections = new List<MigrationSection>
        {
            new
            {
                name = account.SourceAccountName,
                primaryDomain = account.PrimaryDomain ?? string.Empty,
                ownerEmail = account.OwnerEmail,
                ownerLanguage = account.OwnerLanguage,
                planName = (string?)null,
                shellAccess = false,
            }.ToSection(AccountsModuleId, SectionVersion),
        };

        var sites = new List<object>();
        foreach (var site in inventory.Sites)
        {
            if (site.Backend == DiscoveredSiteBackend.Proxy)
            {
                findings.Add(Finding(AccountsModuleId, MigrationFindingSeverity.Warning, nameof(ErrorMessages.MigrationSiteProxyNotImported), site.Domain));
                continue;
            }

            sites.Add(new
            {
                domain = site.Domain,
                aliases = site.Aliases,
                backendType = site.Backend == DiscoveredSiteBackend.Php ? 2 : 1,
                phpVersion = site.PhpVersion ?? string.Empty,
                proxyUpstream = string.Empty,
                enabled = true,
            });
        }

        if (sites.Count > 0)
        {
            sections.Add(new { sites }.ToSection(SitesModuleId, SectionVersion, sites.Count));
        }

        var databases = new List<object>();
        foreach (var database in inventory.Databases)
        {
            var suffix = ImportedDatabaseNamePolicy.SuffixOf(account.SourceAccountName, database.Name);
            if (suffix is null)
            {
                findings.Add(Finding(DatabasesModuleId, MigrationFindingSeverity.Conflict, nameof(ErrorMessages.MigrationDatabaseNameNotImportable), database.Name));
                continue;
            }

            databases.Add(new { name = suffix, dbUserNameSuffix = suffix });
        }

        if (databases.Count > 0)
        {
            sections.Add(new { databases }.ToSection(DatabasesModuleId, SectionVersion, databases.Count));
        }

        if (account.CronEntryCount > 0)
        {
            findings.Add(Finding(AccountsModuleId, MigrationFindingSeverity.Warning, nameof(ErrorMessages.MigrationCronNotMigrated), account.SourceAccountName));
        }

        if (account.MailboxCount > 0)
        {
            findings.Add(Finding(AccountsModuleId, MigrationFindingSeverity.Warning, nameof(ErrorMessages.MigrationMailNotMigrated), account.SourceAccountName));
        }

        return new MigrationSectionComposition(sections, findings);
    }

    /// <summary>Reads the snapshot the job holds for the account.</summary>
    /// <param name="account">The job's row for the account.</param>
    /// <returns>The snapshot; empty when the stored text is not one (it was written by this panel, so it is).</returns>
    public static MigrationInventorySnapshot ReadInventory(MigrationJobAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        try
        {
            return JsonSerializer.Deserialize<MigrationInventorySnapshot>(account.InventoryJson, JsonSerializerOptions.Web)
                ?? new MigrationInventorySnapshot([], []);
        }
        catch (JsonException)
        {
            return new MigrationInventorySnapshot([], []);
        }
    }

    /// <summary>Writes a snapshot as the text the job stores.</summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns>The JSON.</returns>
    public static string WriteInventory(MigrationInventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return JsonSerializer.Serialize(snapshot, JsonSerializerOptions.Web);
    }

    /// <summary>Builds one finding.</summary>
    /// <param name="moduleId">The module the finding is filed under.</param>
    /// <param name="severity">How much it matters.</param>
    /// <param name="code">The code, a key of this module's error messages.</param>
    /// <param name="subject">What it is about.</param>
    /// <returns>The finding.</returns>
    private static MigrationFinding Finding(string moduleId, MigrationFindingSeverity severity, string code, string subject)
    {
        return new MigrationFinding(moduleId, severity, code, subject);
    }
}
