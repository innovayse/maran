using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Utilities.Versions;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests.Modules;

/// <summary>
/// A loader for the contract tests: it implements <see cref="IModuleLoader"/> over <see cref="FakePackage"/>
/// and does what the real, closed loader is held to - verify before keeping anything, refuse and audit
/// what fails, leave the previous state untouched on every refusal, ask for a restart rather than
/// activating in place, and report a module the lease no longer covers as degraded without removing it.
/// </summary>
/// <remarks>
/// <para>
/// It exists so the seam can be tested without the closed loader and without the package format: the
/// Host's parts (the audit journal, the registry, the catalogue, the restart request) are driven through
/// the same interfaces the real loader uses. The tests written against it are the contract the real
/// loader's own suite repeats; they are named for the behaviour, not for this class.
/// </para>
/// <para>
/// UNOBSERVED HERE: that a real package's signature, hashes and assemblies are verified - the seal stands
/// in for them - and that a module's code is loaded at all. Nothing here is composed into the host;
/// restart-to-activate means a module joins at the next start, which a test of one host cannot cross.
/// </para>
/// </remarks>
public sealed class FakeModuleLoader : IModuleLoader
{
    /// <summary>The package could not be read at all.</summary>
    public const string Unreadable = "FakePackageUnreadable";

    /// <summary>The package is not sealed by the trusted key, or was edited after it was sealed.</summary>
    public const string SealInvalid = "FakePackageSealInvalid";

    /// <summary>The module is already installed; the caller meant to upgrade it.</summary>
    public const string AlreadyInstalled = "FakePackageAlreadyInstalled";

    /// <summary>The lease does not cover the module.</summary>
    public const string NotEntitled = "FakePackageNotEntitled";

    /// <summary>The module is not installed.</summary>
    public const string NotInstalled = "FakePackageNotInstalled";

    /// <summary>The package is not newer than the installed version.</summary>
    public const string Downgrade = "FakePackageDowngrade";

    /// <summary>There is no earlier version to return to.</summary>
    public const string NothingToRollBack = "FakePackageNothingToRollBack";

    /// <summary>The lease no longer covers the module.</summary>
    public const string LeaseLapsed = "FakeLeaseLapsed";

    /// <summary>Opens the scope the audit journal needs; this type is a singleton and the journal is scoped.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>The lease that decides what may run.</summary>
    private readonly ILicenceLease _lease;

    /// <summary>Where a restart is asked for.</summary>
    private readonly IPanelRestart _restart;

    /// <summary>What the loader holds, by module id.</summary>
    private readonly Dictionary<string, Held> _held = new(StringComparer.Ordinal);

    /// <summary>Ids already reported degraded, so a lapse is audited once and not at every reconcile.</summary>
    private readonly HashSet<string> _degradedReported = new(StringComparer.Ordinal);

    /// <summary>Creates the loader.</summary>
    /// <param name="scopeFactory">Opens the scope the audit journal runs in.</param>
    /// <param name="lease">The lease that decides what may run.</param>
    /// <param name="restart">Where a restart is asked for.</param>
    public FakeModuleLoader(IServiceScopeFactory scopeFactory, ILicenceLease lease, IPanelRestart restart)
    {
        _scopeFactory = scopeFactory;
        _lease = lease;
        _restart = restart;
    }

    /// <inheritdoc />
    public async Task<Result<ModuleInstallResult>> InstallAsync(IModulePackageSource source, CancellationToken ct)
    {
        var judged = await ReadAndVerifyAsync(source, ct);
        if (judged.Failure is not null)
        {
            return judged.Failure;
        }

        var package = judged.Package!;
        if (_held.ContainsKey(package.Id))
        {
            return await RefuseAsync(package, AlreadyInstalled, ErrorType.Conflict, ct);
        }

        if (!_lease.IsEntitled(package.Id))
        {
            return await RefuseAsync(package, NotEntitled, ErrorType.Forbidden, ct);
        }

        _held[package.Id] = new Held(package.Version, null, source.Origin, ModuleLifecycleState.Installed);
        await RecordAsync(MarketplaceAuditActions.ModuleInstalled, package.Id, package.Version, null, true, ct);
        await _restart.RequestAsync(package.Id, ct);

        return Result<ModuleInstallResult>.Ok(
            new ModuleInstallResult(package.Id, package.Version, ModuleLifecycleState.Installed));
    }

