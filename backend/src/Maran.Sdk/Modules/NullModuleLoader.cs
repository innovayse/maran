using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.Sdk.Resources;

namespace Maran.Sdk.Modules;

/// <summary>
/// The loader the open panel ships: it loads nothing. Every operation that would change what is
/// installed is refused with <c>MarketplaceNotAvailable</c>, and it holds no modules. It is registered
/// only if nothing else registered an <see cref="IModuleLoader"/> first, so a package that provides a
/// real loader replaces it without this one being edited.
/// </summary>
public sealed class NullModuleLoader : IModuleLoader
{
    /// <summary>The one refusal every changing operation answers with.</summary>
    private static readonly Error NotAvailable =
        Error.Of(nameof(ErrorMessages.MarketplaceNotAvailable), ErrorType.Unavailable);

    /// <inheritdoc />
    public Task<Result<ModuleInstallResult>> InstallAsync(IModulePackageSource source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Task.FromResult(Result<ModuleInstallResult>.Fail(NotAvailable));
    }

    /// <inheritdoc />
    public Task<Result<ModuleInstallResult>> UpgradeAsync(IModulePackageSource source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Task.FromResult(Result<ModuleInstallResult>.Fail(NotAvailable));
    }

    /// <inheritdoc />
    public Task<Result> RollbackAsync(string moduleId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        return Task.FromResult(Result.Fail(NotAvailable));
    }

    /// <inheritdoc />
    public Task<Result> UnloadAsync(string moduleId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        return Task.FromResult(Result.Fail(NotAvailable));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<LoadedModuleInfo>> ListAsync(CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<LoadedModuleInfo>>([]);
    }
}
