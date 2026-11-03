using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Queries.ListResellerChoices;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Queries.ListResellerChoices;

/// <summary>
/// Behavioral contract of <see cref="ListResellerChoicesQueryHandler"/>, run on the EF Core InMemory
/// provider.
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: the search, an <c>ILIKE</c> that only PostgreSQL translates; the integration
/// suite covers it against a real server.
/// </remarks>
public sealed class ListResellerChoicesQueryHandlerTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Runs the handler over this test's context.</summary>
    /// <param name="query">The list request.</param>
    private Task<Result<PagedResult<ResellerChoiceDto>>> RunAsync(ListResellerChoicesQuery query)
    {
        return new ListResellerChoicesQueryHandler(_dbContext).HandleAsync(query, CancellationToken.None);
    }

    /// <summary>Stores a reseller for each name.</summary>
    /// <param name="names">The reseller names.</param>
    private async Task StoreAsync(params string[] names)
    {
        _dbContext.Resellers.AddRange(names.Select(name =>
        {
            return ResellerTestData.Reseller(name);
        }));
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>An empty store answers an empty page.</summary>
    [Fact]
    public async Task An_empty_store_answers_an_empty_page()
    {
        var result = await RunAsync(new ListResellerChoicesQuery());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.Total);
    }

    /// <summary>A choice is only an id and a name, ordered by name by default.</summary>
    [Fact]
    public async Task A_choice_is_an_id_and_a_name_ordered_by_name_by_default()
    {
        await StoreAsync("charlie", "alpha", "bravo");

        var result = await RunAsync(new ListResellerChoicesQuery());

        Assert.Equal(["alpha", "bravo", "charlie"], result.Value.Items.Select(dto => { return dto.Name; }));
        var stored = _dbContext.Resellers.Single(reseller => reseller.Name == "alpha");
        Assert.Equal(stored.Id, result.Value.Items[0].Id);
    }

    /// <summary>A descending name sort reverses the order.</summary>
    [Fact]
    public async Task A_descending_name_sort_reverses_the_order()
    {
        await StoreAsync("charlie", "alpha", "bravo");

        var result = await RunAsync(new ListResellerChoicesQuery(Sort: SortRequest.Parse("-name")));

        Assert.Equal(["charlie", "bravo", "alpha"], result.Value.Items.Select(dto => { return dto.Name; }));
    }

    /// <summary>Paging holds the rows after the first page and the total counts every reseller.</summary>
    [Fact]
    public async Task Paging_holds_the_rows_after_the_first_page_and_the_total_counts_every_reseller()
    {
        await StoreAsync(Enumerable.Range(0, 12).Select(index =>
        {
            return $"reseller{index:D2}";
        }).ToArray());

        var result = await RunAsync(new ListResellerChoicesQuery(Page: 2, PageSize: 10));

        Assert.Equal(12, result.Value.Total);
        Assert.Equal(["reseller10", "reseller11"], result.Value.Items.Select(dto => { return dto.Name; }));
    }
}
