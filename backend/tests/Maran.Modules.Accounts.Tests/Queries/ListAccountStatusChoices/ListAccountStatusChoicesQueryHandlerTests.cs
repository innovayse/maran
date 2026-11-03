using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Queries.ListAccountStatusChoices;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Queries.ListAccountStatusChoices;

/// <summary>Behavioral contract of <see cref="ListAccountStatusChoicesQueryHandler"/>.</summary>
public sealed class ListAccountStatusChoicesQueryHandlerTests
{
    /// <summary>It lists every <see cref="AccountStatus"/> member in declaration order, each with its resolved name.</summary>
    [Fact]
    public async Task It_lists_every_account_status_in_order_with_its_name()
    {
        var names = new StubDisplayNames();
        foreach (var status in Enum.GetValues<AccountStatus>())
        {
            names = names.With($"AccountStatus{status}", $"name of {status}");
        }

        var result = await new ListAccountStatusChoicesQueryHandler(names)
            .HandleAsync(new ListAccountStatusChoicesQuery());

        Assert.True(result.IsSuccess);
        Assert.Equal(Enum.GetValues<AccountStatus>(), result.Value!.Select(row =>
        {
            return row.Status;
        }));
        Assert.Equal("name of Suspended", result.Value!.Single(row =>
        {
            return row.Status == AccountStatus.Suspended;
        }).StatusName);
    }

    /// <summary>A state the double has no entry for reads as its key, which is how a missing translation shows.</summary>
    [Fact]
    public async Task A_status_without_a_resource_entry_reads_as_its_key()
    {
        var result = await new ListAccountStatusChoicesQueryHandler(new StubDisplayNames())
            .HandleAsync(new ListAccountStatusChoicesQuery());

        Assert.Equal("AccountStatusActive", result.Value![0].StatusName);
    }
}
