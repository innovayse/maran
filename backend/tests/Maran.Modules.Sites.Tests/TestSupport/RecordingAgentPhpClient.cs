using System.Runtime.CompilerServices;
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
    private readonly List<PhpVersionDto> _installed;

    /// <summary>The events this fake streams for an install, in order.</summary>
    private readonly List<PhpInstallEvent> _installEvents = [];

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

    /// <summary>The versions an install was asked for, in order.</summary>
    public List<string> InstallCalls { get; } = [];

    /// <summary>
    /// Stages the events one install will stream, and whether the version then appears installed.
    /// </summary>
    /// <param name="events">The events to stream, in order.</param>
    /// <param name="thenInstalled">
    /// The version to add to the list a later read answers, or <see langword="null"/> to leave the
    /// list alone. It is separate from the events on purpose: a fake that made the version appear
    /// because the stream SAID so could not stage the case this code most needs — a stream that
    /// claims success over packages that are not there.
    /// </param>
    public void StageInstall(IEnumerable<PhpInstallEvent> events, string? thenInstalled = null)
    {
        _installEvents.Clear();
        _installEvents.AddRange(events);
        _appearsAfterInstall = thenInstalled;
    }

    /// <summary>The version the staged install adds to the installed list, if any.</summary>
    private string? _appearsAfterInstall;

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
    public async IAsyncEnumerable<PhpInstallEvent> InstallVersionAsync(
        string version,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        InstallCalls.Add(version);

        foreach (var change in _installEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The version becomes installed AT the event that says so, not after the loop: a
            // consumer stops enumerating on a terminal event, which disposes this iterator without
            // running anything written past the loop. A fake that appended there would report the
            // version as absent to every caller that behaves the way the real one does.
            if (change.Kind == PhpInstallEventKind.Installed && _appearsAfterInstall is not null)
            {
                _installed.Add(
                    new PhpVersionDto(_appearsAfterInstall, $"/run/php/{_appearsAfterInstall}", IsDefault: null));
            }

            yield return change;
        }

        await Task.CompletedTask;
    }
}
