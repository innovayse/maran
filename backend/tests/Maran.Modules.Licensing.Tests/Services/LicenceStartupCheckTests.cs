using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers <see cref="LicenceStartupCheck.CheckOnceAsync"/>'s own guard — the mechanism that holds
/// spec §229 at the Host boundary for this module (see the type's own remarks): whatever breaks
/// inside it, the method returns normally rather than faulting the caller.
/// </summary>
public sealed class LicenceStartupCheckTests
{
    /// <summary>A raw-text source that always throws, standing in for a future storage failure.</summary>
    private sealed class ThrowingRawTextSource : ILicenceRawTextSource
    {
        public string? ReadRawLicenceText()
        {
            throw new InvalidOperationException("Simulated failure reading the installed licence.");
        }
    }

    /// <summary>Builds a <see cref="LicenceStartupCheck"/> whose scope resolves the given source.</summary>
    /// <param name="rawTextSource">The <see cref="ILicenceRawTextSource"/> the check will resolve.</param>
    /// <returns>The check, ready to drive directly via <see cref="LicenceStartupCheck.CheckOnceAsync"/>.</returns>
    private static LicenceStartupCheck BuildCheck(ILicenceRawTextSource rawTextSource)
    {
        var services = new ServiceCollection();
        services.AddSingleton(rawTextSource);
        services.AddSingleton<ILicenceSignatureVerifier, Ed25519LicenceSignatureVerifier>();
        services.AddSingleton<LicenceVerifier>();
        services.AddScoped<LicenceStatusDisplayNames>(_ => { return LicensingTestContext.StatusText(); });

        var provider = services.BuildServiceProvider();

        return new LicenceStartupCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<LicenceStartupCheck>.Instance);
    }

    /// <summary>
    /// A scoped dependency throwing does not escape <see cref="LicenceStartupCheck.CheckOnceAsync"/>.
    /// </summary>
    /// <remarks>
    /// This is the mutant this slice's proof names: removing the guard's <c>catch (Exception)</c> arm
    /// (or narrowing it so it no longer catches <see cref="InvalidOperationException"/>) makes this
    /// test throw instead of complete, which is the observable difference a mutation kill needs.
    /// </remarks>
    [Fact]
    public async Task A_failure_reading_the_installed_licence_does_not_escape_the_check()
    {
        var check = BuildCheck(new ThrowingRawTextSource());

        // No exception thrown out of this call is the assertion; xUnit fails the test if one is.
        await check.CheckOnceAsync(CancellationToken.None);
    }

    /// <summary>The ordinary path — nothing installed — also completes without throwing.</summary>
    [Fact]
    public async Task Nothing_installed_also_completes_without_throwing()
    {
        var check = BuildCheck(new NoLicenceInstalledTextSource());

        await check.CheckOnceAsync(CancellationToken.None);
    }
}
