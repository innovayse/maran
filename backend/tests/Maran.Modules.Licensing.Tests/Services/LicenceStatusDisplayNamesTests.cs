using Maran.Modules.Licensing.Domain.Entities;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Modules.Licensing.Services;
using Maran.Modules.Licensing.Tests.TestSupport;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers that every outcome an operator can see — <see cref="LicenceStatus.Valid"/>,
/// <see cref="LicenceStatus.Absent"/>, and every member of <see cref="LicenceRefusalReason"/> — has a
/// real sentence in all three shipped languages, walked rather than sampled (same shape as the
/// Databases module's own <c>GrantRepairRefusalDisplayNamesTests</c>).
/// </summary>
public sealed class LicenceStatusDisplayNamesTests
{
    /// <summary>The three cultures this panel ships, neutral first.</summary>
    private static readonly string[] Cultures = ["en", "ru", "hy"];

    /// <summary>Every refusal reason has a name and an action in every language.</summary>
    [Fact]
    public void Every_refusal_reason_has_a_name_and_an_advice_sentence_in_every_language()
    {
        var reasons = Enum.GetValues<LicenceRefusalReason>();
        Assert.NotEmpty(reasons);

        var problems = new List<string>();
        foreach (var culture in Cultures)
        {
            using var _ = new CultureScope(culture);
            var text = LicensingTestContext.StatusText();

            foreach (var reason in reasons)
            {
                var machine = reason.ToString();
                var name = text.SentenceFor(new LicenceStatus.Refused(reason));
                var advice = text.AdviceFor(reason);

                if (string.Equals(name, "LicenceRefusal" + machine, StringComparison.Ordinal))
                {
                    problems.Add($"'{machine}' has no name in {culture}; the screen would print the resource key.");
                }

                if (string.Equals(advice, "LicenceAdvice" + machine, StringComparison.Ordinal))
                {
                    problems.Add($"'{machine}' has no advice in {culture}; the screen would print the resource key.");
                }
            }
        }

        Assert.True(
            problems.Count == 0,
            "A licence refusal reaches the operator's screen as a resource key:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, problems));
    }

    /// <summary>The walk really covers the enum, including the member no code path can produce yet.</summary>
    /// <remarks>
    /// The positive control the walk above owes (rules/testing.md): a reflection query returning
    /// nothing would make it pass over nothing at all. Explicitly names
    /// <see cref="LicenceRefusalReason.FingerprintMismatch"/>, still unreachable from
    /// <see cref="LicenceVerifier"/> in this build, to prove its text exists anyway.
    /// </remarks>
    [Fact]
    public void The_walk_reads_the_real_enum_including_the_still_unreachable_member()
    {
        var reasons = Enum.GetValues<LicenceRefusalReason>().Select(reason => { return reason.ToString(); }).ToList();

        Assert.Contains("Malformed", reasons);
        Assert.Contains("SignatureInvalid", reasons);
        Assert.Contains("Expired", reasons);
        Assert.Contains("ProductMismatch", reasons);
        Assert.Contains("FingerprintMismatch", reasons);
        Assert.Equal(5, reasons.Count);
    }

    /// <summary>Absent and Valid each carry their own sentence, not a machine identifier.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public void Absent_and_valid_each_carry_a_real_sentence(string culture)
    {
        using var _ = new CultureScope(culture);
        var text = LicensingTestContext.StatusText();

        var licence = new Licence(
            LicenceId.Of("lic-001"),
            "maran",
            "included",
            ["databases"],
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.NotEqual("LicenceStatusAbsent", text.SentenceFor(LicenceStatus.Absent.Instance));
        Assert.NotEqual("LicenceStatusValid", text.SentenceFor(new LicenceStatus.Valid(licence)));
    }

    /// <summary>Every wire state has a real, specific short label in every language.</summary>
    /// <remarks>
    /// Asserts an exact Russian sentence rather than merely "not empty" or "not the raw key": a
    /// resolver that always answers with the machine value passes a presence check but not this one
    /// — this is the check that kills the mutant where the resx lookup is bypassed
    /// (rules/architecture.md, "The backend owns the data, the SPA renders it").
    /// </remarks>
    [Fact]
    public void Every_wire_state_has_a_specific_localized_label()
    {
        using var _ = new CultureScope("ru");
        var text = LicensingTestContext.StatusText();

        Assert.Equal("Лицензия установлена", text.StateNameFor("Valid"));
        Assert.Equal("Лицензия не установлена", text.StateNameFor("Absent"));
        Assert.Equal("Лицензия не принята", text.StateNameFor("Refused"));
    }

    /// <summary>Every wire refusal reason has a real, specific short label in every language.</summary>
    /// <remarks>Same mutation-killing shape as <see cref="Every_wire_state_has_a_specific_localized_label"/>.</remarks>
    [Fact]
    public void Every_wire_refusal_reason_has_a_specific_localized_label()
    {
        using var _ = new CultureScope("ru");
        var text = LicensingTestContext.StatusText();

        Assert.Equal("Файл лицензии не читается", text.ReasonNameFor("Malformed"));
        Assert.Equal("Подпись лицензии не совпадает", text.ReasonNameFor("SignatureInvalid"));
        Assert.Equal("Срок действия лицензии истёк", text.ReasonNameFor("Expired"));
        Assert.Equal("Лицензия выпущена для другого продукта", text.ReasonNameFor("ProductMismatch"));
        Assert.Equal("Лицензия выпущена для другого сервера", text.ReasonNameFor("FingerprintMismatch"));
    }

    /// <summary>An unrecognized wire value degrades to itself rather than to an empty string or a resource key.</summary>
    [Fact]
    public void An_unrecognized_wire_value_falls_back_to_itself()
    {
        using var _ = new CultureScope("ru");
        var text = LicensingTestContext.StatusText();

        Assert.Equal("SomeFutureState", text.StateNameFor("SomeFutureState"));
        Assert.Equal("SomeFutureReason", text.ReasonNameFor("SomeFutureReason"));
    }

    /// <summary>Absent's own sentence does not read as an error — the ordinary-state requirement.</summary>
    /// <remarks>
    /// A narrow, mechanical check on a requirement that is otherwise a judgement call: it cannot prove
    /// the wording is neutral, but it can prove the words this design was explicitly told never to use
    /// are absent from the one state that is not a fault.
    /// </remarks>
    [Fact]
    public void Absent_does_not_use_words_that_read_as_an_error_or_accusation()
    {
        using var _ = new CultureScope("en");
        var text = LicensingTestContext.StatusText();
        var sentence = text.SentenceFor(LicenceStatus.Absent.Instance);

        foreach (var word in new[] { "error", "invalid", "fail", "wrong", "violat", "illegal" })
        {
            Assert.DoesNotContain(word, sentence, StringComparison.OrdinalIgnoreCase);
        }
    }
}
