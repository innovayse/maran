using Maran.Modules.Mail.Commands.SetMailboxAutoresponder;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Tests.TestSupport;

namespace Maran.Modules.Mail.Tests.Commands.SetMailboxAutoresponder;

/// <summary>Behavioral contract of <see cref="SetMailboxAutoresponderCommandValidator"/>.</summary>
public sealed class SetMailboxAutoresponderCommandValidatorTests
{
    /// <summary>The start of a valid window.</summary>
    private static readonly DateTimeOffset Start = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The validator under test.</summary>
    private readonly SetMailboxAutoresponderCommandValidator _validator = new();

    /// <summary>A well-formed reply and every edge it may sit on are accepted — the inverse control for the refusals.</summary>
    [Fact]
    public void A_well_formed_reply_and_the_edges_of_its_limits_are_accepted()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
        Assert.True(_validator.Validate(Valid() with { StartsAt = null, EndsAt = null, ReplyIntervalDays = null }).IsValid);
        Assert.True(_validator.Validate(Valid() with { Subject = new string('s', 200) }).IsValid);
        Assert.True(_validator.Validate(Valid() with { Body = new string('b', 4096) }).IsValid);
        Assert.True(_validator.Validate(Valid() with { ReplyIntervalDays = 1 }).IsValid);
        Assert.True(_validator.Validate(Valid() with { ReplyIntervalDays = 30 }).IsValid);
        Assert.True(_validator.Validate(Valid() with { Body = "Line one\nLine two. Not a dot start.\n\nEnd" }).IsValid);
        Assert.True(_validator.Validate(Valid() with { Body = new string('é', 2048) }).IsValid);
    }

    /// <summary>A subject that is empty, too long, or holds a control character or a variable expansion is refused.</summary>
    /// <param name="subject">The subject sent.</param>
    [Theory]
    [InlineData("")]
    [InlineData("Away\nBcc: someone@example.com")]
    [InlineData("Away\r")]
    [InlineData("Away\t")]
    [InlineData("Away ${env}")]
    public void A_subject_the_host_must_not_be_given_is_refused(string subject)
    {
        var result = _validator.Validate(Valid() with { Subject = subject });

        Assert.Equal(MailboxGroupErrorCodes.MailAutoresponderInvalid, Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A subject one character past the ceiling is refused.</summary>
    [Fact]
    public void A_subject_past_the_ceiling_is_refused()
    {
        var result = _validator.Validate(
            Valid() with { Subject = new string('s', MailDefaults.AutoresponderSubjectMaxLength + 1) });

        Assert.Equal(MailboxGroupErrorCodes.MailAutoresponderInvalid, Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A body that is empty or holds a forbidden control character, dot line or variable expansion is refused.</summary>
    /// <param name="body">The body sent.</param>
    [Theory]
    [InlineData("")]
    [InlineData("Line one\r\nLine two")]
    [InlineData("Tab\there")]
    [InlineData("Line one\n.\nLine three")]
    [InlineData(".leading dot")]
    [InlineData("Line one\n.hidden")]
    [InlineData("Hello ${name}")]
    [InlineData("Bell\a")]
    public void A_body_the_host_must_not_be_given_is_refused(string body)
    {
        var result = _validator.Validate(Valid() with { Body = body });

        Assert.Equal(MailboxGroupErrorCodes.MailAutoresponderInvalid, Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A body past 4096 bytes of UTF-8 is refused even when it is fewer than 4096 characters.</summary>
    [Fact]
    public void A_body_past_the_byte_ceiling_is_refused_by_bytes_not_characters()
    {
        var result = _validator.Validate(Valid() with { Body = new string('é', 2049) });

        Assert.Equal(MailboxGroupErrorCodes.MailAutoresponderInvalid, Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>An end at or before the start is refused; an end after it is accepted.</summary>
    [Fact]
    public void An_end_at_or_before_the_start_is_refused()
    {
        Assert.False(_validator.Validate(Valid() with { EndsAt = Start }).IsValid);
        Assert.False(_validator.Validate(Valid() with { EndsAt = Start.AddMinutes(-1) }).IsValid);
        Assert.True(_validator.Validate(Valid() with { EndsAt = Start.AddMinutes(1) }).IsValid);
    }

    /// <summary>A reply interval outside 1 to 30 days is refused.</summary>
    /// <param name="days">The interval sent.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(31)]
    public void A_reply_interval_outside_the_range_is_refused(int days)
    {
        var result = _validator.Validate(Valid() with { ReplyIntervalDays = days });

        Assert.Equal(MailboxGroupErrorCodes.MailAutoresponderInvalid, Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>Builds a command every rule accepts.</summary>
    /// <returns>The command.</returns>
    private static SetMailboxAutoresponderCommand Valid()
    {
        return new SetMailboxAutoresponderCommand("Away", "Back soon", Start, Start.AddDays(14), 7);
    }
}
