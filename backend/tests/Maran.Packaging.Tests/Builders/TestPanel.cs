using Maran.Packaging.Models;
using Maran.SharedKernel.Utilities.Versions;

namespace Maran.Packaging.Tests.Builders;

/// <summary>The panel every test runs a package against: a fixed version at a fixed instant, so nothing reads the machine.</summary>
public static class TestPanel
{
    /// <summary>The panel's version.</summary>
    public static SemanticVersion Version { get; } = Parse("1.4.0");

    /// <summary>The instant verification runs at.</summary>
    public static DateTimeOffset Now { get; } = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Parses a version a test names, failing the test if it does not read.</summary>
    /// <param name="text">The version text.</param>
    /// <returns>The version.</returns>
    public static SemanticVersion Parse(string text)
    {
        Assert.True(SemanticVersion.TryParse(text, out var version), text);
        return version!;
    }

    /// <summary>A context for judging a package on its own, with no installation.</summary>
    /// <returns>The context.</returns>
    public static IpmVerificationContext Alone()
    {
        return new IpmVerificationContext(Version, Now, null, null);
    }

    /// <summary>A context for judging a package against an installation.</summary>
    /// <param name="installed">The installed modules, id to version.</param>
    /// <param name="downgrade">The operator's downgrade decision, or null.</param>
    /// <returns>The context.</returns>
    public static IpmVerificationContext Installed(
        IReadOnlyDictionary<string, string> installed,
        IpmDowngradeOverride? downgrade = null)
    {
        return new IpmVerificationContext(Version, Now, installed, downgrade);
    }
}
