using Maran.SharedKernel.Utilities.Mail;

namespace Maran.SharedKernel.Tests.Utilities.Mail;

/// <summary>rules/security.md item 4 applied to SMTP: no value bound for a header may carry a line break.</summary>
public sealed class MailHeaderTextRuleTests
{
    /// <summary>Ordinary text is accepted, accents and punctuation included.</summary>
    [Theory]
    [InlineData("Maran Panel")]
    [InlineData("smtp.example.com")]
    [InlineData("Ops — Yerevan")]
    public void Ordinary_text_is_accepted(string candidate)
    {
        Assert.True(MailHeaderTextRule.IsHeaderSafe(candidate));
    }

    /// <summary>An absent or empty value is acceptable, because several of these fields are optional.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_absent_value_is_acceptable(string? candidate)
    {
        Assert.True(MailHeaderTextRule.IsHeaderSafe(candidate));
    }

    /// <summary>A carriage return or newline is refused, because it would invent the next header.</summary>
    [Theory]
    [InlineData("Panel\r\nBcc: attacker@example.net")]
    [InlineData("Panel\nContent-Type: text/html")]
    [InlineData("Panel\r")]
    public void A_line_break_is_refused_because_it_would_invent_a_header(string candidate)
    {
        Assert.False(MailHeaderTextRule.IsHeaderSafe(candidate));
    }

    /// <summary>Every other control character goes too, not only the two that separate headers.</summary>
    /// <remarks>
    /// A NUL truncates the value for anything that reads it as a C string on the way, and no header
    /// field has a legitimate use for the rest.
    /// </remarks>
    [Theory]
    [InlineData("Panel\0truncated")]
    [InlineData("Panel\ttabbed")]
    public void Any_other_control_character_is_refused(string candidate)
    {
        Assert.False(MailHeaderTextRule.IsHeaderSafe(candidate));
    }
}
