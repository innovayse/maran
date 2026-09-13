using FluentValidation;

namespace Maran.Modules.Monitoring.Queries.GetMetricsChart;

/// <summary>Refuses a chart request whose range is not one of the two the panel offers.</summary>
/// <remarks>
/// A query-string value binds to an enum by NUMBER as readily as by name, so <c>?range=99</c>
/// produces a perfectly typed <c>ChartRange</c> that matches no member. Without this rule that value
/// would reach <c>ChartWindow.For</c> and silently fall to its default arm — the caller would be
/// answered with a day's chart while believing they had asked for something else, which is worse
/// than a refusal because nothing about the response says so.
/// </remarks>
public sealed class GetMetricsChartQueryValidator : AbstractValidator<GetMetricsChartQuery>
{
    /// <summary>Declares the rule.</summary>
    public GetMetricsChartQueryValidator()
    {
        RuleFor(query => query.Range).IsInEnum();
    }
}
