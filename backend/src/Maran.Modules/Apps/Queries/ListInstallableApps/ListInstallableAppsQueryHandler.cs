using Maran.Agent.Client.Interfaces;
using Maran.Modules.Apps.Common;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Mappers;
using Maran.Modules.Apps.Persistence;
using Maran.Modules.Apps.Services;

namespace Maran.Modules.Apps.Queries.ListInstallableApps;

/// <summary>
/// Handles <see cref="ListInstallableAppsQuery"/>: the apps the agent says the host's package sources can
/// install, each flagged as installed or not (issue #517), with the reason no Node.js app is among them when
/// the host could not offer one (issue #532).
/// </summary>
/// <remarks>
/// <para>
/// <b>Never a static list.</b> What is offered is what the agent reads from the package metadata on this
/// host (issue #466), so a picker built on this never offers an app the install would refuse; the install
/// handler asks the same list before it begins a task.
/// </para>
/// <para>
/// <b>Installed means installed or on its way.</b> An app the agent reports installed, or one the ledger
/// holds as installed or installing, is shown as taken, so the first-run screen does not offer a second
/// install of what is already running as a task.
/// </para>
/// </remarks>
public sealed class ListInstallableAppsQueryHandler
{
    /// <summary>The agent's app store surface.</summary>
    private readonly IAgentAppsClient _agent;

    /// <summary>The Apps module's database context.</summary>
    private readonly AppsDbContext _dbContext;

    /// <summary>Names an app and its kind in the request's language.</summary>
    private readonly AppDisplayNames _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent's app store surface.</param>
    /// <param name="dbContext">The Apps module's database context.</param>
    /// <param name="names">Names an app and its kind in the request's language.</param>
    public ListInstallableAppsQueryHandler(IAgentAppsClient agent, AppsDbContext dbContext, AppDisplayNames names)
    {
        _agent = agent;
        _dbContext = dbContext;
        _names = names;
    }

    /// <summary>Returns the installable apps in the agent's catalogue order.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The apps with the reason Node.js is missing, if it is, or the agent's own typed failure.</returns>
    public async Task<Result<InstallableAppsDto>> HandleAsync(
        ListInstallableAppsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var installable = await _agent.ListInstallableAppsAsync(cancellationToken);
        if (!installable.IsSuccess)
        {
            return Result<InstallableAppsDto>.Fail(installable.Error!);
        }

        var taken = (await _dbContext.InstalledApps
                .AsNoTracking()
                .Where(app => app.State == AppState.Installed || app.State == AppState.Installing)
                .Select(app => app.AppId)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var apps = new List<InstallableAppDto>(installable.Value.Apps.Count);
        foreach (var app in installable.Value.Apps)
        {
            if (!AppId.TryParse(app.AppId, out var appId))
            {
                continue;
            }

            apps.Add(new InstallableAppDto(
                appId.Value,
                _names.Of(appId),
                appId.Kind,
                _names.Of(appId.Kind),
                app.Installed || taken.Contains(appId.Value)));
        }

        var unavailable = AgentAppMapper.ToNodejsUnavailable(installable.Value.NodejsUnavailable);

        return Result<InstallableAppsDto>.Ok(
            new InstallableAppsDto(apps, unavailable, unavailable is { } reason ? _names.Of(reason) : null));
    }
}
