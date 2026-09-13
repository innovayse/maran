using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentSitesClient"/> double that records what it was asked to do and answers
/// however the test needs.
///
/// The handlers' whole subject is the ORDER of two effects — the agent first, the row second — so a
/// test has to be able to say "the agent refused" and then assert the row did not move. It also
/// keeps the <see cref="SiteDescriptor"/> it was handed, because a descriptor fabricated at a call
/// site rather than read from the stored row is a real defect this project has already seen.
/// </summary>
public sealed class RecordingAgentSitesClient : IAgentSitesClient
{
    /// <summary>The error every call answers with, or null to succeed.</summary>
    private readonly Error? _failure;

    /// <summary>Operations this client was asked to perform, in order.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The descriptors handed to the operations that re-render a vhost, in order.</summary>
    public List<SiteDescriptor> Descriptors { get; } = [];

    /// <summary>The worker budgets handed to <see cref="ChangePhpVersionAsync"/>, in order.</summary>
    public List<uint> MaxChildren { get; } = [];

    /// <summary>The cancellation tokens the handlers forwarded, in order.</summary>
    /// <remarks>
    /// A handler that passes <c>CancellationToken.None</c> instead of the request own token turns
    /// an abandoned HTTP request into work the agent keeps doing. The forwarding is invisible at
    /// every call site, so it is recorded here rather than trusted.
    /// </remarks>
    public List<CancellationToken> Tokens { get; } = [];

    /// <summary>Creates a client that succeeds at everything.</summary>
    public RecordingAgentSitesClient()
    {
    }

    /// <summary>Creates a client that refuses every call with <paramref name="failure"/>.</summary>
    /// <param name="failure">The error to answer with.</param>
    public RecordingAgentSitesClient(Error failure)
    {
        _failure = failure;
    }

    /// <inheritdoc/>
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
        Calls.Add($"create:{accountUsername}:{domain}:{kind}:{phpVersion}:{string.Join('|', aliases)}:{maxChildren}");
        Tokens.Add(cancellationToken);
        return Task.FromResult(_failure is null
            ? Result<CreatedSiteDto>.Ok(new CreatedSiteDto($"/home/{accountUsername}/sites/{domain}"))
            : Result<CreatedSiteDto>.Fail(_failure));
    }

    /// <inheritdoc/>
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
        Calls.Add($"change-php:{accountUsername}:{domain}:{phpVersion}:{removePreviousPool}");
        Tokens.Add(cancellationToken);
        Descriptors.Add(site);
        MaxChildren.Add(maxChildren);
        return Task.FromResult(Answer());
    }

    /// <inheritdoc/>
    public Task<Result<bool>> EnableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        Calls.Add($"enable:{accountUsername}:{domain}");
        Tokens.Add(cancellationToken);
        Descriptors.Add(site);
        return Task.FromResult(Answer());
    }

    /// <inheritdoc/>
    public Task<Result<bool>> DisableAsync(
        string accountUsername,
        string domain,
        SiteDescriptor site,
        CancellationToken cancellationToken)
    {
        Calls.Add($"disable:{accountUsername}:{domain}");
        Tokens.Add(cancellationToken);
        Descriptors.Add(site);
        return Task.FromResult(Answer());
    }

    /// <inheritdoc/>
    public Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string domain,
        string retiredPhpVersion,
        CancellationToken cancellationToken)
    {
        Calls.Add($"delete:{accountUsername}:{domain}:{retiredPhpVersion}");
        Tokens.Add(cancellationToken);
        return Task.FromResult(Answer());
    }

    /// <summary>The events a tail yields, in order. Empty means a stream that ends saying nothing.</summary>
    public IReadOnlyList<SiteLogEvent> LogEvents { get; init; } = [];

    /// <inheritdoc/>
    public async IAsyncEnumerable<SiteLogEvent> TailLogAsync(
        string accountUsername,
        string domain,
        SiteLogSource logSource,
        uint historyLines,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Calls.Add($"tail:{accountUsername}:{domain}:{logSource}:{historyLines}");
        Tokens.Add(cancellationToken);

        foreach (var scripted in LogEvents)
        {
            // Checked per event rather than only at the start: a test that cancels mid-stream is
            // testing what the service does when the caller walks away, and a double that ignored
            // the token would answer that question for it.
            cancellationToken.ThrowIfCancellationRequested();
            yield return scripted;
        }

        await Task.CompletedTask;
    }

    /// <summary>How many times the batch web-server reload was asked for.</summary>
    public int ReloadCallCount { get; private set; }

    /// <inheritdoc/>
    public Task<Result<bool>> ReloadWebServerAsync(CancellationToken cancellationToken)
    {
        ReloadCallCount++;
        return Task.FromResult(Answer());
    }

    /// <summary>The configured answer for a call returning nothing but success.</summary>
    private Result<bool> Answer()
    {
        return _failure is null ? Result<bool>.Ok(true) : Result<bool>.Fail(_failure);
    }
}
