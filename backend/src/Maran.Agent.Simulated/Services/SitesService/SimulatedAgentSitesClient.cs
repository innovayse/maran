using System.Diagnostics.CodeAnalysis;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Simulated.Enums;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.State;
using Maran.Agent.Simulated.Validation;

namespace Maran.Agent.Simulated.Services.SitesService;

/// <summary>
/// The vhost, document root and PHP pool behind a domain, kept in memory: nothing is rendered,
/// nothing is written to a web-server configuration and no pool exists, so there is nothing to reload.
/// </summary>
/// <remarks>
/// <para>
/// Answers like the real agent, which is not idempotent here: a domain, or an alias, that another vhost
/// already serves is <c>AgentAlreadyExists</c>; an operation on a vhost that is not there, a delete
/// included, is <c>AgentNotFound</c>;
/// binding to a PHP version that is not installed is <c>AgentValidationFailed</c>. The vhost is
/// looked up by domain AND account, so one account cannot name another's site.
/// </para>
/// <para>
/// The pool of a PHP version is not modelled, so <c>removePreviousPool</c> and the retired version of
/// a delete are accepted and need no action. A site's certificate belongs to the Ssl client and
/// survives the site, as a certificate file survives its vhost include on a real host.
/// </para>
/// <para>
/// Traffic summaries and log pruning are answered from the site's simulated logs
/// (<see cref="SimulatedSiteTraffic"/>); nothing is read from, or removed from, any disk.
/// </para>
/// </remarks>
public sealed class SimulatedAgentSitesClient : IAgentSitesClient
{
    /// <summary>The fewest rotations a prune may keep.</summary>
    private const uint MinRetainedRotations = 1;

    /// <summary>The most rotations a prune may keep.</summary>
    private const uint MaxRetainedRotations = 14;

    /// <summary>How many logs a site has, and so how many files an archive number stands for.</summary>
    private const uint LogsPerSite = 2;

    /// <summary>The simulated host's state.</summary>
    private readonly SimulatedAgentLedger _ledger;

    /// <summary>The source of the current moment.</summary>
    private readonly IClock _clock;

    /// <summary>The source of log-tail events.</summary>
    private readonly SimulatedSiteLogTail _tails;

    /// <summary>Creates a client over <paramref name="ledger"/>.</summary>
    /// <param name="ledger">The simulated host's state.</param>
    /// <param name="clock">The source of the moment the tailed log is stamped with.</param>
    public SimulatedAgentSitesClient(SimulatedAgentLedger ledger, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(clock);

        _ledger = ledger;
        _clock = clock;
        _tails = new SimulatedSiteLogTail(ledger, clock);
    }

    /// <inheritdoc />
    public Task<Result<CreatedSiteDto>> CreateAsync(
        string accountUsername,
        string domain,
        IReadOnlyList<string> aliases,
        SiteBackendKind kind,
        string phpVersion,
        string proxyUpstream,
        SiteWebServer webServer,
        SiteNodeApp node,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(aliases);
        ArgumentNullException.ThrowIfNull(settingOverrides);
        ArgumentNullException.ThrowIfNull(node);

        // A Node site is refused without its runtime facts, as the real agent refuses it; the web server
        // is not modelled by the simulation, which renders no vhost text.
        var nodeComplete = kind != SiteBackendKind.Node
            || (node.Version.Length > 0 && node.Port > 0 && node.StartCommand.Length > 0);
        var wellFormed = Enum.IsDefined(webServer)
            && nodeComplete
            && SimulatedSiteRequestRules.IsCreateWellFormed(
                _ledger.Limits, accountUsername, domain, aliases, kind, phpVersion, proxyUpstream, settingOverrides);
        if (!wellFormed)
        {
            return Task.FromResult(Result<CreatedSiteDto>.Fail(SimulatedAgentErrors.InvalidInput()));
        }

        var result = _ledger.Atomically(
            () =>
            {
                if (!_ledger.Accounts.TryGet(accountUsername, out var account))
                {
                    return Result<CreatedSiteDto>.Fail(SimulatedAgentErrors.NotFound());
                }

                if (kind == SiteBackendKind.Php && !_ledger.PhpRuntimes.Contains(phpVersion))
                {
                    return Result<CreatedSiteDto>.Fail(SimulatedAgentErrors.ValidationFailed());
                }

                if (IsServedAlready(domain, aliases))
                {
                    return Result<CreatedSiteDto>.Fail(SimulatedAgentErrors.AlreadyExists());
                }

                var isPhp = kind == SiteBackendKind.Php;
                var vhost = new SimulatedVhost(
                    accountUsername,
                    domain,
                    aliases.ToList(),
                    kind,
                    isPhp ? phpVersion : string.Empty,
                    kind == SiteBackendKind.ReverseProxy ? proxyUpstream : string.Empty,
                    isPhp ? maxChildren : 0,
                    isPhp ? settingOverrides.ToList() : [],
                    true,
                    account.HomeDirectory + "/sites/" + domain);

                return _ledger.Vhosts.Add(domain, vhost) switch
                {
                    SimulatedLedgerAdd.Added => Result<CreatedSiteDto>.Ok(new CreatedSiteDto(vhost.DocumentRoot)),
                    SimulatedLedgerAdd.AlreadyExists => Result<CreatedSiteDto>.Fail(
                        SimulatedAgentErrors.AlreadyExists()),
                    _ => Result<CreatedSiteDto>.Fail(SimulatedAgentErrors.LimitReached()),
                };
            });

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<Result> ChangePhpVersionAsync(
        string accountUsername,
        string domain,
        string phpVersion,
        SiteDescriptor site,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        bool removePreviousPool,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(settingOverrides);

        if (!SimulatedInput.IsPhpVersion(phpVersion)
            || !SimulatedPhpSettingRules.AreWellFormed(_ledger.Limits, settingOverrides))
        {
            return Task.FromResult(Result.Fail(SimulatedAgentErrors.InvalidInput()));
        }

        var result = _ledger.Atomically(
            () =>
            {
                if (!TryFind(accountUsername, domain, out var vhost))
                {
                    return Result.Fail(SimulatedAgentErrors.NotFound());
                }

                if (vhost.Backend != SiteBackendKind.Php || !_ledger.PhpRuntimes.Contains(phpVersion))
                {
                    return Result.Fail(SimulatedAgentErrors.ValidationFailed());
                }

                _ledger.Vhosts.Replace(
                    domain,
                    current =>
                    {
                        return current with
                        {
                            PhpVersion = phpVersion,
                            MaxChildren = maxChildren,
                            SettingOverrides = settingOverrides.ToList(),
                        };
                    });

                return Result.Ok();
            });

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<Result> EnableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(SetEnabled(accountUsername, domain, true, cancellationToken));
    }

    /// <inheritdoc />
    public Task<Result> DisableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(SetEnabled(accountUsername, domain, false, cancellationToken));
    }

