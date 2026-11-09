using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// A PHP runtime client reporting one installed version with scripted extensions and a scripted
/// change stream, for a test that drives the extension endpoints over real HTTP.
/// </summary>
/// <remarks>
/// A replayed <see cref="PhpExtensionEventKind.Changed"/> event flips the requested extension in the
/// listing, so the handler's read-back after the change sees what the real agent would report. Kept
/// apart from the install-oriented scripted client so neither grows the other's responsibilities.
/// </remarks>
public sealed class ScriptedExtensionsAgentPhpClient : IAgentPhpClient
{
    /// <summary>The extensions the scripted version loads, changed in place by a replayed change.</summary>
    private readonly List<PhpExtensionDto> _extensions;

    /// <summary>The events a change yields, in order.</summary>
    private readonly IReadOnlyList<PhpExtensionEvent> _events;

    /// <summary>Creates the client.</summary>
    /// <param name="extensions">The extensions version 8.3 reports.</param>
    /// <param name="events">The change stream to replay.</param>
    public ScriptedExtensionsAgentPhpClient(IEnumerable<PhpExtensionDto> extensions, IReadOnlyList<PhpExtensionEvent> events)
    {
        _extensions = extensions.ToList();
        _events = events;
    }

    /// <summary>How many times a change was requested.</summary>
    public int ChangeCalls { get; private set; }

    /// <summary>The version, extension and state of the last change requested.</summary>
    public (string Version, string Extension, bool Enabled)? LastChange { get; private set; }

    /// <summary>Answers version 8.3 as the only installed one.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>The installed versions.</returns>
    public Task<Result<IReadOnlyList<PhpVersionDto>>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PhpVersionDto> installed = [new PhpVersionDto("8.3", "/run/php/8.3", AgentPhpDefaultStatus.Default)];

        return Task.FromResult(Result<IReadOnlyList<PhpVersionDto>>.Ok(installed));
    }

    /// <summary>Never answers: these tests do not install.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<IReadOnlyList<InstallablePhpVersionDto>>> ListInstallableVersionsAsync(
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The extension endpoints never ask what can be installed.");
    }

    /// <summary>Never answers: these tests do not install.</summary>
    /// <param name="version">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public IAsyncEnumerable<PhpInstallEvent> InstallVersionAsync(string version, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The extension endpoints never install a PHP version.");
    }

    /// <summary>Answers the scripted extensions.</summary>
    /// <param name="version">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>A copy of the scripted extensions.</returns>
    public Task<Result<IReadOnlyList<PhpExtensionDto>>> ListExtensionsAsync(
        string version,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<IReadOnlyList<PhpExtensionDto>>.Ok(_extensions.ToList()));
    }

    /// <summary>Replays the scripted change stream, applying the change at the terminal changed event.</summary>
    /// <param name="version">The version asked for.</param>
    /// <param name="extension">The extension asked for.</param>
    /// <param name="enabled">The state asked for.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>The scripted events.</returns>
    public async IAsyncEnumerable<PhpExtensionEvent> SetExtensionAsync(
        string version,
        string extension,
        bool enabled,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ChangeCalls++;
        LastChange = (version, extension, enabled);
        foreach (var change in _events)
        {
            if (change.Kind == PhpExtensionEventKind.Changed)
            {
                var index = _extensions.FindIndex(item =>
                {
                    return string.Equals(item.Name, extension, StringComparison.Ordinal);
                });
                if (index >= 0)
                {
                    _extensions[index] = _extensions[index] with { Enabled = enabled };
                }
            }

            yield return change;
            await Task.Yield();
        }
    }

    /// <summary>Never answers: these tests do not apply pool settings.</summary>
    /// <param name="accountUsername">Ignored.</param>
    /// <param name="version">Ignored.</param>
    /// <param name="maxChildren">Ignored.</param>
    /// <param name="settingOverrides">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result> ApplyPoolSettingsAsync(
        string accountUsername,
        string version,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The extension endpoints never apply pool settings.");
    }
}
