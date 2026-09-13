using Maran.Modules.Ftp.Commands.CreateFtpUser;
using Maran.Modules.Ftp.Tests.TestSupport;

namespace Maran.Modules.Ftp.Tests.Commands.CreateFtpUser;

/// <summary>The alphabet a login name is allowed, and the characters that must never survive it.</summary>
public sealed class CreateFtpUserCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly CreateFtpUserCommandValidator _validator = new();

    /// <summary>A lowercase alphanumeric name is accepted.</summary>
    /// <remarks>
    /// The inverse control every refusing rule owes: a pattern that refused everything would satisfy
    /// each rejection below and would make the endpoint unusable.
    /// </remarks>
    [Theory]
    [InlineData("files")]
    [InlineData("web2")]
    [InlineData("a")]
    public void A_lowercase_alphanumeric_name_is_accepted(string name)
    {
        Assert.True(_validator.Validate(Command(name)).IsValid);
    }

    /// <summary>A name carrying anything else is refused with this module's own code.</summary>
    /// <remarks>
    /// The newline and carriage-return cases are the ones with teeth. The value becomes an argument
    /// to the host's account tools and is compared against line-oriented files, and
    /// rules/security.md item 4 requires such a value to be REFUSED rather than escaped. The pattern
    /// is anchored with <c>\z</c> rather than <c>$</c> precisely so <c>files\n</c> does not pass —
    /// in .NET, <c>$</c> also matches immediately before a trailing newline.
    ///
    /// The underscore case has teeth of a different kind: account names may contain one, so a suffix
    /// carrying it would let account <c>alice</c> ask for <c>bob_files</c> and be handed
    /// <c>alice_bob_files</c>, a name that reads as account <c>bob</c>'s in every log an operator
    /// will ever look at.
    /// </remarks>
    [Theory]
    [InlineData("Files")]
    [InlineData("bob_files")]
    [InlineData("files-2")]
    [InlineData("files\n")]
    [InlineData("files\r")]
    [InlineData("fi les")]
    [InlineData("файлы")]
    public void A_name_outside_the_alphabet_is_refused(string name)
    {
        var result = _validator.Validate(Command(name));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == ErrorCodes.FtpUserNameInvalidFormat;
        });
    }

    /// <summary>An empty name is refused, and with the message the customer can act on.</summary>
    /// <remarks>
    /// <c>NotEmpty</c> carries no message of its own, so the answer a customer reads for a blank
    /// field is the one the alphabet rule produces — which is why that sentence says the name may
    /// not be empty as well as what it may contain. Asserted here because the code is what decides
    /// which sentence is rendered, and a bare <c>IsValid</c> check cannot see it.
    /// </remarks>
    [Fact]
    public void An_empty_name_is_refused_with_the_alphabet_message()
    {
        var result = _validator.Validate(Command(string.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == ErrorCodes.FtpUserNameInvalidFormat;
        });
    }

    /// <summary>A name past the coarse suffix ceiling is refused as TOO LONG, by name.</summary>
    /// <remarks>
    /// <para>
    /// Thirty-one characters. The exact ceiling depends on the account's own user name and is
    /// checked in the handler; this one only stops a value that cannot fit under any account.
    /// </para>
    /// <para>
    /// The refusal's CODE is asserted, not merely that the command was refused. A bare
    /// <c>IsValid</c> assertion passed while the rule carried no message of its own, and an unnamed
    /// rule does not fail loudly: <c>ExceptionMiddleware.ResolveValidationCode</c> collapses to the
    /// generic "check your input", so a customer whose only fault was a long name was sent to
    /// re-read a field that was otherwise perfect. <c>FtpUserNameInvalidFormat</c> is the wrong
    /// answer that this value could plausibly have produced, and equality rather than a substring
    /// bound is what separates the two.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_name_past_the_coarse_suffix_ceiling_is_refused_as_too_long()
    {
        var result = _validator.Validate(Command(new string('a', 31)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == ErrorCodes.FtpUserNameTooLong;
        });
        Assert.DoesNotContain(result.Errors, failure =>
        {
            return failure.ErrorMessage == ErrorCodes.FtpUserNameInvalidFormat;
        });
    }

    /// <summary>A name exactly at the coarse suffix ceiling is accepted.</summary>
    /// <remarks>The boundary asserted from the accepting side, at the exact value.</remarks>
    [Fact]
    public void A_name_exactly_at_the_coarse_suffix_ceiling_is_accepted()
    {
        Assert.True(_validator.Validate(Command(new string('a', 30))).IsValid);
    }

    /// <summary>An empty account id is refused.</summary>
    [Fact]
    public void An_empty_account_id_is_refused()
    {
        Assert.False(_validator.Validate(new CreateFtpUserCommand(Guid.Empty, "files")).IsValid);
    }

    /// <summary>Builds a command carrying one name against a fixed account.</summary>
    /// <param name="name">The login name to validate.</param>
    /// <returns>The command to hand the validator.</returns>
    private static CreateFtpUserCommand Command(string name)
    {
        return new CreateFtpUserCommand(new Guid("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72"), name);
    }
}
