using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Stands in for the agent's site operations and records the vhost removals the panel asked for, so
/// a test can assert that the HOST half of the account-deletion cascade was actually ordered.
/// </summary>
/// <remarks>
/// <para>
/// Only the agent is replaced, and only because it cannot be present: it is a separate root process
/// holding the nginx include directory and the certificate store. Everything between the command
/// and this boundary is the shipped implementation.
/// </para>
/// <para>
/// It records rather than asserts. The distinction matters for the defect this fixture was written
/// for: the cascade reported COMPLETED while ordering nothing, and a stub that merely returned
/// success would have been equally quiet about it. What is recorded is exactly what the agent would
/// have been told to remove.
/// </para>
/// </remarks>
public sealed class RecordingAgentSitesClient : IAgentSitesClient
{
    /// <summary>Every <c>(account, domain, retired PHP version)</c> the panel asked to remove, in order.</summary>
    public List<(string Account, string Domain, string RetiredPhpVersion)> Deleted { get; } = [];

    /// <summary>When set, every removal is refused with this code, so an abort can be exercised.</summary>
    public string? RefuseWith { get; init; }

    /// <summary>Records the removal the panel ordered, and answers as the agent would.</summary>
    /// <param name="accountUsername">System username of the owning account.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="retiredPhpVersion">The pool version this removal retires, or the empty string.</param>
    /// <param name="cancellationToken">Unused: nothing here awaits.</param>
    /// <returns>Success, or the configured refusal.</returns>
    public Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string domain,
        string retiredPhpVersion,
        CancellationToken cancellationToken)
    {
        Deleted.Add((accountUsername, domain, retiredPhpVersion));

        return Task.FromResult(RefuseWith is null
            ? Result<bool>.Ok(true)
            : Result<bool>.Fail(Error.Of(RefuseWith, ErrorType.Failure)));
    }

    /// <summary>Not exercised by these tests; a call would be a test asking the wrong question.</summary>
    /// <param name="accountUsername">Unused.</param>
    /// <param name="domain">Unused.</param>
    /// <param name="logSource">Unused.</param>
    /// <param name="historyLines">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public IAsyncEnumerable<SiteLogEvent> TailLogAsync(
        string accountUsername,
        string domain,
        SiteLogSource logSource,
        uint historyLines,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    /// <summary>Not exercised by these tests.</summary>
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
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<bool>> ReloadWebServerAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
