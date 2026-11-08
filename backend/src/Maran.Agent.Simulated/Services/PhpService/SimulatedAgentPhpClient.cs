using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.State;
using Maran.Agent.Simulated.Validation;

namespace Maran.Agent.Simulated.Services.PhpService;

/// <summary>
/// The host's PHP runtimes, kept in memory: an install is a canned progress sequence that ends with a
/// version appearing in the ledger, and nothing is downloaded, unpacked or reloaded.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent like the real agent. Every stream ends in exactly one terminal event, and a caller that
/// stops watching is told so with a <c>Cancelled</c> event rather than a silent end; the streams
/// themselves are <see cref="SimulatedPhpInstallEvents"/> and <see cref="SimulatedPhpExtensionEvents"/>.
/// </para>
/// <para>
/// An account's pool at a version exists exactly while one of its PHP sites is bound to that version,
/// because that is the only thing in the ledger that would have created it; applying settings to a
/// pool that does not exist is <c>AgentNotFound</c>, as on a real host.
/// </para>
/// </remarks>
public sealed class SimulatedAgentPhpClient : IAgentPhpClient
{
    /// <summary>The simulated host's state.</summary>
    private readonly SimulatedAgentLedger _ledger;

    /// <summary>The source of install events.</summary>
    private readonly SimulatedPhpInstallEvents _installs;

    /// <summary>The source of extension-switch events.</summary>
    private readonly SimulatedPhpExtensionEvents _extensions;

    /// <summary>Creates a client over <paramref name="ledger"/>.</summary>
    /// <param name="ledger">The simulated host's state.</param>
    public SimulatedAgentPhpClient(SimulatedAgentLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        _ledger = ledger;
        _installs = new SimulatedPhpInstallEvents(ledger);
        _extensions = new SimulatedPhpExtensionEvents(ledger);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<PhpVersionDto>>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var runtimes = _ledger.PhpRuntimes.Snapshot().ToDictionary(
            runtime => { return runtime.Version; },
            StringComparer.Ordinal);
        IReadOnlyList<PhpVersionDto> versions = SimulatedPhpCatalog.NewestFirst(runtimes.Keys)
            .Select(
                version =>
                {
                    var runtime = runtimes[version];

                    return new PhpVersionDto(
                        runtime.Version,
                        runtime.FpmSocketDirectory,
                        runtime.IsDefault ? AgentPhpDefaultStatus.Default : AgentPhpDefaultStatus.NotDefault);
                })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<PhpVersionDto>>.Ok(versions));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<InstallablePhpVersionDto>>> ListInstallableVersionsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<InstallablePhpVersionDto> versions = SimulatedPhpCatalog.Installable
            .Select(
                version => { return new InstallablePhpVersionDto(version, _ledger.PhpRuntimes.Contains(version)); })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<InstallablePhpVersionDto>>.Ok(versions));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<PhpInstallEvent> InstallVersionAsync(string version, CancellationToken cancellationToken)
    {
        return _installs.EventsAsync(version, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<PhpExtensionDto>>> ListExtensionsAsync(
        string version,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!SimulatedInput.IsPhpVersion(version))
        {
            return Task.FromResult(Result<IReadOnlyList<PhpExtensionDto>>.Fail(SimulatedAgentErrors.InvalidInput()));
        }

        if (!_ledger.PhpRuntimes.TryGet(version, out var runtime))
        {
            return Task.FromResult(
                Result<IReadOnlyList<PhpExtensionDto>>.Fail(SimulatedAgentErrors.ValidationFailed()));
        }

        IReadOnlyList<PhpExtensionDto> extensions = SimulatedPhpCatalog.Extensions
            .Select(
                name =>
                {
                    return new PhpExtensionDto(
                        name,
                        runtime.EnabledExtensions.Contains(name),
                        PhpExtensionAvailability.Available);
                })
            .ToList();

        return Task.FromResult(Result<IReadOnlyList<PhpExtensionDto>>.Ok(extensions));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<PhpExtensionEvent> SetExtensionAsync(
        string version,
        string extension,
        bool enabled,
        CancellationToken cancellationToken)
    {
        return _extensions.EventsAsync(version, extension, enabled, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result> ApplyPoolSettingsAsync(
        string accountUsername,
        string version,
        uint maxChildren,
        IReadOnlyList<PhpSettingDto> settingOverrides,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(settingOverrides);

        var wellFormed = SimulatedInput.IsAccountName(accountUsername, _ledger.Limits.MaxAccountNameLength)
            && SimulatedInput.IsPhpVersion(version)
            && SimulatedPhpSettingRules.AreWellFormed(_ledger.Limits, settingOverrides);
        if (!wellFormed)
        {
            return Task.FromResult(Result.Fail(SimulatedAgentErrors.InvalidInput()));
        }

        var result = _ledger.Atomically(
            () =>
            {
                var domains = _ledger.Vhosts.Snapshot()
                    .Where(vhost => { return IsPoolMember(vhost, accountUsername, version); })
                    .Select(vhost => { return vhost.Domain; })
                    .ToList();
                if (domains.Count == 0)
                {
                    return Result.Fail(SimulatedAgentErrors.NotFound());
                }

                foreach (var domain in domains)
                {
                    _ledger.Vhosts.Replace(
                        domain,
                        vhost =>
                        {
                            return vhost with
                            {
                                MaxChildren = maxChildren,
                                SettingOverrides = settingOverrides.ToList(),
                            };
                        });
                }

                return Result.Ok();
            });

        return Task.FromResult(result);
    }

    /// <summary>Whether <paramref name="vhost"/> is a PHP site of the account at the version.</summary>
    /// <param name="vhost">The vhost to judge.</param>
    /// <param name="accountUsername">The account's system user name.</param>
    /// <param name="version">The PHP version.</param>
    /// <returns>True when the vhost belongs to that account's pool at that version.</returns>
    private static bool IsPoolMember(SimulatedVhost vhost, string accountUsername, string version)
    {
        return vhost.Backend == SiteBackendKind.Php
            && string.Equals(vhost.AccountUsername, accountUsername, StringComparison.Ordinal)
            && string.Equals(vhost.PhpVersion, version, StringComparison.Ordinal);
    }
}
