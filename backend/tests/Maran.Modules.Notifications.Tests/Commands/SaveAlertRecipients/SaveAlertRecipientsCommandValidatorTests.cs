using FluentValidation.TestHelper;
using Maran.Modules.Notifications.Commands.SaveAlertRecipients;

namespace Maran.Modules.Notifications.Tests.Commands.SaveAlertRecipients;

/// <summary>What the panel refuses to save as the alert recipient list, and what it accepts.</summary>
public sealed class SaveAlertRecipientsCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly SaveAlertRecipientsCommandValidator _validator = new();

    /// <summary>A short list of valid addresses is accepted.</summary>
    [Fact]
    public void A_list_of_valid_addresses_is_accepted()
    {
        _validator.TestValidate(Command("a@example.com", "b@example.com")).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>An empty list is accepted: an operator may want no alert mail at all.</summary>
    [Fact]
    public void An_empty_list_is_accepted()
    {
        _validator.TestValidate(Command()).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>Ten addresses are accepted and eleven are refused, so the bound is exact.</summary>
    [Fact]
    public void The_limit_is_ten_addresses()
    {
        var ten = Enumerable.Range(0, 10).Select(index => { return $"user{index}@example.com"; }).ToArray();
        var eleven = ten.Append("extra@example.com").ToArray();

        _validator.TestValidate(Command(ten)).ShouldNotHaveAnyValidationErrors();
        _validator.TestValidate(Command(eleven)).ShouldHaveValidationErrorFor(command => command.Addresses);
    }

    /// <summary>An address that is not one bare mailbox is refused, a newline included.</summary>
    [Theory]
    [InlineData("not-an-address")]
    [InlineData("ops@example.com\r\nBcc: attacker@example.net")]
    [InlineData("")]
    public void An_address_that_is_not_one_bare_mailbox_is_refused(string address)
    {
        _validator.TestValidate(Command(address)).ShouldHaveValidationErrors();
    }

    /// <summary>The same address twice is refused, whatever its case.</summary>
    [Fact]
    public void The_same_address_twice_is_refused_whatever_its_case()
    {
        _validator.TestValidate(Command("Ops@Example.com", "ops@example.com"))
            .ShouldHaveValidationErrorFor(command => command.Addresses);
    }

    /// <summary>Builds a command carrying the given addresses.</summary>
    /// <param name="addresses">The addresses.</param>
    /// <returns>The command.</returns>
    private static SaveAlertRecipientsCommand Command(params string[] addresses)
    {
        return new SaveAlertRecipientsCommand(addresses);
    }
}
