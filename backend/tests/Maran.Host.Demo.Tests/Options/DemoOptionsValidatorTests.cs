using Maran.Host.Demo.Options;

namespace Maran.Host.Demo.Tests.Options;

/// <summary>The abuse contact is required and well-formed, and the budgets' relations hold, at start-up.</summary>
public sealed class DemoOptionsValidatorTests
{
    /// <summary>A role mailbox, as the documented placeholder in <c>demo/panel.env.example</c> spells one.</summary>
    private const string Contact = "abuse@innovayse.com";

    /// <summary>The defaults are valid once the required contact is set.</summary>
    [Fact]
    public void The_defaults_with_a_contact_are_valid()
    {
        Assert.True(new DemoOptionsValidator().Validate(null, new DemoOptions { AbuseContact = Contact }).Succeeded);
    }

    /// <summary>The refusing control: no contact is configured, so the demo does not start.</summary>
    [Fact]
    public void A_missing_abuse_contact_is_refused()
    {
        var result = new DemoOptionsValidator().Validate(null, new DemoOptions());

        Assert.True(result.Failed);
        Assert.Contains("Demo:AbuseContact", result.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>A contact that is neither one bare address nor an https URL is refused.</summary>
    /// <param name="contact">The configured value.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not an address")]
    [InlineData("Abuse Desk <abuse@innovayse.com>")]
    [InlineData("abuse@innovayse.com ")]
    [InlineData("abuse@innovayse.com\nBcc: x@y.z")]
    [InlineData("a@b.c, d@e.f")]
    [InlineData("http://innovayse.com/abuse")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:abuse@innovayse.com")]
    [InlineData("https://")]
    [InlineData("https://user:pw@innovayse.com/abuse")]
    [InlineData("https://innovayse.com/ab use")]
    public void A_malformed_abuse_contact_is_refused(string contact)
    {
        Assert.True(new DemoOptionsValidator().Validate(null, new DemoOptions { AbuseContact = contact }).Failed);
    }

    /// <summary>The accepting control: an address and an https URL are both contacts.</summary>
    /// <param name="contact">The configured value.</param>
    [Theory]
    [InlineData("abuse@innovayse.com")]
    [InlineData("report.abuse+demo@innovayse.com")]
    [InlineData("https://innovayse.com/abuse")]
    [InlineData("https://innovayse.com/abuse?topic=demo#form")]
    public void An_address_or_an_https_url_is_a_valid_abuse_contact(string contact)
    {
        Assert.True(new DemoOptionsValidator().Validate(null, new DemoOptions { AbuseContact = contact }).Succeeded);
    }

    /// <summary>One visitor may not be granted more than the whole demo has.</summary>
    [Fact]
    public void A_visitor_budget_above_the_global_budget_is_refused()
    {
        var options = new DemoOptions
        {
            AbuseContact = Contact,
            GlobalMutationBudget = 10,
            VisitorMutationsPerWindow = 11,
        };

        var result = new DemoOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
    }

    /// <summary>A visitor budget equal to the global one is allowed.</summary>
    [Fact]
    public void A_visitor_budget_equal_to_the_global_budget_is_valid()
    {
        var options = new DemoOptions
        {
            AbuseContact = Contact,
            GlobalMutationBudget = 10,
            VisitorMutationsPerWindow = 10,
        };

        Assert.True(new DemoOptionsValidator().Validate(null, options).Succeeded);
    }

    /// <summary>One visitor may not be granted more sign-ins than the whole demo accepts.</summary>
    [Fact]
    public void A_visitor_sign_in_allowance_above_the_global_one_is_refused()
    {
        var options = new DemoOptions
        {
            AbuseContact = Contact,
            GlobalSignInsPerWindow = 10,
            VisitorSignInsPerWindow = 11,
        };

        Assert.True(new DemoOptionsValidator().Validate(null, options).Failed);
    }
}
