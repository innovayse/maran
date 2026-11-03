using System.Globalization;
using Maran.Modules.Tasks.Domain.Enums;
using Maran.Modules.Tasks.Queries.ListTaskStatusChoices;
using Maran.Modules.Tasks.Tests.TestSupport;

namespace Maran.Modules.Tasks.Tests.Queries.ListTaskStatusChoices;

/// <summary>What the tasks list's status filter is offered: every status once, named in the caller's language.</summary>
public sealed class ListTaskStatusChoicesQueryHandlerTests
{
    /// <summary>The filter is offered every member of the enum exactly once, in declaration order.</summary>
    [Fact]
    public async Task Every_status_is_offered_once_in_declaration_order()
    {
        var handler = new ListTaskStatusChoicesQueryHandler(TasksTestContext.StatusNames());

        var result = await handler.HandleAsync(new ListTaskStatusChoicesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            Enum.GetValues<PanelTaskStatus>(),
            result.Value.Select(choice =>
            {
                return choice.Status;
            }));
    }

    /// <summary>Each status carries a real name in all three languages, never the resource key.</summary>
    /// <param name="culture">The request culture to name the statuses in.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public async Task Every_status_has_a_name_in_each_language(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var handler = new ListTaskStatusChoicesQueryHandler(TasksTestContext.StatusNames());

            var result = await handler.HandleAsync(new ListTaskStatusChoicesQuery(), CancellationToken.None);

            foreach (var choice in result.Value)
            {
                Assert.False(string.IsNullOrWhiteSpace(choice.StatusName));
                Assert.DoesNotContain("TaskStatus", choice.StatusName, StringComparison.Ordinal);
            }

            Assert.Equal(
                result.Value.Count,
                result.Value.Select(choice =>
                {
                    return choice.StatusName;
                }).Distinct().Count());
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
