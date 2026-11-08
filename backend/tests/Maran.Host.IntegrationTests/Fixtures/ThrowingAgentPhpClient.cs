using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// A PHP runtime client that refuses to answer at all, for a test whose sites are static.
/// </summary>
/// <remarks>
/// It throws rather than returning an empty list because the two say different things. An empty list is
/// an answer — "this host has no PHP" — and a test whose subject is a plan limit must not depend on one;
/// a throw asserts that the code path under test never asks. If a change made a static site probe the
/// host's runtimes, this fails loudly instead of quietly measuring something else.
/// </remarks>
public sealed class ThrowingAgentPhpClient : IAgentPhpClient
{
    /// <summary>Never answers.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<IReadOnlyList<PhpVersionDto>>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("A static site must not ask the host which PHP runtimes it has.");
    }

    /// <summary>Never answers.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<IReadOnlyList<InstallablePhpVersionDto>>> ListInstallableVersionsAsync(
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("A static site must not ask the host which PHP runtimes it can install.");
    }

    /// <summary>Never answers.</summary>
    /// <param name="version">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public IAsyncEnumerable<PhpInstallEvent> InstallVersionAsync(string version, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Creating a site never installs a PHP runtime.");
    }

    /// <summary>Never answers.</summary>
    /// <param name="version">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public Task<Result<IReadOnlyList<PhpExtensionDto>>> ListExtensionsAsync(
        string version,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("A static site must not ask the host which PHP extensions a version has.");
    }

    /// <summary>Never answers.</summary>
    /// <param name="version">Ignored.</param>
    /// <param name="extension">Ignored.</param>
    /// <param name="enabled">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never returns.</returns>
    public IAsyncEnumerable<PhpExtensionEvent> SetExtensionAsync(
        string version,
        string extension,
        bool enabled,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Creating a site never switches a PHP extension.");
    }

    /// <summary>Never answers.</summary>
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
        throw new NotSupportedException("A static site never rewrites a PHP pool.");
    }
}
