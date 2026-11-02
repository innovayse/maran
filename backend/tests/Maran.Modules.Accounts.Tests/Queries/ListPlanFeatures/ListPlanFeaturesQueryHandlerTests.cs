using Maran.Modules.Accounts.Queries.ListPlanFeatures;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;

namespace Maran.Modules.Accounts.Tests.Queries.ListPlanFeatures;

/// <summary>Behavioral contract of <see cref="ListPlanFeaturesQueryHandler"/>.</summary>
public sealed class ListPlanFeaturesQueryHandlerTests
{
    /// <summary>It lists every feature, in declaration order, and neither the empty set nor the full one.</summary>
    [Fact]
    public async Task It_lists_every_feature_in_declaration_order_and_no_set()
    {
        var names = new StubDisplayNames();
        foreach (var feature in AccountFeature.All.Members())
        {
            names = names.With($"AccountFeature{feature}", $"name of {feature}");
        }

        var result = await new ListPlanFeaturesQueryHandler(names).HandleAsync(new ListPlanFeaturesQuery());

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountFeature.All.Members(), result.Value!.Select(row =>
        {
            return row.Feature;
        }));
        Assert.Equal("name of FileTransfer", result.Value!.Single(row =>
        {
            return row.Feature == AccountFeature.FileTransfer;
        }).DisplayName);
        Assert.DoesNotContain(result.Value!, row =>
        {
            return row.Feature is AccountFeature.None or AccountFeature.All;
        });
    }
}
