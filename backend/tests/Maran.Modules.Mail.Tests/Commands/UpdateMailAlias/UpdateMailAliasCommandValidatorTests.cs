using Maran.Modules.Mail.Commands.UpdateMailAlias;
using Maran.Modules.Mail.Tests.TestSupport;

namespace Maran.Modules.Mail.Tests.Commands.UpdateMailAlias;

/// <summary>Behavioral contract of <see cref="UpdateMailAliasCommandValidator"/>.</summary>
public sealed class UpdateMailAliasCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly UpdateMailAliasCommandValidator _validator = new();

    /// <summary>A well-formed target list is accepted — the inverse control for the refusals below.</summary>
    [Fact]
    public void A_well_formed_target_list_is_accepted()
    {
        Assert.True(_validator.Validate(new UpdateMailAliasCommand(["bob@example.com", "me@example.org"])).IsValid);
    }

    /// <summary>An empty, repeated or malformed list is refused with the invalid-target code.</summary>
    [Fact]
    public void An_empty_repeated_or_malformed_list_is_refused()
    {
        var empty = _validator.Validate(new UpdateMailAliasCommand([]));
        var repeated = _validator.Validate(new UpdateMailAliasCommand(["a@example.com", "A@EXAMPLE.com"]));
        var malformed = _validator.Validate(new UpdateMailAliasCommand(["user@[127.0.0.1]"]));
        var missing = _validator.Validate(new UpdateMailAliasCommand(null!));

        Assert.Equal(MailAliasErrorCodes.MailAliasTargetInvalid, Assert.Single(empty.Errors).ErrorMessage);
        Assert.Equal(MailAliasErrorCodes.MailAliasTargetInvalid, Assert.Single(repeated.Errors).ErrorMessage);
        Assert.Equal(MailAliasErrorCodes.MailAliasTargetInvalid, Assert.Single(malformed.Errors).ErrorMessage);
        Assert.Equal(MailAliasErrorCodes.MailAliasTargetInvalid, Assert.Single(missing.Errors).ErrorMessage);
    }

    /// <summary>A list beyond the ceiling is refused with the limit code.</summary>
    [Fact]
    public void A_list_beyond_the_ceiling_is_refused_with_the_limit_code()
    {
        var targets = Enumerable.Range(0, 21)
            .Select(index =>
            {
                return $"u{index}@example.com";
            })
            .ToArray();

        var result = _validator.Validate(new UpdateMailAliasCommand(targets));

        Assert.Equal(MailAliasErrorCodes.MailAliasTargetLimit, Assert.Single(result.Errors).ErrorMessage);
    }
}
