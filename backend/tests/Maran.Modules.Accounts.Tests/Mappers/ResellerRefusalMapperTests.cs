using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Models;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Mappers;

/// <summary>
/// Behavioral contract of <see cref="ResellerRefusalMapper"/>: a refusal becomes the failed result of
/// the refused operation, carrying its facts when it has any and none when it has none.
/// </summary>
public sealed class ResellerRefusalMapperTests
{
    /// <summary>A refusal with no facts becomes a plain failure with its code and kind.</summary>
    [Fact]
    public void A_refusal_with_no_facts_becomes_a_plain_failure_with_its_code_and_kind()
    {
        var refusal = new ResellerRefusal(
            Error.Of("ResellerAccountLimitReached", ErrorType.Conflict));

        var result = ResellerRefusalMapper.ToFailure<string>(refusal);

        Assert.False(result.IsSuccess);
        Assert.Equal("ResellerAccountLimitReached", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Null(result.Extension);
    }

    /// <summary>A refusal that names figures carries them beside the code.</summary>
    [Fact]
    public void A_refusal_that_names_figures_carries_them_beside_the_code()
    {
        var facts = ProblemExtension.Of("envelope", new PlanEnvelopeViolationsDto(["diskQuotaMb"]));
        var refusal = new ResellerRefusal(
            Error.Of("ResellerPlanExceedsEnvelope", ErrorType.Validation), facts);

        var result = ResellerRefusalMapper.ToFailure<string>(refusal);

        Assert.Equal("ResellerPlanExceedsEnvelope", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Same(facts, result.Extension);
    }
}
