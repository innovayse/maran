using System.Globalization;
using Maran.Modules.Monitoring.Domain.Policies;
using Maran.Modules.Monitoring.Queries.ListPendingUpdateClassifications;
using Maran.Modules.Monitoring.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Monitoring.Tests.Queries.ListPendingUpdateClassifications;

/// <summary>The classification choices of the updates screen: every word, in rank order, named for the caller.</summary>
public sealed class ListPendingUpdateClassificationsQueryHandlerTests
{
    /// <summary>The choices are security, ordinary and unmeasured, in that order.</summary>
    [Fact]
    public async Task The_choices_are_security_ordinary_and_unmeasured_in_that_order()
    {
        var result = await Handler().HandleAsync(new ListPendingUpdateClassificationsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["security", "ordinary", "unmeasured"], result.Value.Select(choice =>
        {
            return choice.Value;
        }));
        Assert.Equal(PendingUpdateClassificationPolicy.All, result.Value.Select(choice =>
        {
            return choice.Value;
        }));
    }

    /// <summary>Every choice has a name of its own rather than its resource key.</summary>
    [Fact]
    public async Task Every_choice_has_a_name_of_its_own_rather_than_its_key()
    {
        var result = await Handler().HandleAsync(new ListPendingUpdateClassificationsQuery(), CancellationToken.None);

        Assert.All(result.Value, choice =>
        {
            Assert.False(string.IsNullOrWhiteSpace(choice.Name));
            Assert.DoesNotContain("PendingUpdateClassification", choice.Name, StringComparison.Ordinal);
        });
    }

    /// <summary>The names follow the caller's language.</summary>
    [Fact]
    public async Task The_names_follow_the_callers_language()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru");

            var result = await Handler().HandleAsync(
                new ListPendingUpdateClassificationsQuery(),
                CancellationToken.None);

            Assert.Equal("Безопасность", result.Value[0].Name);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>Builds the handler over the resx-backed localizer the module uses in production.</summary>
    /// <returns>The handler.</returns>
    private static ListPendingUpdateClassificationsQueryHandler Handler()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);

        return new ListPendingUpdateClassificationsQueryHandler(new StringLocalizer<DisplayNames>(factory));
    }
}
