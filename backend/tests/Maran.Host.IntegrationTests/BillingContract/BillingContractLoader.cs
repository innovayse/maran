using System.Text.Json;

namespace Maran.Host.IntegrationTests.BillingContract;

/// <summary>Reads the billing contract the documentation holds, from the copy the test build puts beside the tests.</summary>
public static class BillingContractLoader
{
    /// <summary>The folder the project's <c>Content</c> item puts <c>docs/api/billing-contract/lifecycle.json</c> in.</summary>
    private const string Folder = "BillingContract";

    /// <summary>The contract's file name.</summary>
    private const string FileName = "lifecycle.json";

    /// <summary>The reader's options: camelCase names, as the file spells them.</summary>
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Reads and parses the contract, and refuses a file that cannot be replayed at all.</summary>
    /// <returns>The contract.</returns>
    public static BillingContractFile Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, Folder, FileName);

        Assert.True(
            File.Exists(path),
            $"The billing contract is not at {path}. The test project copies docs/api/billing-contract/lifecycle.json "
                + "there with a Content item; the item or the file has moved.");

        var contract = JsonSerializer.Deserialize<BillingContractFile>(File.ReadAllText(path), Options);

        Assert.NotNull(contract);
        Assert.NotEmpty(contract.Interactions);

        var duplicated = contract.Interactions
            .GroupBy(interaction => { return interaction.Name; }, StringComparer.Ordinal)
            .Where(group => { return group.Count() > 1; })
            .Select(group => { return group.Key; })
            .ToList();
        Assert.True(duplicated.Count == 0, $"Interaction names must be unique; repeated: {string.Join(", ", duplicated)}.");

        return contract;
    }
}
