using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Validators;

namespace Maran.Modules.Cron.Tests.Validators;

/// <summary>What this panel accepts as one cron environment assignment.</summary>
public sealed class CronEnvironmentVariableValidatorTests
{
    /// <summary>An ordinary assignment is accepted.</summary>
    [Theory]
    [InlineData("PATH", "/usr/local/bin:/usr/bin")]
    [InlineData("TZ", "Europe/Yerevan")]
    [InlineData("_PRIVATE", "x")]
    [InlineData("APP_ENV2", "production")]
    public void An_ordinary_assignment_is_accepted(string name, string value)
    {
        Assert.True(IsValid(name, value));
    }

    /// <summary>An empty value is accepted because it is a real assignment.</summary>
    [Fact]
    public void An_empty_value_is_accepted_because_it_is_a_real_assignment()
    {
        // `TZ=` sets the variable to the empty string, which is different from not setting it.
        Assert.True(IsValid("TZ", string.Empty));
    }

    /// <summary>A name outside the shells own alphabet is refused.</summary>
    [Theory]
    [InlineData("path")]
    [InlineData("MY-VAR")]
    [InlineData("MY VAR")]
    [InlineData("2FAST")]
    [InlineData("")]
    public void A_name_outside_the_shells_own_alphabet_is_refused(string name)
    {
        // Unlike the command, this really does end up on a crontab line, so the alphabet is a
        // permitted set rather than a list of refusals.
        Assert.False(IsValid(name, "x"));
    }

    /// <summary>The two names the agent writes itself are refused.</summary>
    [Theory]
    [InlineData("MAILTO")]
    [InlineData("SHELL")]
    public void The_two_names_the_agent_writes_itself_are_refused(string name)
    {
        // A customer who could set MAILTO would have an outbound mail relay through the host's mail
        // transfer agent; one who could set SHELL would choose the interpreter every one of their
        // entries runs under, including entries created before they changed it.
        Assert.False(IsValid(name, "x"));
    }

    /// <summary>A percent sign in a value is refused because cron rewrites it into a newline.</summary>
    [Fact]
    public void A_percent_sign_in_a_value_is_refused_because_cron_rewrites_it_into_a_newline()
    {
        // The rewrite happens to the LINE, and an assignment IS the line — which is why the value is
        // stricter than the command, which was moved off the line into a file.
        Assert.False(IsValid("RATE", "100%"));
    }

    /// <summary>A value cron would silently alter on its way in is refused.</summary>
    [Theory]
    [InlineData(" x")]
    [InlineData("x ")]
    [InlineData("\"x\"")]
    [InlineData("'x'")]
    public void A_value_cron_would_silently_alter_on_its_way_in_is_refused(string value)
    {
        // Cron trims whitespace around a value and strips a matching pair of quotes, so all of these
        // set one variable to one thing — while a panel that stored them would show several
        // different values and call the rest wrong when comparing.
        Assert.False(IsValid("TZ", value));
    }

    /// <summary>A lone quote is not a pair and is accepted.</summary>
    [Fact]
    public void A_lone_quote_is_not_a_pair_and_is_accepted()
    {
        // The boundary of the quote rule from the other side: a one-character value cannot be a pair
        // of quotes around anything, and refusing it would refuse a legal value.
        Assert.True(IsValid("Q", "\""));
    }

    /// <summary>A control character in a value is refused.</summary>
    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    public void A_control_character_in_a_value_is_refused(string value)
    {
        Assert.False(IsValid("TZ", value));
    }

    /// <summary>A name or value past what cron itself keeps is refused.</summary>
    [Fact]
    public void A_name_or_value_past_what_cron_itself_keeps_is_refused()
    {
        // Cron reads an environment line into a fixed buffer and discards the rest SILENTLY. A
        // ceiling above what survives would let the panel store and display a PATH that the host
        // runs truncated, which is the worst shape a limit can have because nothing reports it.
        Assert.False(IsValid(new string('A', 65), "x"));
        Assert.True(IsValid(new string('A', 64), "x"));
        Assert.False(IsValid("PATH", new string('x', 935)));
        Assert.True(IsValid("PATH", new string('x', 934)));
    }

    /// <summary>Runs the validator over one assignment.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The variable value.</param>
    /// <returns>Whether the validator accepted it.</returns>
    private static bool IsValid(string name, string value)
    {
        return new CronEnvironmentVariableValidator()
            .Validate(new CronEnvironmentVariableDto(name, value))
            .IsValid;
    }
}
