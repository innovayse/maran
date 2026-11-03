using Maran.Modules.Accounts.Commands.ChangeResellerEnvelope;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Commands.ChangeResellerEnvelope;

/// <summary>
/// Behavioral contract of <see cref="ChangeResellerEnvelopeCommandValidator"/>: the envelope is
/// required and held to the same bounds as at creation.
/// </summary>
public sealed class ChangeResellerEnvelopeCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly ChangeResellerEnvelopeCommandValidator _validator = new();

    /// <summary>A valid envelope is accepted: the inverse control for the refusals below.</summary>
    [Fact]
    public void A_valid_envelope_is_accepted()
    {
        var command = new ChangeResellerEnvelopeCommand(ResellerEnvelopeDtoTestData.Valid());

        Assert.True(_validator.Validate(command).IsValid);
    }

    /// <summary>An envelope left out is refused with the envelope code.</summary>
    [Fact]
    public void An_envelope_left_out_is_refused_with_the_envelope_code()
    {
        var result = _validator.Validate(new ChangeResellerEnvelopeCommand(null!));

        Assert.Contains(result.Errors, failure =>
        {
            return failure.ErrorMessage == "ResellerEnvelopeInvalid";
        });
    }

    /// <summary>An envelope with a figure out of bounds is refused with the envelope code.</summary>
    [Fact]
    public void An_envelope_with_a_figure_out_of_bounds_is_refused_with_the_envelope_code()
    {
        var command = new ChangeResellerEnvelopeCommand(ResellerEnvelopeDtoTestData.Valid() with { DiskPoolMb = -1 });

        var result = _validator.Validate(command);

        Assert.Equal("ResellerEnvelopeInvalid", Assert.Single(result.Errors).ErrorMessage);
    }
}
