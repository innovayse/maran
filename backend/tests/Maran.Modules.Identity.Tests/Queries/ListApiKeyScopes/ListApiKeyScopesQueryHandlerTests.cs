using System.Globalization;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Queries.ListApiKeyScopes;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Queries.ListApiKeyScopes;

/// <summary>Behavioural contract of <see cref="ListApiKeyScopesQueryHandler"/>.</summary>
public sealed class ListApiKeyScopesQueryHandlerTests
{
    /// <summary>Builds the handler under test over the real resource tables.</summary>
    private static ListApiKeyScopesQueryHandler NewHandler()
    {
        return new ListApiKeyScopesQueryHandler(new ApiKeyScopeDisplayNames(IdentityTestContext.DisplayNameResources()));
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
            var result = await NewHandler().HandleAsync(new ListApiKeyScopesQuery(), CancellationToken.None);
            return result.Value;
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>The choices are exactly the scope vocabulary, in its order, each with a readable name.</summary>
    [Fact]
    public async Task The_choices_are_exactly_the_scope_vocabulary_each_with_a_readable_name()
    {
        var choices = await ChoicesAsync("en");

        Assert.Equal(ApiKeyScopes.All, choices.Select(choice => { return choice.Value; }));
        Assert.All(choices, choice =>
        {
            Assert.False(string.IsNullOrWhiteSpace(choice.Name));
            Assert.NotEqual(choice.Value, choice.Name);
        });
    }

    /// <summary>Every scope is named differently in russian and in english, so none is a silent fallback.</summary>
    [Fact]
    public async Task Every_scope_is_named_differently_in_russian_and_in_english()
    {
        var english = await ChoicesAsync("en");
        var russian = await ChoicesAsync("ru");

        for (var i = 0; i < english.Count; i++)
        {
            Assert.NotEqual(english[i].Name, russian[i].Name);
        }
    }

    /// <summary>Every scope is named differently in armenian and in english.</summary>
    [Fact]
    public async Task Every_scope_is_named_differently_in_armenian_and_in_english()
    {
        var english = await ChoicesAsync("en");
        var armenian = await ChoicesAsync("hy");

        for (var i = 0; i < english.Count; i++)
        {
            Assert.NotEqual(english[i].Name, armenian[i].Name);
        }
    }

    /// <summary>A scope with no display name falls back to the scope itself rather than to a resource key.</summary>
    [Fact]
    public void A_scope_with_no_display_name_falls_back_to_the_scope_itself()
    {
        var names = new ApiKeyScopeDisplayNames(IdentityTestContext.DisplayNameResources());

        Assert.Equal("future.write", names.Of("future.write"));
    }
}