    /// <inheritdoc />
    public Task<Result> DeleteAsync(
        string accountUsername,
        string domain,
        string retiredPhpVersion,
        SiteWebServer webServer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (retiredPhpVersion.Length > 0 && !SimulatedInput.IsPhpVersion(retiredPhpVersion))
        {
            return Task.FromResult(Result.Fail(SimulatedAgentErrors.InvalidInput()));
        }

        var result = _ledger.Atomically(
            () =>
            {
                if (!TryFind(accountUsername, domain, out _))
                {
                    return Result.Fail(SimulatedAgentErrors.NotFound());
                }

                _ledger.Vhosts.Remove(domain);

                return Result.Ok();
            });

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<SiteLogEvent> TailLogAsync(
        string accountUsername,
        string domain,
        SiteLogSource logSource,
        uint historyLines,
        CancellationToken cancellationToken)
    {
        return _tails.EventsAsync(
            accountUsername, domain, logSource, historyLines, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<SiteTrafficReading>> SummariseTrafficAsync(
        string accountUsername,
        string domain,
        SiteTrafficCursor? cursor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = _ledger.Atomically(
            () =>
            {
                return TryFind(accountUsername, domain, out _)
                    ? Result<SiteTrafficReading>.Ok(SimulatedSiteTraffic.Read(domain, cursor, _clock.UtcNow))
                    : Result<SiteTrafficReading>.Fail(SimulatedAgentErrors.NotFound());
            });

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<Result<PrunedSiteLogs>> PruneLogsAsync(
        string accountUsername,
        string domain,
        uint retainRotations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (retainRotations is < MinRetainedRotations or > MaxRetainedRotations)
        {
            return Task.FromResult(Result<PrunedSiteLogs>.Fail(SimulatedAgentErrors.InvalidInput()));
        }

        var result = _ledger.Atomically(
            () =>
            {
                if (!TryFind(accountUsername, domain, out var vhost))
                {
                    return Result<PrunedSiteLogs>.Fail(SimulatedAgentErrors.NotFound());
                }

                // Every archive above the cut is a file of each of the site's two logs.
                var surplus = vhost.RotatedLogs > retainRotations ? vhost.RotatedLogs - retainRotations : 0;
                _ledger.Vhosts.Replace(
                    domain,
                    current =>
                    {
                        return current with { RotatedLogs = Math.Min(current.RotatedLogs, retainRotations) };
                    });

                return Result<PrunedSiteLogs>.Ok(new PrunedSiteLogs(surplus * LogsPerSite));
            });

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<Result> ReloadWebServerAsync(SiteWebServer webServer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // There is no web server to validate or reload; reloading with nothing pending is a success.
        return Task.FromResult(Result.Ok());
    }

    /// <summary>Whether any vhost already serves <paramref name="domain"/> or an alias.</summary>
    /// <param name="domain">The primary domain being created.</param>
    /// <param name="aliases">The aliases being created.</param>
    /// <returns>True when a name is already taken.</returns>
    private bool IsServedAlready(string domain, IReadOnlyList<string> aliases)
    {
        var wanted = new HashSet<string>(aliases, StringComparer.Ordinal) { domain };

        return _ledger.Vhosts.Snapshot()
            .Any(vhost => { return wanted.Contains(vhost.Domain) || vhost.Aliases.Any(wanted.Contains); });
    }

    /// <summary>Finds the vhost of <paramref name="domain"/> owned by <paramref name="accountUsername"/>.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="vhost">The vhost, when one is held for this account.</param>
    /// <returns>True when found.</returns>
    private bool TryFind(string accountUsername, string domain, [NotNullWhen(true)] out SimulatedVhost? vhost)
    {
        if (_ledger.Vhosts.TryGet(domain, out var found)
            && string.Equals(found.AccountUsername, accountUsername, StringComparison.Ordinal))
        {
            vhost = found;

            return true;
        }

        vhost = null;

        return false;
    }

    /// <summary>Switches a vhost between serving its content and serving the suspension page.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="enabled">The state to put the vhost in.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, or <c>AgentNotFound</c>.</returns>
    private Result SetEnabled(string accountUsername, string domain, bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _ledger.Atomically(
            () =>
            {
                if (!TryFind(accountUsername, domain, out _))
                {
                    return Result.Fail(SimulatedAgentErrors.NotFound());
                }

                _ledger.Vhosts.Replace(domain, current => { return current with { Enabled = enabled }; });

                return Result.Ok();
            });
    }
}