    /// <inheritdoc />
    public async Task<Result<ModuleInstallResult>> UpgradeAsync(IModulePackageSource source, CancellationToken ct)
    {
        var judged = await ReadAndVerifyAsync(source, ct);
        if (judged.Failure is not null)
        {
            return judged.Failure;
        }

        var package = judged.Package!;
        if (!_held.TryGetValue(package.Id, out var current))
        {
            return await RefuseAsync(package, NotInstalled, ErrorType.NotFound, ct);
        }

        if (!IsNewer(package.Version, current.Version))
        {
            return await RefuseAsync(package, Downgrade, ErrorType.Conflict, ct);
        }

        _held[package.Id] = new Held(package.Version, current.Version, source.Origin, current.State);
        await RecordAsync(MarketplaceAuditActions.ModuleUpgraded, package.Id, package.Version, null, true, ct);
        await _restart.RequestAsync(package.Id, ct);

        return Result<ModuleInstallResult>.Ok(new ModuleInstallResult(package.Id, package.Version, current.State));
    }

    /// <inheritdoc />
    public async Task<Result> RollbackAsync(string moduleId, CancellationToken ct)
    {
        if (!_held.TryGetValue(moduleId, out var current))
        {
            return await RefuseAsync(moduleId, null, NotInstalled, ErrorType.NotFound, ct);
        }

        if (current.PreviousVersion is null)
        {
            return await RefuseAsync(moduleId, current.Version, NothingToRollBack, ErrorType.Conflict, ct);
        }

        _held[moduleId] = new Held(current.PreviousVersion, null, current.Origin, current.State);
        await RecordAsync(MarketplaceAuditActions.ModuleRolledBack, moduleId, current.PreviousVersion, null, true, ct);
        await _restart.RequestAsync(moduleId, ct);

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<Result> UnloadAsync(string moduleId, CancellationToken ct)
    {
        if (!_held.Remove(moduleId, out var removed))
        {
            return await RefuseAsync(moduleId, null, NotInstalled, ErrorType.NotFound, ct);
        }

        _degradedReported.Remove(moduleId);
        await RecordAsync(MarketplaceAuditActions.ModuleUnloaded, moduleId, removed.Version, null, true, ct);
        await _restart.RequestAsync(moduleId, ct);

        return Result.Ok();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LoadedModuleInfo>> ListAsync(CancellationToken ct)
    {
        IReadOnlyList<LoadedModuleInfo> listed = _held
            .OrderBy(entry => { return entry.Key; }, StringComparer.Ordinal)
            .Select(entry => { return Describe(entry.Key, entry.Value); })
            .ToList();

        return Task.FromResult(listed);
    }

    /// <summary>
    /// Compares the lease with what is held and audits each module it no longer covers, once. A lapse
    /// stops a module serving; it never unloads it, deletes its data or touches any other module.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Resolves once every newly degraded module is recorded.</returns>
    public async Task ReconcileLeaseAsync(CancellationToken ct)
    {
        foreach (var entry in _held.OrderBy(held => { return held.Key; }, StringComparer.Ordinal).ToList())
        {
            if (!_lease.IsEntitled(entry.Key) && _degradedReported.Add(entry.Key))
            {
                await RecordAsync(
                    MarketplaceAuditActions.ModuleDegraded,
                    entry.Key,
                    entry.Value.Version,
                    Error.Of(LeaseLapsed, ErrorType.Forbidden),
                    false,
                    ct);
            }
        }
    }

    /// <summary>Whether a candidate version is strictly newer than the installed one.</summary>
    /// <param name="candidate">The offered version.</param>
    /// <param name="installed">The installed version.</param>
    /// <returns>True when the candidate parses, the installed one parses, and the candidate is newer.</returns>
    private static bool IsNewer(string candidate, string installed)
    {
        return SemanticVersion.TryParse(candidate, out var offered)
            && SemanticVersion.TryParse(installed, out var running)
            && SemanticVersion.Compare(offered!, running!) > 0;
    }

    /// <summary>Builds the loader's report for one held module.</summary>
    /// <param name="id">The module id.</param>
    /// <param name="held">What is held.</param>
    /// <returns>The report, degraded when the lease no longer covers the module.</returns>
    private LoadedModuleInfo Describe(string id, Held held)
    {
        return _lease.IsEntitled(id)
            ? new LoadedModuleInfo(id, held.Version, held.State, held.Origin, null)
            : new LoadedModuleInfo(
                id,
                held.Version,
                ModuleLifecycleState.Degraded,
                held.Origin,
                Error.Of(LeaseLapsed, ErrorType.Forbidden));
    }

    /// <summary>Reads a package and verifies its seal, refusing and auditing it when either fails.</summary>
    /// <param name="source">Where the package comes from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The verified package, or the refusal already recorded.</returns>
    private async Task<(FakePackage? Package, Result<ModuleInstallResult>? Failure)> ReadAndVerifyAsync(
        IModulePackageSource source,
        CancellationToken ct)
    {
        await using var stream = await source.OpenAsync(ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);

        var package = FakePackage.TryRead(buffer.ToArray());
        if (package is null)
        {
            return (null, await RefuseAsync(null, Unreadable, ErrorType.Validation, ct));
        }

        return package.IsSealedByTrustedKey()
            ? (package, null)
            : (null, await RefuseAsync(package, SealInvalid, ErrorType.Validation, ct));
    }

    /// <summary>Audits a refusal of a package and builds the failure.</summary>
    /// <param name="package">The package, or null when it could not be read.</param>
    /// <param name="code">The refusal's code.</param>
    /// <param name="type">The kind of failure.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The failure.</returns>
    private async Task<Result<ModuleInstallResult>> RefuseAsync(
        FakePackage? package,
        string code,
        ErrorType type,
        CancellationToken ct)
    {
        var error = Error.Of(code, type);
        await RecordAsync(MarketplaceAuditActions.ModuleRefused, package?.Id, package?.Version, error, false, ct);

        return Result<ModuleInstallResult>.Fail(error);
    }

    /// <summary>Audits a refusal of an operation on an installed module and builds the failure.</summary>
    /// <param name="moduleId">The module's id.</param>
    /// <param name="version">The module's version, when known.</param>
    /// <param name="code">The refusal's code.</param>
    /// <param name="type">The kind of failure.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The failure.</returns>
    private async Task<Result> RefuseAsync(
        string moduleId,
        string? version,
        string code,
        ErrorType type,
        CancellationToken ct)
    {
        var error = Error.Of(code, type);
        await RecordAsync(MarketplaceAuditActions.ModuleRefused, moduleId, version, error, false, ct);

        return Result.Fail(error);
    }

    /// <summary>Writes one entry through the Host's audit journal, in a scope of its own.</summary>
    /// <param name="action">The action.</param>
    /// <param name="moduleId">The module's id, when known.</param>
    /// <param name="version">The module's version, when known.</param>
    /// <param name="reason">Why it failed, if it did.</param>
    /// <param name="succeeded">Whether it took effect.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    private async Task RecordAsync(
        string action,
        string? moduleId,
        string? version,
        Error? reason,
        bool succeeded,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IModuleLoaderAudit>()
            .RecordAsync(action, moduleId, version, reason, succeeded, ct);
    }

    /// <summary>What the loader holds for one module.</summary>
    /// <param name="Version">The version that runs after the next start.</param>
    /// <param name="PreviousVersion">The version before the last upgrade, or null when there was none.</param>
    /// <param name="Origin">Where the package came from.</param>
    /// <param name="State">The state the module is in, before the lease is consulted.</param>
    private sealed record Held(string Version, string? PreviousVersion, string Origin, ModuleLifecycleState State);
}
