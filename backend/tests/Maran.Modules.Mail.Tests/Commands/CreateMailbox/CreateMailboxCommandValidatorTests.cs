using Maran.Modules.Mail.Commands.CreateMailbox;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Tests.TestSupport;

namespace Maran.Modules.Mail.Tests.Commands.CreateMailbox;

/// <summary>Behavioral contract of <see cref="CreateMailboxCommandValidator"/>.</summary>
public sealed class CreateMailboxCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly CreateMailboxCommandValidator _validator = new();

    /// <summary>A well-formed command, and the ends of the ranges, are accepted — the inverse control for the refusals.</summary>
    [Fact]
    public void A_well_formed_command_and_the_ends_of_its_ranges_are_accepted()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
        Assert.True(_validator.Validate(Valid() with { QuotaMib = 1 }).IsValid);
        Assert.True(_validator.Validate(Valid() with { QuotaMib = MailDefaults.MaxMailboxQuotaMib }).IsValid);
        Assert.True(_validator.Validate(Valid() with { LocalPart = "  Info.Desk-1 " }).IsValid);
    }

    /// <summary>A local part the address policy refuses is refused with its own code.</summary>
    /// <param name="localPart">The local part sent.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-info")]
    [InlineData("info.")]
    [InlineData("in..fo")]
    [InlineData("in fo")]
    [InlineData("info@example.com")]
    [InlineData("*")]
    [InlineData("in\nfo")]
    public void A_local_part_the_address_policy_refuses_is_refused(string localPart)
    {
        var result = _validator.Validate(Valid() with { LocalPart = localPart });

        Assert.Equal(MailboxGroupErrorCodes.MailboxLocalPartInvalid, Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A local part longer than the ceiling is refused.</summary>
    [Fact]
    public void A_local_part_beyond_the_ceiling_is_refused()
    {
        var result = _validator.Validate(
            Valid() with { LocalPart = new string('a', MailDefaults.LocalPartMaxLength + 1) });

        Assert.Equal(MailboxGroupErrorCodes.MailboxLocalPartInvalid, Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A password the policy refuses is refused with the weak-password code.</summary>
    /// <param name="password">The password sent.</param>
    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("elevenchars")]
    [InlineData("has a tab\tinside it")]
    public void A_password_the_policy_refuses_is_refused(string password)
    {
        var result = _validator.Validate(Valid() with { Password = password });

        Assert.Equal(MailboxGroupErrorCodes.MailPasswordTooWeak, Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>A quota outside 1 to the ceiling is refused.</summary>
    /// <param name="quota">The quota sent.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(MailDefaults.MaxMailboxQuotaMib + 1)]
    public void A_quota_outside_the_range_is_refused(int quota)
    {
        var result = _validator.Validate(Valid() with { QuotaMib = quota });

        Assert.Equal(MailboxGroupErrorCodes.MailboxQuotaOutOfRange, Assert.Single(result.Errors).ErrorMessage);
    }

    /// <summary>An empty domain identifier is refused.</summary>
    [Fact]
    public void An_empty_domain_identifier_is_refused()
    {
        Assert.False(_validator.Validate(Valid() with { MailDomainId = Guid.Empty }).IsValid);
    }

    /// <summary>Builds a command every rule accepts.</summary>
    /// <returns>The command.</returns>
    private static CreateMailboxCommand Valid()
    {
        return new CreateMailboxCommand(Guid.NewGuid(), "info", MailboxGroupSeeds.Password, 1024);
    }
}
