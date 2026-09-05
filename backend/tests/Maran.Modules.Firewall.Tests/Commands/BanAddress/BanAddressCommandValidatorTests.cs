using Maran.Modules.Firewall.Commands.BanAddress;

namespace Maran.Modules.Firewall.Tests.Commands.BanAddress;

/// <summary>What a ban request has to look like before a handler ever sees it.</summary>
public sealed class BanAddressCommandValidatorTests
{
    /// <summary>A duration of zero minutes is refused because zero means permanent on the wire.</summary>
    [Fact]
    public void A_duration_of_zero_minutes_is_refused_because_zero_means_permanent_on_the_wire()
    {
        // A well-formed request for "no time at all" would arrive at the agent as a ban that never
        // ends. Permanent has to be asked for by sending no duration.
        var result = Validate(0);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == "BanDurationInvalid";
        });
    }

    /// <summary>A negative duration is refused.</summary>
    [Fact]
    public void A_negative_duration_is_refused()
    {
        Assert.False(Validate(-5).IsValid);
    }

    /// <summary>A duration longer than a year is refused.</summary>
    [Fact]
    public void A_duration_longer_than_a_year_is_refused()
    {
        // Past a year "temporary" has stopped meaning anything, and an administrator who wants
        // longer wants a permanent ban — a decision the journal should record as what it is.
        Assert.False(Validate(525_601).IsValid);
    }

    /// <summary>An absent duration is accepted and means permanent.</summary>
    [Fact]
    public void An_absent_duration_is_accepted_and_means_permanent()
    {
        Assert.True(Validate(null).IsValid);
    }

    /// <summary>An ordinary duration is accepted.</summary>
    [Fact]
    public void An_ordinary_duration_is_accepted()
    {
        Assert.True(Validate(60).IsValid);
    }

    /// <summary>An empty address is refused.</summary>
    [Fact]
    public void An_empty_address_is_refused()
    {
        Assert.False(new BanAddressCommandValidator()
            .Validate(new BanAddressCommand(string.Empty, 60, "198.51.100.1", "curl")).IsValid);
    }

    /// <summary>The validator does not judge the form of an address.</summary>
    [Fact]
    public void The_validator_does_not_judge_the_form_of_an_address()
    {
        // Deliberate. The address is parsed once, by IpAddressNormalizer in the handler, which is
        // also what maps ::ffff:a.b.c.d onto plain IPv4. A second format rule here would mask that
        // one: removing the normalisation would leave this validator still passing everything, so
        // nothing would go red.
        Assert.True(new BanAddressCommandValidator()
            .Validate(new BanAddressCommand("not-an-address", 60, "198.51.100.1", "curl")).IsValid);
    }

    /// <summary>Runs the validator over one candidate ban.</summary>
    /// <param name="durationMinutes">The duration asked for, or null for permanent.</param>
    private static FluentValidation.Results.ValidationResult Validate(int? durationMinutes)
    {
        return new BanAddressCommandValidator().Validate(
            new BanAddressCommand("203.0.113.7", durationMinutes, "198.51.100.1", "curl"));
    }
}
