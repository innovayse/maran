using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentPhpClient"/> double reporting a fixed set of installed versions, or a typed
/// failure. The two are different answers and the handlers must not conflate them — "the agent
/// could not be asked" is retried, "the version is not installed" is not.
/// </summary>
public sealed class RecordingAgentPhpClient : IAgentPhpClient
{
    /// <summary>The versions this host reports as installed.</summary>
    private readonly IReadOnlyList<PhpVersionDto> _installed;

    /// <summary>The error <see cref="ListVersionsAsync"/> answers with, or null to succeed.</summary>
    private readonly Error? _failure;

    /// <summary>How many times the installed list was asked for.</summary>
    public int ListCalls { get; private set; }

    /// <summary>Creates a client reporting the given versions as installed.</summary>
    /// <param name="versions">Two-component versions, e.g. <c>8.3</c>.</param>
    public RecordingAgentPhpClient(params string[] versions)
    {
        _installed = versions.Select(version =>
        {
            return new PhpVersionDto(version, $"/run/php/{version}", IsDefault: null);
        }).ToList();
    }

    /// <summary>Creates a client that refuses to answer, with <paramref name="failure"/>.</summary>
    /// <param name="failure">The error to answer with.</param>
    public RecordingAgentPhpClient(Error failure)
    {
        _installed = [];
        _failure = failure;
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<PhpVersionDto>>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        ListCalls++;
        return Task.FromResult(_failure is null
            ? Result<IReadOnlyList<PhpVersionDto>>.Ok(_installed)
            : Result<IReadOnlyList<PhpVersionDto>>.Fail(_failure));
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<PhpInstallEvent> InstallVersionAsync(string version, CancellationToken cancellationToken)
    {
        // Installing a PHP version is a host-level operation no Sites module command drives.
        throw new NotSupportedException("PHP installation is not driven by any Sites module operation.");
    }
}
