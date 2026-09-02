using Maran.SharedKernel.Security;

namespace Maran.SharedKernel.Tests.Security;

/// <summary>
/// The properties a password minted for a host credential has to have, each of which something
/// downstream silently depends on.
/// </summary>
public sealed class ProvisionedPasswordGeneratorTests
{
    /// <summary>
    /// Exactly the alphabet the agent's <c>Password</c> type accepts: ASCII letters, ASCII digits,
    /// and the five symbols it names. Written out again here rather than read from the production
    /// constant, because a test that reads the value under test agrees with any change to it.
    /// </summary>
    private const string AgentPasswordAlphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.=+";

    /// <summary>The generators alphabet is exactly the one the agent accepts.</summary>
    [Fact]
    public void The_generators_alphabet_is_exactly_the_one_the_agent_accepts()
    {
        // A character the agent refuses is a provisioning that fails AFTER the UI has promised
        // the customer their resource. A character the agent accepts but this omits is only lost entropy —
        // but the two sets drifting apart at all is what this pins.
        Assert.Equal(AgentPasswordAlphabet, ProvisionedPasswordGenerator.Alphabet);
    }

    /// <summary>
    /// The generators alphabet excludes every character that could break out of root sql or a chpasswd line.
    /// </summary>
    [Theory]
    [InlineData('\'')]
    [InlineData('"')]
    [InlineData('`')]
    [InlineData('\\')]
    [InlineData(';')]
    [InlineData(':')]
    [InlineData(' ')]
    [InlineData('\n')]
    [InlineData('\r')]
    public void The_generators_alphabet_excludes_every_character_that_could_break_out_of_root_sql_or_a_chpasswd_line(
        char forbidden)
    {
        // Stated as its own test rather than left implicit in the equality above, because THIS is
        // the property that matters: the value is interpolated into
        // `IDENTIFIED BY '<value>'` in a root MySQL session and written into a `user:password`
        // line on chpasswd's standard input, neither of which takes a placeholder.
        Assert.DoesNotContain(forbidden, ProvisionedPasswordGenerator.Alphabet);
    }

    /// <summary>The generated length is at or above the floor the agent error redaction needs.</summary>
    [Fact]
    public void The_generated_length_is_at_or_above_the_floor_the_agent_error_redaction_needs()
    {
        // The silent one. AgentErrorTranslator strips the secret a call carried out of the agent's
        // text before logging it, but only when that secret is long enough to be searched for
        // literally. Drop below the floor and nothing visibly breaks: the call still succeeds, the
        // log line is still written, and the only difference is the customer's password sitting in
        // it. This is the assertion that makes the floor a contract rather than a comment.
        Assert.True(
            ProvisionedPasswordGenerator.PasswordLength >= SecretRedactionPolicy.ShortestRecognisableSecret,
            $"a {ProvisionedPasswordGenerator.PasswordLength}-character password is below the "
            + $"{SecretRedactionPolicy.ShortestRecognisableSecret}-character floor the agent-error "
            + "redaction searches at, so the password would be logged verbatim when the server quotes "
            + "it back.");
    }

    /// <summary>A generated password has the declared length and only allowed characters.</summary>
    [Fact]
    public void A_generated_password_has_the_declared_length_and_only_allowed_characters()
    {
        var password = ProvisionedPasswordGenerator.Generate();

        Assert.Equal(ProvisionedPasswordGenerator.PasswordLength, password.Reveal().Length);
        Assert.All(
            password.Reveal(),
            character =>
            {
                Assert.Contains(character.ToString(), AgentPasswordAlphabet, StringComparison.Ordinal);
            });
    }

    /// <summary>Two generated passwords differ.</summary>
    [Fact]
    public void Two_generated_passwords_differ()
    {
        // Not a randomness test — no test can be one — but it does catch the whole class of mistakes
        // that returns a constant, a seeded sequence, or the same buffer twice.
        Assert.NotEqual(
            ProvisionedPasswordGenerator.Generate().Reveal(),
            ProvisionedPasswordGenerator.Generate().Reveal());
    }

    /// <summary>A generated password never prints itself.</summary>
    [Fact]
    public void A_generated_password_never_prints_itself()
    {
        // The carrier is the defence against the leak nobody writes on purpose: a record's generated
        // ToString, a structured-logging argument, an interpolated exception message.
        var password = ProvisionedPasswordGenerator.Generate();

        Assert.DoesNotContain(password.Reveal(), $"{password}", StringComparison.Ordinal);
    }
}
