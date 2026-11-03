using Maran.Modules.Accounts.Queries.ListOwnerLanguages;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Constants;

namespace Maran.Modules.Accounts.Tests.Queries.ListOwnerLanguages;

/// <summary>Behavioral contract of <see cref="ListOwnerLanguagesQueryHandler"/>.</summary>
public sealed class ListOwnerLanguagesQueryHandlerTests
{
    /// <summary>It lists exactly the panel's supported languages, in order, each with its resolved name.</summary>
    [Fact]
    public async Task It_lists_every_supported_language_in_order_with_its_name()
    {
        var names = new StubDisplayNames();
        foreach (var language in PanelLanguages.Supported)
        {
            names = names.With($"OwnerLanguage{language.ToUpperInvariant()}", $"name of {language}");
        }

        var result = await new ListOwnerLanguagesQueryHandler(names).HandleAsync(new ListOwnerLanguagesQuery());

        Assert.True(result.IsSuccess);
        Assert.Equal(PanelLanguages.Supported, result.Value!.Select(row =>
        {
            return row.Language;
        }));
        Assert.Equal("name of hy", result.Value!.Single(row =>
        {
            return row.Language == PanelLanguages.Armenian;
        }).LanguageDisplayName);
    }
}
