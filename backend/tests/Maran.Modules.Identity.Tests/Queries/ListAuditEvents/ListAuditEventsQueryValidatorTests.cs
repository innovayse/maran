using FluentValidation.TestHelper;
using Maran.Modules.Identity.Queries.ListAuditEvents;

namespace Maran.Modules.Identity.Tests.Queries.ListAuditEvents;
/// <summary>Behavioural contract of list audit events query validator.</summary>

public sealed class ListAuditEventsQueryValidatorTests
{
    private readonly ListAuditEventsQueryValidator _validator = new();

    /// <summary>A limit outside the allowed range is rejected.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(501)]
    public void A_limit_outside_the_allowed_range_is_rejected(int limit)
    {
        _validator.TestValidate(new ListAuditEventsQuery(limit)).ShouldHaveValidationErrorFor(query => query.Limit);
    }

    /// <summary>A limit inside the allowed range is accepted.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    public void A_limit_inside_the_allowed_range_is_accepted(int limit)
    {
        _validator.TestValidate(new ListAuditEventsQuery(limit)).ShouldNotHaveValidationErrorFor(query => query.Limit);
    }
}
