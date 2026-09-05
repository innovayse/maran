using FluentValidation.TestHelper;
using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Queries.GetMetricsChart;

namespace Maran.Modules.Monitoring.Tests.Queries.GetMetricsChart;

/// <summary>The chart request's one parameter, and why an unrecognised value must be refused.</summary>
public sealed class GetMetricsChartQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly GetMetricsChartQueryValidator _validator = new();

    /// <summary>Both offered ranges are accepted.</summary>
    [Theory]
    [InlineData(ChartRange.LastDay)]
    [InlineData(ChartRange.LastWeek)]
    public void Both_offered_ranges_are_accepted(ChartRange range)
    {
        _validator.TestValidate(new GetMetricsChartQuery(range)).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>A range outside the offered set is refused rather than silently answered with a day.</summary>
    /// <remarks>
    /// A query-string value binds to an enum by number as readily as by name, so <c>?range=99</c>
    /// produces a typed value matching no member. Without this rule it would fall to
    /// <c>ChartWindow.For</c>'s default arm and the caller would be answered with a chart they did not
    /// ask for, with nothing in the response saying so.
    /// </remarks>
    [Fact]
    public void A_range_outside_the_offered_set_is_refused()
    {
        _validator.TestValidate(new GetMetricsChartQuery((ChartRange)99))
            .ShouldHaveValidationErrorFor(query => query.Range);
    }
}
