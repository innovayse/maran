using Maran.Packaging.Interfaces;
using Maran.Packaging.Options;
using Maran.Packaging.Services;
using Maran.Packaging.Tests.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Packaging.Tests.Services;

/// <summary>
/// The registration entry point. It wires the reader and the verifier with default caps and leaves the
/// signing key to the caller: with no <see cref="IIpmSignatureVerifier"/> registered, nothing resolves
/// to a verifier that trusts a key of this library's choosing.
/// </summary>
public sealed class DependencyInjectionTests
{
    /// <summary>With a signature verifier registered by the caller, the package verifier resolves and uses the default caps.</summary>
    [Fact]
    public void The_package_verifier_resolves_once_the_caller_supplies_the_key()
    {
        using var key = IpmFixtureBuilder.NewKey();
        var services = new ServiceCollection().AddPackaging();
        services.AddSingleton<IIpmSignatureVerifier>(IpmSignatureVerifier.FromKey(key).Value);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IpmPackageVerifier>());
        Assert.NotNull(provider.GetRequiredService<IpmReader>());
        Assert.Same(IpmLimits.Default, provider.GetRequiredService<IpmLimits>());
    }

    /// <summary>With no signature verifier registered, the package verifier cannot be resolved: the library carries no key.</summary>
    [Fact]
    public void The_package_verifier_does_not_resolve_without_a_key_from_the_caller()
    {
        using var provider = new ServiceCollection().AddPackaging().BuildServiceProvider();

        Assert.Null(provider.GetService<IIpmSignatureVerifier>());
        Assert.Throws<InvalidOperationException>(() =>
        {
            return provider.GetRequiredService<IpmPackageVerifier>();
        });
    }

    /// <summary>Caps registered before the library are kept, so a host can tighten them.</summary>
    [Fact]
    public void Caps_registered_before_the_library_are_kept()
    {
        var tighter = IpmLimits.Default with { MaxEntries = 8 };
        var services = new ServiceCollection();
        services.AddSingleton(tighter);

        using var provider = services.AddPackaging().BuildServiceProvider();

        Assert.Same(tighter, provider.GetRequiredService<IpmLimits>());
    }
}
