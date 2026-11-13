using System.Globalization;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Tests.Errors;

/// <summary>
/// The one translation of a wire <see cref="ErrorCode"/> into the panel's kind and machine code: the
/// file manager's two codes (issue #167) each have an arm of their own, and every code the translator
/// can answer with has a sentence in all three languages.
/// </summary>
public sealed class AgentErrorTranslatorTests
{
    /// <summary>The cultures every error table ships.</summary>
    private static readonly string[] Cultures = ["en", "ru", "hy"];

    /// <summary>A spent disk quota is a conflict with the customer's own state, not a server fault.</summary>
    [Fact]
    public void A_spent_disk_quota_is_a_conflict_with_its_own_code()
    {
        Assert.Equal(ErrorType.Conflict, AgentErrorTranslator.ToErrorType(ErrorCode.QuotaExceeded));
        Assert.Equal("AgentQuotaExceeded", AgentErrorTranslator.ToErrorCode(ErrorCode.QuotaExceeded));
    }

    /// <summary>An unsafe archive is a request the customer can change, so it is a validation failure.</summary>
    [Fact]
    public void An_unsafe_archive_is_a_validation_failure_with_its_own_code()
    {
        Assert.Equal(ErrorType.Validation, AgentErrorTranslator.ToErrorType(ErrorCode.ArchiveUnsafe));
        Assert.Equal("AgentArchiveUnsafe", AgentErrorTranslator.ToErrorCode(ErrorCode.ArchiveUnsafe));
    }

    /// <summary>A file or folder too large to open is a request the caller cannot repeat unchanged, so it is a validation failure.</summary>
    [Fact]
    public void A_file_too_large_to_open_is_a_validation_failure_with_its_own_code()
    {
        Assert.Equal(ErrorType.Validation, AgentErrorTranslator.ToErrorType(ErrorCode.TooLarge));
        Assert.Equal("AgentTooLarge", AgentErrorTranslator.ToErrorCode(ErrorCode.TooLarge));
    }

    /// <summary>
    /// The three app-store refusals (issue #517) have an arm of their own: an app that is not there is a
    /// not-found, an app a site uses and a second web server are conflicts, and none rides on the
    /// unspecified arm.
    /// </summary>
    /// <param name="code">The wire code.</param>
    /// <param name="expectedCode">The panel's code.</param>
    /// <param name="expectedType">The panel's kind.</param>
    [Theory]
    [InlineData(ErrorCode.AppNotInstalled, "AgentAppNotInstalled", ErrorType.NotFound)]
    [InlineData(ErrorCode.AppInUse, "AgentAppInUse", ErrorType.Conflict)]
    [InlineData(ErrorCode.WebServerAlreadyInstalled, "AgentWebServerAlreadyInstalled", ErrorType.Conflict)]
    [InlineData(ErrorCode.NodeMajorAlreadyInstalled, "AgentNodeMajorAlreadyInstalled", ErrorType.Conflict)]
    [InlineData(ErrorCode.NodeUnmanagedInstall, "AgentNodeUnmanagedInstall", ErrorType.Conflict)]
    [InlineData(ErrorCode.NodeStreamEnabledByOperator, "AgentNodeStreamEnabledByOperator", ErrorType.Conflict)]
    [InlineData(ErrorCode.NodeInterpreterUnreadable, "AgentNodeInterpreterUnreadable", ErrorType.Conflict)]
    [InlineData(ErrorCode.NodeOperationInProgress, "AgentNodeOperationInProgress", ErrorType.Conflict)]
    public void The_app_store_refusals_have_their_own_code_and_kind(
        ErrorCode code,
        string expectedCode,
        ErrorType expectedType)
    {
        Assert.Equal(expectedType, AgentErrorTranslator.ToErrorType(code));
        Assert.Equal(expectedCode, AgentErrorTranslator.ToErrorCode(code));
        Assert.NotEqual(AgentErrorTranslator.ToErrorCode(ErrorCode.Unspecified), AgentErrorTranslator.ToErrorCode(code));
    }

    /// <summary>
    /// A sealed backup archive that does not open under the destination's key is a validation failure
    /// with its own code, not the shared "correct these details" one (issue #445).
    /// </summary>
    [Fact]
    public void A_sealed_archive_that_cannot_be_opened_is_a_validation_failure_with_its_own_code()
    {
        Assert.Equal(ErrorType.Validation, AgentErrorTranslator.ToErrorType(ErrorCode.ArchiveUnopenable));
        Assert.Equal("AgentArchiveUnopenable", AgentErrorTranslator.ToErrorCode(ErrorCode.ArchiveUnopenable));
        Assert.NotEqual(
            AgentErrorTranslator.ToErrorCode(ErrorCode.ValidationFailed),
            AgentErrorTranslator.ToErrorCode(ErrorCode.ArchiveUnopenable));
    }

    /// <summary>
    /// The new codes do not ride on the unspecified arm or on the codes they are easily mistaken for:
    /// the host's free-space floor and a stated limit.
    /// </summary>
    [Fact]
    public void The_new_codes_are_not_the_codes_they_resemble()
    {
        var unspecified = AgentErrorTranslator.ToErrorCode(ErrorCode.Unspecified);
        var floor = AgentErrorTranslator.ToErrorCode(ErrorCode.BelowFreeSpaceFloor);
        var limit = AgentErrorTranslator.ToErrorCode(ErrorCode.LimitReached);

        foreach (var code in new[] { ErrorCode.QuotaExceeded, ErrorCode.ArchiveUnsafe, ErrorCode.TooLarge, ErrorCode.ArchiveUnopenable })
        {
            var translated = AgentErrorTranslator.ToErrorCode(code);

            Assert.NotEqual(unspecified, translated);
            Assert.NotEqual(floor, translated);
            Assert.NotEqual(limit, translated);
        }
    }

    /// <summary>
    /// Every code the translator can answer with, for every wire value, has a non-empty sentence in
    /// English, Russian and Armenian read from that culture's own table and not by fallback.
    /// </summary>
    [Fact]
    public void Every_translated_code_has_a_sentence_in_every_language()
    {
        var sets = Cultures.ToDictionary(
            culture => { return culture; },
            culture => { return ErrorMessages.ResourceManager.GetResourceSet(new CultureInfo(culture), true, false); });

        // Positive control: a code that has always been there is found in each table, so a table the
        // lookup could not open (null) or read nothing from cannot pass this test by being empty.
        foreach (var culture in Cultures)
        {
            Assert.NotNull(sets[culture]);
            Assert.False(string.IsNullOrWhiteSpace(sets[culture]!.GetString("AgentLimitReached")), culture);
        }

        foreach (var wire in Enum.GetValues<ErrorCode>())
        {
            var key = AgentErrorTranslator.ToErrorCode(wire);

            foreach (var culture in Cultures)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(sets[culture]!.GetString(key)),
                    $"{wire} translates to {key}, which has no sentence in {culture}");
            }
        }
    }
}
