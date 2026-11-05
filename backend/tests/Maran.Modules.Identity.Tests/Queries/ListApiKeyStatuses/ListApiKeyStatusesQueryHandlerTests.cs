using System.Globalization;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.ListApiKeys;
using Maran.Modules.Identity.Queries.ListApiKeyStatuses;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;

namespace Maran.Modules.Identity.Tests.Queries.ListApiKeyStatuses;

/// <summary>Behavioural contract of <see cref="ListApiKeyStatusesQueryHandler"/>.</summary>
public sealed class ListApiKeyStatusesQueryHandlerTests
{
    /// <summary>Builds the handler under test over the real resource tables.</summary>
    private static ListApiKeyStatusesQueryHandler NewHandler()
    {
        return new ListApiKeyStatusesQueryHandler(new ApiKeyStatusDisplayNames(IdentityTestContext.DisplayNameResources()));
    }

    /// <summary>Runs the query under a UI culture and restores the previous one.</summary>
    /// <param name="culture">The culture name.</param>
    /// <returns>The choices the query answered with.</returns>
    private static async Task<IReadOnlyList<ApiKeyChoiceDto>> ChoicesAsync(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var result = await NewHandler().HandleAsync(new ListApiKeyStatusesQuery(), CancellationToken.None);
            return result.Value;
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>The choices are active, revoked and expired in that order, each with a readable name.</summary>
    [Fact]
    public async Task The_choices_are_active_revoked_and_expired_in_that_order_each_with_a_readable_name()
    {
        var choices = await ChoicesAsync("en");

        Assert.Equal(new[] { "active", "revoked", "expired" }, choices.Select(choice => { return choice.Value; }));
        Assert.Equal(ApiKeyStatusFilters.All, choices.Select(choice => { return choice.Value; }));
        Assert.All(choices, choice =>
        {
            Assert.False(string.IsNullOrWhiteSpace(choice.Name));
            Assert.NotEqual(choice.Value, choice.Name);
        });
    }

    /// <summary>Every status is named differently in russian and in english, so none is a silent fallback.</summary>
    [Fact]
    public async Task Every_status_is_named_differently_in_russian_and_in_english()
    {
        var english = await ChoicesAsync("en");
        var russian = await ChoicesAsync("ru");

        for (var i = 0; i < english.Count; i++)
        {
            Assert.NotEqual(english[i].Name, russian[i].Name);
        }
    }

    /// <summary>Every status is named differently in armenian and in english.</summary>
    [Fact]
    public async Task Every_status_is_named_differently_in_armenian_and_in_english()
    {
        var english = await ChoicesAsync("en");
        var armenian = await ChoicesAsync("hy");

        for (var i = 0; i < english.Count; i++)
        {
            Assert.NotEqual(english[i].Name, armenian[i].Name);
        }
    }

    /// <summary>A status with no display name falls back to the status itself rather than to a resource key.</summary>
    [Fact]
    public void A_status_with_no_display_name_falls_back_to_the_status_itself()
    {
        var names = new ApiKeyStatusDisplayNames(IdentityTestContext.DisplayNameResources());

        Assert.Equal("suspended", names.Of("suspended"));
    }
}
