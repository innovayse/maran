using System.Globalization;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Tests.TestSupport;

namespace Maran.Modules.Backups.Tests.Services;

/// <summary>
/// The names an operator reads for a backup's verification state and for what a verification found,
/// in every language the panel ships.
/// </summary>
/// <remarks>
/// A state or a finding with no entry falls back to the machine constant (a raw code for a finding),
/// which is the defect the display-name law exists against, so the closed sets are walked and the
/// VALUES asserted per language rather than sampled.
/// </remarks>
public sealed class BackupVerificationNamesTests
{
    /// <summary>The findings a verification can record on a backup, which are this module's own codes.</summary>
    private static readonly string[] FindingCodes =
    [
        "BackupVerificationFailed",
        "BackupVerificationArtifactMissing",
    ];

    /// <summary>Every verification state has a name that is not its machine constant, in every language.</summary>
    /// <param name="culture">The request culture to resolve in.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public void Every_verification_state_has_a_name_in_every_language(string culture)
    {
        var names = BackupsTestContext.BackupNames();
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            foreach (var state in Enum.GetValues<BackupVerificationState>())
            {
                var name = names.Of(state);

                Assert.False(string.IsNullOrWhiteSpace(name));
                Assert.NotEqual("BackupVerificationState" + state, name);
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>The English name of the verified state is the word an operator reads, not a restore promise.</summary>
    [Fact]
    public void The_english_name_of_the_verified_state_is_verified()
    {
        Assert.Equal("Verified", BackupsTestContext.BackupNames().Of(BackupVerificationState.Verified));
    }

    /// <summary>Every finding a verification can record has a name an operator can read, in every language.</summary>
    /// <param name="culture">The request culture to resolve in.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public void Every_verification_finding_has_a_name_in_every_language(string culture)
    {
        var names = BackupsTestContext.FailureNames();
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);

            Assert.NotEmpty(FindingCodes);
            foreach (var code in FindingCodes)
            {
                Assert.NotEqual(code, names.Of(code));
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
