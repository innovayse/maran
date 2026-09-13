using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Stands in for the agent while the panel's own log-streaming path is exercised end to end: the
/// real controller, the real tenant-scoped resolution, the real frame mapping and the real
/// server-sent-event writer, over real HTTP and real PostgreSQL.
/// </summary>
/// <remarks>
/// Only the agent is replaced, and only because it cannot be present: it is a separate root process
/// that reads files on a provisioned host. Everything between the HTTP request and this boundary is
/// the shipped implementation, so a test that passes here is not merely a test that a call was made
/// (rules/testing.md).
///
/// It is deliberately dumb — it replays a script and records what it was asked. It asserts nothing
/// itself; the tests do.
/// </remarks>
public sealed class StubAgentSitesClient : IAgentSitesClient
{
    /// <summary>The events this stub yields, in order, before its stream ends.</summary>
    public IReadOnlyList<SiteLogEvent> Events { get; init; } = [];

    /// <summary>
    /// When true, the stream does not end after <see cref="Events"/> — it waits for the caller's
    /// cancellation, the way a real tail on a quiet log does. That is what makes an abandoned stream
    /// observable: a stub that ended on its own would end whether or not anything stopped it.
    /// </summary>
    public bool WaitsForCancellation { get; init; }

    /// <summary>The account name the panel addressed the tail with, or <c>null</c> if it was never called.</summary>
    public string? RequestedAccountUsername { get; private set; }

    /// <summary>The domain the panel addressed the tail with.</summary>
    public string? RequestedDomain { get; private set; }

    /// <summary>The log the panel asked for.</summary>
    public SiteLogSource? RequestedSource { get; private set; }

    /// <summary>The history count the panel asked for.</summary>
    public uint? RequestedHistoryLines { get; private set; }

    /// <summary>Set once the tail stopped because the caller cancelled it, rather than of its own accord.</summary>
    public bool StoppedByCaller { get; private set; }

    /// <summary>Replays the scripted events, then ends or waits as configured.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="domain">Primary domain of the site whose log is read.</param>
    /// <param name="logSource">Which log to tail.</param>
    /// <param name="historyLines">How many historical lines to replay.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>The scripted events.</returns>
    public async IAsyncEnumerable<SiteLogEvent> TailLogAsync(
        string accountUsername,
        string domain,
        SiteLogSource logSource,
        uint historyLines,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        RequestedAccountUsername = accountUsername;
        RequestedDomain = domain;
        RequestedSource = logSource;
        RequestedHistoryLines = historyLines;

        foreach (var scripted in Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return scripted;
        }

        if (!WaitsForCancellation)
        {
            yield break;
        }

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        finally
        {
            StoppedByCaller = cancellationToken.IsCancellationRequested;
        }
    }

    /// <summary>Not exercised by these tests; a call would be a test asking the wrong question.</summary>
    /// <param name="accountUsername">Unused.</param>
    /// <param name="domain">Unused.</param>
    /// <param name="aliases">Unused.</param>
    /// <param name="kind">Unused.</param>
    /// <param name="phpVersion">Unused.</param>
    /// <param name="proxyUpstream">Unused.</param>
    /// <param name="maxChildren">Unused.</param>
    /// <param name="settingOverrides">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<CreatedSiteDto>> CreateAsync(
        string accountUsername,
        string domain,
        IReadOnlyList<string> aliases,
        SiteBackendKind kind,
        string phpVersion,
        string proxyUpstream,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not exercised by these tests.</summary>
    /// <param name="accountUsername">Unused.</param>
    /// <param name="domain">Unused.</param>
    /// <param name="phpVersion">Unused.</param>
    /// <param name="site">Unused.</param>
    /// <param name="maxChildren">Unused.</param>
    /// <param name="settingOverrides">Unused.</param>
    /// <param name="removePreviousPool">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<bool>> ChangePhpVersionAsync(
        string accountUsername,
        string domain,
        string phpVersion,
        SiteDescriptor site,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        bool removePreviousPool,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not exercised by these tests.</summary>
    /// <param name="accountUsername">Unused.</param>
    /// <param name="domain">Unused.</param>
    /// <param name="site">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<bool>> EnableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not exercised by these tests.</summary>
    /// <param name="accountUsername">Unused.</param>
    /// <param name="domain">Unused.</param>
    /// <param name="site">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<bool>> DisableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not exercised by these tests.</summary>
    /// <param name="accountUsername">Unused.</param>
    /// <param name="domain">Unused.</param>
    /// <param name="retiredPhpVersion">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string domain,
        string retiredPhpVersion,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not exercised by these tests.</summary>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<bool>> ReloadWebServerAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
