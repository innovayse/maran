using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Tests.Domain.Policies;

/// <summary>
/// A template record is held to the same shape law as a customer's own record, applied to what it expands to:
/// a template opens no second path for a value into a zone file. Each refusal has an accepting control.
/// </summary>
public sealed class TemplateRecordRulesTests
{
    /// <summary>Checks a record after normalising it, as the validator and the handler do.</summary>
    /// <param name="record">The record.</param>
    /// <returns>The verdict.</returns>
    private static TemplateRecordVerdict Check(TemplateRecordDraft record)
    {
        return TemplateRecordRules.Check(TemplateExpansionPolicy.Normalize(record));
    }

    /// <summary>A text record carrying a newline is refused at save, because its expansion fails the text rule.</summary>
    [Fact]
    public void A_text_record_with_a_newline_is_refused()
    {
        var verdict = Check(new TemplateRecordDraft("@", RecordType.Txt, null, "v=spf1 mx\n$INCLUDE /etc/passwd"));

        Assert.False(verdict.IsAccepted);
        Assert.Equal(RecordShapeVerdict.ValueInvalid, verdict.Shape);
    }

    /// <summary>Inverse control: the same record with no newline is accepted.</summary>
    [Fact]
    public void A_valid_text_record_is_accepted()
    {
        Assert.True(Check(new TemplateRecordDraft("@", RecordType.Txt, null, "v=spf1 mx ~all")).IsAccepted);
    }

    /// <summary>A quote or a backslash in text is refused, with a variable in the same value or without one.</summary>
    [Theory]
    [InlineData("say \"hi\"")]
    [InlineData("back\\slash")]
    [InlineData("{apex} \"x\"")]
    public void A_quote_or_a_backslash_in_text_is_refused(string value)
    {
        Assert.False(Check(new TemplateRecordDraft("@", RecordType.Txt, null, value)).IsAccepted);
    }

    /// <summary>An owner name never carries a variable: the name is refused by the name policy.</summary>
    [Fact]
    public void An_owner_name_with_a_variable_is_refused()
    {
        var verdict = Check(new TemplateRecordDraft("{apex}", RecordType.A, null, "{ipv4}"));

        Assert.Equal(RecordShapeVerdict.NameInvalid, verdict.Shape);
        Assert.True(Check(new TemplateRecordDraft("www", RecordType.A, null, "{ipv4}")).IsAccepted);
    }

    /// <summary>A refused token is reported as the token verdict, and the shape is not weighed.</summary>
    [Fact]
    public void A_refused_token_is_reported_before_the_shape()
    {
        var verdict = Check(new TemplateRecordDraft("@", RecordType.A, null, "{ipv6}"));

        Assert.Equal(TemplateTokenVerdict.NotAllowedHere, verdict.Token);
        Assert.False(verdict.IsAccepted);
    }

    /// <summary>A literal address and a variable are both accepted for an A record; a non-address is refused.</summary>
    [Fact]
    public void An_address_record_takes_a_variable_or_a_valid_literal_only()
    {
        Assert.True(Check(new TemplateRecordDraft("@", RecordType.A, null, "{ipv4}")).IsAccepted);
        Assert.True(Check(new TemplateRecordDraft("@", RecordType.A, null, "203.0.113.9")).IsAccepted);
        Assert.False(Check(new TemplateRecordDraft("@", RecordType.A, null, "not-an-address")).IsAccepted);
    }

    /// <summary>A time to live outside the zone's bounds is refused; leaving it to the settings is accepted.</summary>
    [Fact]
    public void A_time_to_live_outside_the_bounds_is_refused()
    {
        Assert.Equal(
            RecordShapeVerdict.TtlOutOfRange,
            Check(new TemplateRecordDraft("@", RecordType.A, 5, "{ipv4}")).Shape);
        Assert.True(Check(new TemplateRecordDraft("@", RecordType.A, null, "{ipv4}")).IsAccepted);
    }

    /// <summary>A field a type does not use must stay at its default, exactly as for a customer's record.</summary>
    [Fact]
    public void A_field_the_type_does_not_use_is_refused()
    {
        Assert.Equal(
            RecordShapeVerdict.PriorityOutOfRange,
            Check(new TemplateRecordDraft("@", RecordType.A, null, "{ipv4}", Priority: 5)).Shape);
        Assert.True(Check(new TemplateRecordDraft("@", RecordType.Mx, null, "mail.{apex}", Priority: 5)).IsAccepted);
    }
}
