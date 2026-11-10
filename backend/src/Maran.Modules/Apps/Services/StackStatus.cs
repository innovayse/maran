using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Persistence;
using Maran.SharedKernel.Apps;

namespace Maran.Modules.Apps.Services;

/// <summary>
/// Answers <see cref="IStackStatus"/> from the panel's ledger of installed apps: the one place "is the
/// stack ready" is decided, for the gate in the Sites module and for the endpoint the SPA reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only an installed app counts.</b> An app being installed, one that failed and one being removed are
/// not served from, so a web server mid-install leaves the stack not ready: a site created in that window
/// would be written against a daemon that is not there yet.
/// </para>
/// <para>
/// <b>The ledger, not a probe.</b> The gate runs on every site-mutating request, so it costs one indexed
/// read and never a round trip to the agent. The ledger is reconciled against the host at start-up and
/// moved by the install and removal jobs, which is the only way the app set changes through the panel.
/// </para>
/// <para>
/// <b>One reading per scope.</b> The members are synchronous properties, so the ledger is read once, on
/// first use, with a synchronous query over a table of at most a dozen rows, and the same answer serves
/// the rest of the request. The service is scoped for that reason: a singleton would never see an install
/// finish.
/// </para>
/// </remarks>
public sealed class StackStatus : IStackStatus
{
    /// <summary>The Apps module's database context.</summary>
    private readonly AppsDbContext _dbContext;

    /// <summary>The reading taken from the ledger, or null before the first member is read.</summary>
    private Reading? _reading;

    /// <summary>Creates the reader.</summary>
    /// <param name="dbContext">The Apps module's database context.</param>
    public StackStatus(AppsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public bool IsReady
    {
        get { return Read().WebServer != StackWebServer.None; }
    }

    /// <inheritdoc />
    public StackWebServer WebServer
    {
        get { return Read().WebServer; }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> PhpVersions
    {
        get { return Read().PhpVersions; }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> NodeMajors
    {
        get { return Read().NodeMajors; }
    }

    /// <summary>Takes the reading, once.</summary>
    /// <returns>The reading.</returns>
    private Reading Read()
    {
        if (_reading is not null)
        {
            return _reading;
        }

        var installed = _dbContext.InstalledApps
            .AsNoTracking()
            .Where(app => app.State == AppState.Installed)
            .Select(app => app.AppId)
            .ToList();

        var webServer = StackWebServer.None;
        var php = new List<string>();
        var node = new List<string>();
        foreach (var text in installed)
        {
            if (!AppId.TryParse(text, out var appId))
            {
                // A row the catalogue does not know (a catalogue that shrank in an update) serves no site.
                continue;
            }

            switch (appId.Family)
            {
                case AppFamily.Nginx:
                    webServer = StackWebServer.Nginx;
                    break;
                case AppFamily.Apache:
                    webServer = StackWebServer.Apache;
                    break;
                case AppFamily.Php:
                    php.Add(appId.Version);
                    break;
                case AppFamily.Node:
                    node.Add(appId.Version);
                    break;
            }
        }

        _reading = new Reading(webServer, NewestFirst(php, AppId.PhpVersions), NewestFirst(node, AppId.NodeVersions));
        return _reading;
    }

    /// <summary>Orders installed versions the way the catalogue lists them, newest first.</summary>
    /// <param name="installed">The installed versions, in no order.</param>
    /// <param name="catalogue">The catalogue's versions, newest first.</param>
    /// <returns>The installed versions in the catalogue's order.</returns>
    private static List<string> NewestFirst(List<string> installed, IReadOnlyList<string> catalogue)
    {
        return catalogue.Where(version => { return installed.Contains(version); }).ToList();
    }

    /// <summary>One consistent reading of the ledger.</summary>
    /// <param name="WebServer">The installed site web server.</param>
    /// <param name="PhpVersions">The installed PHP versions.</param>
    /// <param name="NodeMajors">The installed Node.js majors.</param>
    private sealed record Reading(
        StackWebServer WebServer,
        IReadOnlyList<string> PhpVersions,
        IReadOnlyList<string> NodeMajors);
}
