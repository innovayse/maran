using System.Globalization;
using Maran.Modules.Ssl.Tests.TestSupport;

namespace Maran.Modules.Ssl.Tests.Services;

/// <summary>
/// A stored renewal failure code becomes the sentence its own table writes, in the reader's culture,
/// and a code no table describes becomes the generic sentence — never the code itself.
/// </summary>
public sealed class CertificateRenewalFailureMessagesTests
{
    /// <summary>A certificate with no failure is described as nothing.</summary>
    [Fact]
    public void A_certificate_with_no_failure_is_described_as_nothing()
    {
        Assert.Null(SslTestContext.FailureMessages().Describe(null));
    }

    /// <summary>A code a table describes reads as that table's sentence, in English.</summary>
    [Fact]
    public void A_described_code_reads_as_its_own_sentence()
    {
        var sentence = InCulture("en", () =>
        {
            return SslTestContext.FailureMessages().Describe("CertificateNotFound");
        });

        Assert.NotNull(sentence);
        Assert.NotEqual("CertificateNotFound", sentence);
        Assert.NotEqual(Generic("en"), sentence);
    }

    /// <summary>A code no table describes reads as the generic sentence, and never as the code.</summary>
    /// <param name="culture">The reader's culture.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public void A_code_no_table_describes_reads_as_the_generic_sentence_in_the_readers_culture(string culture)
    {
        var sentence = InCulture(culture, () =>
        {
            return SslTestContext.FailureMessages().Describe("SomeRemovedModuleFailure");
        });

        Assert.Equal(Generic(culture), sentence);
        Assert.DoesNotContain("SomeRemovedModuleFailure", sentence, StringComparison.Ordinal);
    }

    /// <summary>The Russian reader gets Cyrillic text, so the translation is real and not a copy of English.</summary>
    [Fact]
    public void The_russian_generic_sentence_is_a_real_translation()
    {
        Assert.NotEqual(Generic("en"), Generic("ru"));
        Assert.Contains("сертификат", Generic("ru"), StringComparison.Ordinal);
    }

    /// <summary>Reads the generic sentence for one culture.</summary>
    /// <param name="culture">The culture to read in.</param>
    /// <returns>The generic sentence.</returns>
    private static string Generic(string culture)
    {
        return InCulture(culture, () =>
        {
            return SslTestContext.FailureMessages().Describe("SomeRemovedModuleFailure")!;
        });
    }

    /// <summary>Runs <paramref name="read"/> with the UI culture a request would have set, then restores it.</summary>
    /// <typeparam name="T">What the read returns.</typeparam>
    /// <param name="culture">The culture to read in.</param>
    /// <param name="read">The read.</param>
    /// <returns>What the read returned.</returns>
    private static T InCulture<T>(string culture, Func<T> read)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return read();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
