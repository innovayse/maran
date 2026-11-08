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
            return new PhpVersionDto(version, $"/run/php/{version}", AgentPhpDefaultStatus.Unknown);
        }).ToList();
    }

    /// <summary>The extensions this fake reports for any version; empty unless a test sets them.</summary>
    public List<PhpExtensionDto> Extensions { get; } = [];

    /// <summary>The (version, extension, enabled) triples a switch was asked for, in order.</summary>
    public List<(string Version, string Extension, bool Enabled)> SetExtensionCalls { get; } = [];

    /// <summary>The events this fake streams for a switch, in order; a Changed event answers when none is staged.</summary>
    public List<PhpExtensionEvent> ExtensionEvents { get; } = [];

    /// <summary>
    /// Whether a Changed event also flips the extension in <see cref="Extensions"/>, as the real host
    /// does. A test sets it to false to stage a stream that claims success over a state that did not change.
    /// </summary>
    public bool AppliesExtensionChange { get; set; } = true;

    /// <summary>The versions an install was asked for, in order.</summary>
    public List<string> InstallCalls { get; } = [];

    /// <summary>The pool rewrites asked for, in order: account, version, worker budget and the settings sent.</summary>
    public List<(string Username, string Version, uint MaxChildren, IReadOnlyList<PhpSettingDto> Settings)> PoolCalls { get; } = [];

    /// <summary>The error a pool rewrite at a version answers with; a version not listed succeeds.</summary>
    public Dictionary<string, Error> PoolFailures { get; } = [];

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

    /// <summary>The range the agent's repository offers unless a test narrows it, newest first.</summary>
    private static readonly string[] SupportedRange = ["8.4", "8.3", "8.2", "8.1", "8.0", "7.4"];

    /// <summary>The narrowed range set by <see cref="OfferOnly"/>, or null for the full range.</summary>
    private string[]? _installableOverride;

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

    /// <summary>
    /// Answers the full supported range, each version flagged by whether it is in the installed list
    /// at the moment of the call, so an install that appears in the list is reflected here too.
    /// </summary>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>The installable versions, newest first, or the configured failure.</returns>
    public Task<Result<IReadOnlyList<InstallablePhpVersionDto>>> ListInstallableVersionsAsync(
        CancellationToken cancellationToken)
    {
        if (_failure is not null)
        {
            return Task.FromResult(Result<IReadOnlyList<InstallablePhpVersionDto>>.Fail(_failure));
        }

        IReadOnlyList<InstallablePhpVersionDto> offered = (_installableOverride ?? SupportedRange)
            .Select(version =>
            {
                return new InstallablePhpVersionDto(
                    version,
                    _installed.Any(item =>
                    {
                        return string.Equals(item.Version, version, StringComparison.Ordinal);
                    }));
            })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<InstallablePhpVersionDto>>.Ok(offered));
    }

    /// <summary>Narrows the installable range this fake offers, newest first.</summary>
    /// <param name="versions">The versions to offer; any other version is not installable here.</param>
    public void OfferOnly(params string[] versions)
    {
        _installableOverride = versions;
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
                    new PhpVersionDto(_appearsAfterInstall, $"/run/php/{_appearsAfterInstall}", AgentPhpDefaultStatus.Unknown));
            }

            yield return change;
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<PhpExtensionDto>>> ListExtensionsAsync(
        string version,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_failure is null
            ? Result<IReadOnlyList<PhpExtensionDto>>.Ok(Extensions.ToList())
            : Result<IReadOnlyList<PhpExtensionDto>>.Fail(_failure));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<PhpExtensionEvent> SetExtensionAsync(
        string version,
        string extension,
        bool enabled,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        SetExtensionCalls.Add((version, extension, enabled));

        if (ExtensionEvents.Count == 0)
        {
            ApplyExtensionChange(extension, enabled);

            yield return new PhpExtensionEvent(
                PhpExtensionEventKind.Changed,
                100,
                string.Empty,
                version,
                extension,
                enabled,
                null);
        }

        foreach (var change in ExtensionEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The state changes AT the event that says so, for the reason the install stream's version
            // does: a consumer stops enumerating on a terminal event.
            if (change.Kind == PhpExtensionEventKind.Changed)
            {
                ApplyExtensionChange(extension, enabled);
            }

            yield return change;
        }

        await Task.CompletedTask;
    }

    /// <summary>Puts the named extension in the requested state in <see cref="Extensions"/>, when changes are applied.</summary>
    /// <param name="extension">The catalogue name that was switched.</param>
    /// <param name="enabled">The state it was switched to.</param>
    private void ApplyExtensionChange(string extension, bool enabled)
    {
        if (!AppliesExtensionChange)
        {
            return;
        }

        var index = Extensions.FindIndex(item =>
        {
            return string.Equals(item.Name, extension, StringComparison.Ordinal);
        });
        if (index >= 0)
        {
            Extensions[index] = Extensions[index] with { Enabled = enabled };
        }
    }

    /// <inheritdoc/>
    public Task<Result> ApplyPoolSettingsAsync(
        string accountUsername,
        string version,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        CancellationToken cancellationToken)
    {
        PoolCalls.Add((accountUsername, version, maxChildren, settingOverrides));

        return Task.FromResult(PoolFailures.TryGetValue(version, out var failure) ? Result.Fail(failure) : Result.Ok());
    }
}
