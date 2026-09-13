using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// A sites agent double that holds every creation inside the agent call until a stated number of
/// callers have arrived there, so a test can put two panel requests in the same instant on purpose.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists because a concurrency test that cannot LOSE is not a test.</b> The window the panel
/// has to close is the one between a request reading the account's site count and the row for that site
/// being committed, and the agent call sits inside it: two requests that pass the count check and then
/// meet here are exactly two requests that both believed they had room. Without the barrier the two
/// tasks would almost always be serialised by chance — the first would commit before the second read —
/// and the test would pass over code with no protection at all.
/// </para>
/// <para>
/// It counts creations and deletions because the losing request must be observed to have UNDONE the
/// vhost it provisioned: the count is how a test distinguishes "refused" from "refused and left an
/// orphan behind". It also records the PHP version each delete was given, because a compensating
/// delete must retire NO pool — the account's other sites on that version share it.
/// </para>
/// <para>
/// The calls this interface has that a site creation never makes throw rather than answering. A double
/// that answered them would be a stub for a surface this test does not exercise, and a reader could not
/// tell which of its answers were load-bearing.
/// </para>
/// </remarks>
public sealed class BarrierSitesAgent : IAgentSitesClient
{
    /// <summary>Released once <see cref="_expected"/> callers are waiting inside the agent call.</summary>
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The PHP version each delete was asked to retire, in call order.</summary>
    private readonly List<string> _retiredPhpVersions = [];

    /// <summary>How many callers must arrive before any of them is allowed to continue.</summary>
    private readonly int _expected;

    /// <summary>How many callers have arrived so far.</summary>
    private int _arrived;

    /// <summary>How many sites this double has been asked to provision.</summary>
    private int _createCalls;

    /// <summary>Creates the double.</summary>
    /// <param name="expectedCallers">
    /// How many creations must be in flight before any of them proceeds. One makes the double
    /// transparent, which is what the inverse control needs.
    /// </param>
    public BarrierSitesAgent(int expectedCallers)
    {
        _expected = expectedCallers;
    }

    /// <summary>How many sites were provisioned on the "host".</summary>
    public int CreateCalls
    {
        get
        {
            return Volatile.Read(ref _createCalls);
        }
    }

    /// <summary>The PHP version each delete was given, which a compensating delete must leave empty.</summary>
    public IReadOnlyList<string> RetiredPhpVersions
    {
        get
        {
            lock (_retiredPhpVersions)
            {
                return [.. _retiredPhpVersions];
            }
        }
    }

    /// <summary>Provisions the site, but not before every expected caller has reached this point.</summary>
    /// <param name="accountUsername">The owning account's system user name.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="aliases">The site's aliases; ignored.</param>
    /// <param name="kind">The backend kind; ignored.</param>
    /// <param name="phpVersion">The PHP version; ignored.</param>
    /// <param name="proxyUpstream">The proxy upstream; ignored.</param>
    /// <param name="maxChildren">The pool's worker budget; ignored.</param>
    /// <param name="settingOverrides">The php.ini overrides; ignored.</param>
    /// <param name="cancellationToken">Cancellation for the wait and the call.</param>
    /// <returns>The document root, as the real agent reports it.</returns>
    public async Task<Result<CreatedSiteDto>> CreateAsync(
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
        Interlocked.Increment(ref _createCalls);

        if (Interlocked.Increment(ref _arrived) >= _expected)
        {
            _released.TrySetResult();
        }

        await _released.Task.WaitAsync(cancellationToken);

        return Result<CreatedSiteDto>.Ok(new CreatedSiteDto($"/home/{accountUsername}/web/{domain}/public"));
    }

    /// <summary>Removes the vhost, recording what the caller asked to retire with it.</summary>
    /// <param name="accountUsername">The owning account's system user name; ignored.</param>
    /// <param name="domain">The site's primary domain; ignored.</param>
    /// <param name="retiredPhpVersion">The pool version to retire, which a compensation leaves empty.</param>
    /// <param name="cancellationToken">Cancellation for the call; ignored.</param>
    /// <returns>Success.</returns>
    public Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string domain,
        string retiredPhpVersion,
        CancellationToken cancellationToken)
    {
        lock (_retiredPhpVersions)
        {
            _retiredPhpVersions.Add(retiredPhpVersion);
        }

        return Task.FromResult(Result<bool>.Ok(true));
    }

    /// <summary>Not exercised by a site creation.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="domain">Ignored.</param>
    /// <param name="phpVersion">Ignored.</param>
    /// <param name="site">Ignored.</param>
    /// <param name="maxChildren">Ignored.</param>
    /// <param name="settingOverrides">Ignored.</param>
    /// <param name="removePreviousPool">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
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
        throw new NotSupportedException("This double answers site creation and deletion only.");
    }

    /// <summary>Not exercised by a site creation.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="domain">Ignored.</param>
    /// <param name="site">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<bool>> EnableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers site creation and deletion only.");
    }

    /// <summary>Not exercised by a site creation.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="domain">Ignored.</param>
    /// <param name="site">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<bool>> DisableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers site creation and deletion only.");
    }

    /// <summary>Not exercised by a site creation.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="domain">Ignored.</param>
    /// <param name="logSource">Ignored.</param>
    /// <param name="historyLines">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public IAsyncEnumerable<SiteLogEvent> TailLogAsync(
        string accountUsername,
        string domain,
        SiteLogSource logSource,
        uint historyLines,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers site creation and deletion only.");
    }

    /// <summary>Not exercised by a site creation.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<bool>> ReloadWebServerAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("This double answers site creation and deletion only.");
    }
}
