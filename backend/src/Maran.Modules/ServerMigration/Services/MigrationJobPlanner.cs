using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Models;
using Maran.Modules.ServerMigration.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.ServerMigration.Services;

/// <summary>
/// The dry run of a whole-server migration: judges every mapped account of a job against this server and changes
/// nothing (issue #197, phase 2; the importers' <c>PlanAsync</c> of issue #127, reused).
/// </summary>
/// <remarks>
/// <para>
/// <b>Read-only.</b> Every importer's <c>PlanAsync</c> reads and no more, and this service writes no row; it runs
/// again, with the same code, when the job is started, because the state of this server can change between the
/// administrator reading the report and pressing start.
/// </para>
/// <para>
/// <b>No rename (decision D1).</b> A name that is taken here is a conflict on the account that carries it, never a
/// reason to pick another; the importers state it (<c>MigrationNameTaken</c>, <c>MigrationDomainTaken</c>) and this
/// service adds the conflicts only a whole job can have: two of its accounts claiming one name or one domain.
/// </para>
/// </remarks>
public sealed class MigrationJobPlanner
{
    /// <summary>Every module's importer, in the order they run.</summary>
    private readonly IReadOnlyList<IAccountMigrationImporter> _importers;

    /// <summary>Creates the planner.</summary>
    /// <param name="importers">Every module's importer.</param>
    public MigrationJobPlanner(IEnumerable<IAccountMigrationImporter> importers)
    {
        _importers = importers.OrderBy(importer =>
        {
            return importer.Order;
        }).ToList();
    }

    /// <summary>Judges the accounts of a job.</summary>
    /// <param name="accounts">The job's accounts that are not already ended.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every finding, each with the account it is about; empty when the job would import cleanly.</returns>
    public async Task<IReadOnlyList<MigrationAccountFinding>> PlanAsync(
        IReadOnlyList<MigrationJobAccount> accounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        var findings = new List<MigrationAccountFinding>();

        foreach (var account in accounts)
        {
            if (!account.IsMapped)
            {
                findings.Add(Conflict(account, "accounts", nameof(ErrorMessages.MigrationMappingIncomplete), account.SourceAccountName));
                continue;
            }

            var composition = MigrationSectionMapper.Compose(account);
            findings.AddRange(composition.Findings.Select(finding => { return new MigrationAccountFinding(account.SourceAccountName, finding); }));

            var context = new MigrationPlanContext(
                account.SourceAccountName,
                account.PlanId!.Value,
                composition.Sections.ToDictionary(section =>
                {
                    return section.ModuleId;
                }, section =>
                {
                    return section.ResourceCount;
                }),
                account.SourceDiskBytes ?? 0);

            foreach (var importer in _importers)
            {
                var section = composition.Sections.FirstOrDefault(row =>
                {
                    return row.ModuleId == importer.ModuleId;
                });
                if (section is null)
                {
                    continue;
                }

                var planned = await importer.PlanAsync(section, context, cancellationToken);
                findings.AddRange(planned.Select(finding => { return new MigrationAccountFinding(account.SourceAccountName, finding); }));
            }
        }

        findings.AddRange(DuplicatesWithinJob(accounts));

        return findings
            .OrderByDescending(row =>
            {
                return row.Finding.Severity;
            })
            .ThenBy(row =>
            {
                return row.SourceAccountName;
            }, StringComparer.Ordinal)
            .ThenBy(row =>
            {
                return row.Finding.ModuleId;
            }, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Finds two accounts of the job that claim one primary domain or one site name.</summary>
    /// <param name="accounts">The job's accounts.</param>
    /// <returns>A conflict for each account that shares a name with another.</returns>
    private static IEnumerable<MigrationAccountFinding> DuplicatesWithinJob(IReadOnlyList<MigrationJobAccount> accounts)
    {
        var claims = new List<(string Domain, MigrationJobAccount Account)>();
        foreach (var account in accounts)
        {
            if (!string.IsNullOrWhiteSpace(account.PrimaryDomain))
            {
                claims.Add((account.PrimaryDomain.ToLowerInvariant(), account));
            }

            foreach (var site in MigrationSectionMapper.ReadInventory(account).Sites)
            {
                claims.Add((site.Domain.ToLowerInvariant(), account));
                claims.AddRange(site.Aliases.Select(alias => { return (alias.ToLowerInvariant(), account); }));
            }
        }

        return claims
            .GroupBy(claim =>
            {
                return claim.Domain;
            }, StringComparer.Ordinal)
            .Where(group =>
            {
                return group.Select(claim =>
            {
                return claim.Account.Id;
            }).Distinct().Count() > 1;
            })
            .SelectMany(group =>
            {
                return group.Select(claim => { return Conflict(claim.Account, "accounts", nameof(ErrorMessages.MigrationDuplicateInJob), group.Key); });
            })
            .DistinctBy(row =>
            {
                return (row.SourceAccountName, row.Finding.Subject);
            });
    }

    /// <summary>Builds a conflict about an account.</summary>
    /// <param name="account">The account.</param>
    /// <param name="moduleId">The module the finding is filed under.</param>
    /// <param name="code">The code.</param>
    /// <param name="subject">What it is about.</param>
    /// <returns>The finding.</returns>
    private static MigrationAccountFinding Conflict(MigrationJobAccount account, string moduleId, string code, string subject)
    {
        return new MigrationAccountFinding(
            account.SourceAccountName,
            new MigrationFinding(moduleId, MigrationFindingSeverity.Conflict, code, subject));
    }
}
