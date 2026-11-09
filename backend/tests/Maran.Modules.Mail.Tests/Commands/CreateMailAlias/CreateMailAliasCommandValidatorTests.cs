using Maran.Modules.Mail.Commands.CreateMailAlias;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Tests.TestSupport;

namespace Maran.Modules.Mail.Tests.Commands.CreateMailAlias;

/// <summary>Behavioral contract of <see cref="CreateMailAliasCommandValidator"/>.</summary>
public sealed class CreateMailAliasCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly CreateMailAliasCommandValidator _validator = new();

    /// <summary>An alias, a forwarder and a catch-all, each well formed, are accepted — the inverse control for the refusals below.</summary>
    [Fact]
    public void A_well_formed_alias_forwarder_and_catch_all_are_accepted()
    {
        Assert.True(_validator.Validate(Command(MailAliasKind.Alias, "info", "bob@example.com")).IsValid);
        Assert.True(_validator.Validate(Command(MailAliasKind.Forwarder, "sales", "me@example.org")).IsValid);
        Assert.True(_validator.Validate(Command(MailAliasKind.CatchAll, string.Empty, "bob@example.com")).IsValid);
    }

    /// <summary>A local part typed in capitals is accepted, because the handler folds it to one spelling.</summary>
    [Fact]
    public void A_local_part_in_capitals_is_accepted_because_it_is_folded()
    {
        Assert.True(_validator.Validate(Command(MailAliasKind.Alias, "  Info.Desk ", "bob@example.com")).IsValid);
    }

    /// <summary>A local part outside the mailbox grammar is refused with the typed code.</summary>
    /// <param name="localPart">The local part.</param>
    [Theory]
    [InlineData("")]
    [InlineData("a+b")]
    [InlineData(".x")]
    [InlineData("x.")]
    [InlineData("a..b")]
    [InlineData("has space")]
    [InlineData("tab\there")]
    [InlineData("new\nline")]
    [InlineData("*")]
    public void A_local_part_outside_the_grammar_is_refused(string localPart)
    {
        var result = _validator.Validate(Command(MailAliasKind.Alias, localPart, "bob@example.com"));

        Assert.Equal(MailAliasErrorCodes.MailboxLocalPartInvalid, Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A local part longer than the ceiling is refused, and the ceiling itself is accepted.</summary>
    [Fact]
    public void A_local_part_beyond_the_ceiling_is_refused_and_the_ceiling_is_accepted()
    {
        Assert.True(_validator.Validate(Command(MailAliasKind.Alias, new string('a', 64), "bob@example.com")).IsValid);
        Assert.False(_validator.Validate(Command(MailAliasKind.Alias, new string('a', 65), "bob@example.com")).IsValid);
    }

    /// <summary>A catch-all has no local part, so one that carries text is refused rather than ignored.</summary>
    /// <param name="localPart">The local part sent with a catch-all.</param>
    [Theory]
    [InlineData("info")]
    [InlineData("*")]
    [InlineData(" ")]
    public void A_catch_all_that_carries_a_local_part_is_refused(string localPart)
    {
        var result = _validator.Validate(Command(MailAliasKind.CatchAll, localPart, "bob@example.com"));

        Assert.Equal(MailAliasErrorCodes.MailboxLocalPartInvalid, Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>An empty domain id and a kind that names nothing are refused.</summary>
    [Fact]
    public void An_empty_domain_and_an_undefined_kind_are_refused()
    {
        Assert.False(_validator.Validate(
            Command(MailAliasKind.Alias, "info", "bob@example.com") with { MailDomainId = Guid.Empty }).IsValid);
        Assert.False(_validator.Validate(Command((MailAliasKind)7, "info", "bob@example.com")).IsValid);
    }

    /// <summary>A target list the policy refuses is refused with the policy's own code.</summary>
    [Fact]
    public void A_target_list_the_policy_refuses_is_refused_with_its_code()
    {
        var invalid = _validator.Validate(Command(MailAliasKind.Alias, "info", "not an address"));
        var empty = _validator.Validate(Command(MailAliasKind.Alias, "info"));
        var repeated = _validator.Validate(Command(MailAliasKind.Alias, "info", "a@example.com", "A@example.com"));
        var tooMany = _validator.Validate(Command(
            MailAliasKind.Alias,
            "info",
            Enumerable.Range(0, 21)
                .Select(index =>
                {
                    return $"u{index}@example.com";
                })
                .ToArray()));

        Assert.Equal(MailAliasErrorCodes.MailAliasTargetInvalid, Assert.Single(invalid.Errors).ErrorMessage);
        Assert.Equal(MailAliasErrorCodes.MailAliasTargetInvalid, Assert.Single(empty.Errors).ErrorMessage);
        Assert.Equal(MailAliasErrorCodes.MailAliasTargetInvalid, Assert.Single(repeated.Errors).ErrorMessage);
        Assert.Equal(MailAliasErrorCodes.MailAliasTargetLimit, Assert.Single(tooMany.Errors).ErrorMessage);
    }

    /// <summary>A missing target list is refused rather than crashing the validator.</summary>
    [Fact]
    public void A_missing_target_list_is_refused()
    {
        var command = Command(MailAliasKind.Alias, "info") with { Targets = null! };

        var result = _validator.Validate(command);

        Assert.Equal(MailAliasErrorCodes.MailAliasTargetInvalid, Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>Builds a command for a fresh domain.</summary>
    /// <param name="kind">The stated kind.</param>
    /// <param name="localPart">The local part.</param>
    /// <param name="targets">The targets.</param>
    /// <returns>The command.</returns>
    private static CreateMailAliasCommand Command(MailAliasKind kind, string localPart, params string[] targets)
    {
        return new CreateMailAliasCommand(Guid.NewGuid(), kind, localPart, targets);
    }
}
