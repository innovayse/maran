using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Host.Tests.Fixtures;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.Resilience;

/// <summary>An inner PHP client that records its arguments and can fail on demand.</summary>
internal sealed class RecordingAgentPhpClient : IAgentPhpClient
{
    /// <summary>How many calls fail with a transport error before one succeeds.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>How many times the listing method was entered.</summary>
    public int Calls { get; private set; }

    /// <summary>The version of the last install request.</summary>
    public string? LastVersion { get; private set; }

    /// <summary>The version of the last extension listing request.</summary>
    public string? LastExtensionsVersion { get; private set; }

    /// <summary>The extension of the last switch request.</summary>
    public string? LastSwitchedExtension { get; private set; }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<PhpVersionDto>>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        Calls++;

        if (Calls <= FailuresBeforeSuccess)
        {
            throw AgentTransportFaults.ConnectRefused();
        }

        await Task.Yield();

        return Result<IReadOnlyList<PhpVersionDto>>.Ok([new PhpVersionDto("8.3", "/run/php", AgentPhpDefaultStatus.Unknown)]);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<InstallablePhpVersionDto>>> ListInstallableVersionsAsync(
        CancellationToken cancellationToken)
    {
        Calls++;

        if (Calls <= FailuresBeforeSuccess)
        {
            throw AgentTransportFaults.ConnectRefused();
        }

        await Task.Yield();

        return Result<IReadOnlyList<InstallablePhpVersionDto>>.Ok([new InstallablePhpVersionDto("8.3", true)]);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<PhpInstallEvent> InstallVersionAsync(
        string version,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastVersion = version;
        await Task.Yield();

        yield return new PhpInstallEvent(PhpInstallEventKind.Installed, 100, string.Empty, version, null);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<PhpExtensionDto>>> ListExtensionsAsync(
        string version,
        CancellationToken cancellationToken)
    {
        Calls++;
        LastExtensionsVersion = version;

        if (Calls <= FailuresBeforeSuccess)
        {
            throw AgentTransportFaults.ConnectRefused();
        }

        await Task.Yield();

        return Result<IReadOnlyList<PhpExtensionDto>>.Ok(
            [new PhpExtensionDto("gd", true, PhpExtensionAvailability.Available)]);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<PhpExtensionEvent> SetExtensionAsync(
        string version,
        string extension,
        bool enabled,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastSwitchedExtension = extension;
        await Task.Yield();

        yield return new PhpExtensionEvent(PhpExtensionEventKind.Changed, 100, string.Empty, version, extension, enabled, null);
    }

    /// <inheritdoc/>
    public async Task<Result> ApplyPoolSettingsAsync(
        string accountUsername,
        string version,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        CancellationToken cancellationToken)
    {
        Calls++;

        if (Calls <= FailuresBeforeSuccess)
        {
            throw AgentTransportFaults.ConnectRefused();
        }

        await Task.Yield();

        return Result.Ok();
    }
}
