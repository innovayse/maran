using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sites.Commands.CreateSite;
using Maran.Modules.Sites.Commands.DisableSite;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Models;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;
using Wolverine;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Judges and creates an imported account's sites (issue #127). The Sites module's
/// <see cref="IAccountMigrationImporter"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Each site is created through <see cref="CreateSiteCommand"/></b>, so the hostname rules, the account's
/// site limit and the vhost the agent writes are the ordinary ones. A site that was not serving is disabled
/// again afterwards, through the ordinary command.
/// </para>
/// <para>
/// <b>It creates only what lives outside the home.</b> A site's vhost and PHP pool are configuration; the
/// files under the home arrive with the restore that runs after every importer. A conflict is reported when a
/// hostname is already claimed on this server or a PHP version is not installed here: the panel does not
/// install a runtime on an administrator's behalf as a side effect of an import.
/// </para>
/// </remarks>
public sealed class SitesMigrationImporter : IAccountMigrationImporter
{
    /// <summary>Tells which hostnames are already claimed on this server.</summary>
    private readonly SiteHostnameClaims _claims;

    /// <summary>Lists the PHP versions installed on this server.</summary>
    private readonly IAgentPhpClient _php;

    /// <summary>The bus each site is created through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the importer.</summary>
    /// <param name="claims">Tells which hostnames are already claimed.</param>
    /// <param name="php">Lists the installed PHP versions.</param>
    /// <param name="bus">The bus each site is created through.</param>
    public SitesMigrationImporter(SiteHostnameClaims claims, IAgentPhpClient php, IMessageBus bus)
    {
        _claims = claims;
        _php = php;
        _bus = bus;
    }

    /// <inheritdoc />
    public string ModuleId
    {
        get { return SitesManifest.Instance.Id; }
    }

    /// <inheritdoc />
    public int Order
    {
        get { return 30; }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MigrationFinding>> PlanAsync(
        MigrationSection section,
        MigrationPlanContext context,
        CancellationToken cancellationToken)
    {
        var payload = section.ReadPayload<SitesMigrationSection>(ModuleId, SitesMigrationExporter.SectionVersion);

        if (payload is null)
        {
            return [new MigrationFinding(ModuleId, MigrationFindingSeverity.Conflict, MigrationCodes.SectionInvalid, context.AccountName)];
        }

        var findings = new List<MigrationFinding>();
        var installed = await InstalledPhpVersionsAsync(cancellationToken);

        foreach (var site in payload.Sites)
        {
            var names = new[] { site.Domain }.Concat(site.Aliases).Select(name =>
            {
                return name.ToLowerInvariant();
            }).ToList();

            foreach (var claimed in await _claims.ClaimingSiteDomainsAsync(names, cancellationToken))
            {
                findings.Add(new MigrationFinding(ModuleId, MigrationFindingSeverity.Conflict, MigrationCodes.DomainTaken, claimed));
            }

            if (site.BackendType == SiteBackendType.Php && installed is not null && !installed.Contains(site.PhpVersion))
            {
                findings.Add(new MigrationFinding(ModuleId, MigrationFindingSeverity.Conflict, MigrationCodes.PhpVersionMissing, site.Domain));
            }
        }

        return findings;
    }

    /// <inheritdoc />
    public async Task<Result<MigrationSectionOutcome>> ImportAsync(
        MigrationSection section,
        MigrationImportTarget target,
        CancellationToken cancellationToken)
    {
        var payload = section.ReadPayload<SitesMigrationSection>(ModuleId, SitesMigrationExporter.SectionVersion);

        if (payload is null)
        {
            return Result<MigrationSectionOutcome>.Fail(
                Error.Of(MigrationCodes.SectionInvalid, ErrorType.Validation));
        }

        if (target.AccountId is not { } accountId)
        {
            return Result<MigrationSectionOutcome>.Fail(
                Error.Of(MigrationCodes.TargetAccountMissing, ErrorType.Failure));
        }

        foreach (var site in payload.Sites)
        {
            var created = await _bus.InvokeAsync<Result<SiteDto>>(
                new CreateSiteCommand(
                    accountId,
                    site.Domain,
                    site.BackendType,
                    site.Aliases,
                    site.PhpVersion,
                    site.ProxyUpstream,
                    NodeVersion: site.NodeVersion,
                    AppPort: site.AppPort,
                    StartCommand: site.StartCommand),
                cancellationToken);

            if (!created.IsSuccess)
            {
                return Result<MigrationSectionOutcome>.Fail(created.Error!);
            }

            if (!site.Enabled)
            {
                var disabled = await _bus.InvokeAsync<Result<SiteDto>>(
                    new DisableSiteCommand(created.Value!.Id, string.Empty, string.Empty),
                    cancellationToken);

                if (!disabled.IsSuccess)
                {
                    return Result<MigrationSectionOutcome>.Fail(disabled.Error!);
                }
            }
        }

        return Result<MigrationSectionOutcome>.Ok(new MigrationSectionOutcome(null, []));
    }

    /// <summary>Reads the PHP versions installed on this server.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The installed versions, or <c>null</c> when the agent could not say (nothing is then judged missing).</returns>
    private async Task<HashSet<string>?> InstalledPhpVersionsAsync(CancellationToken cancellationToken)
    {
        var versions = await _php.ListVersionsAsync(cancellationToken);

        return versions.IsSuccess ? versions.Value!.Select(version =>
            {
                return version.Version;
            }).ToHashSet(StringComparer.Ordinal) : null;
    }
}
