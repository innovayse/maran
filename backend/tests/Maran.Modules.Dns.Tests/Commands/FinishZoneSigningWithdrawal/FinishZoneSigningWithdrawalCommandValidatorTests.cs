using Maran.Modules.Dns.Commands.FinishZoneSigningWithdrawal;

namespace Maran.Modules.Dns.Tests.Commands.FinishZoneSigningWithdrawal;

/// <summary>The boundary refuses an ending of the withdrawal unless the caller states the DS record was removed.</summary>
public sealed class FinishZoneSigningWithdrawalCommandValidatorTests
{
    /// <summary>A statement that is not true, or a body that left it out (which reads as false), fails with the code.</summary>
    [Fact]
    public void A_missing_or_false_statement_fails_with_its_code()
    {
        var result = new FinishZoneSigningWithdrawalCommandValidator().Validate(
            new FinishZoneSigningWithdrawalCommand(false));

        Assert.Equal("ZoneSigningDsRemovalNotConfirmed", result.Errors.Single().ErrorMessage);
    }

    /// <summary>Inverse control: a true statement passes.</summary>
    [Fact]
    public void A_true_statement_passes()
    {
        var result = new FinishZoneSigningWithdrawalCommandValidator().Validate(
            new FinishZoneSigningWithdrawalCommand(true));

        Assert.True(result.IsValid);
    }
}
