using System.Globalization;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Queries.ListAuditOutcomeChoices;
using Maran.Modules.Identity.Tests.TestSupport;

namespace Maran.Modules.Identity.Tests.Queries.ListAuditOutcomeChoices;

/// <summary>What the journal's outcome filter is offered: every outcome once, named in the caller's language.</summary>
public sealed class ListAuditOutcomeChoicesQueryHandlerTests
{
    /// <summary>The filter is offered every member of the enum exactly once, in declaration order.</summary>
    [Fact]
    public async Task Every_outcome_is_offered_once_in_declaration_order()
    {
        var handler = new ListAuditOutcomeChoicesQueryHandler(IdentityTestContext.OutcomeNames());

        var result = await handler.HandleAsync(new ListAuditOutcomeChoicesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Enum.GetValues<AuditOutcome>(),
            result.Value.Select(choice =>
            {
                return choice.Outcome;
            }));
    }

    /// <summary>Each outcome carries a real name in all three languages, never the resource key.</summary>
    /// <param name="culture">The request culture to name the outcomes in.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public async Task Every_outcome_has_a_name_in_each_language(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var handler = new ListAuditOutcomeChoicesQueryHandler(IdentityTestContext.OutcomeNames());

            var result = await handler.HandleAsync(new ListAuditOutcomeChoicesQuery(), CancellationToken.None);

            foreach (var choice in result.Value)
            {
                Assert.False(string.IsNullOrWhiteSpace(choice.OutcomeName));
                Assert.DoesNotContain("AuditOutcome", choice.OutcomeName, StringComparison.Ordinal);
            }

            Assert.Equal(
                result.Value.Count,
                result.Value.Select(choice =>
                {
                    return choice.OutcomeName;
                }).Distinct().Count());
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
