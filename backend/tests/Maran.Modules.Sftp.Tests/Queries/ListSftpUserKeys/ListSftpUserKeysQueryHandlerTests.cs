using Maran.Modules.Sftp.Common;
using Maran.Modules.Sftp.Domain.ValueObjects;
using Maran.Modules.Sftp.Persistence;
using Maran.Modules.Sftp.Queries.ListSftpUserKeys;
using Maran.Modules.Sftp.Tests.TestSupport;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sftp.Tests.Queries.ListSftpUserKeys;

/// <summary>
/// Where a key listing's rows come from, how they are ordered and paged, and what they leave out.
/// </summary>
/// <remarks>
/// <b>UNOBSERVED HERE: the search by label.</b> It is an <c>ILIKE</c> predicate, which the in-memory
/// provider these tests run on cannot translate; it is exercised against PostgreSQL by the
/// integration suite. Everything else the handler does is observed here.
/// </remarks>
public sealed class ListSftpUserKeysQueryHandlerTests
{
    /// <summary>A customer is listed the keys of their own login with the fields a screen shows.</summary>
    [Fact]
    public async Task A_customer_is_listed_the_keys_of_their_own_login()
    {
        var world = await SftpKeyTestWorld.CreateAsync();

        var result = await ListAsync(world, new ListSftpUserKeysQuery(SftpUserId: world.OwnLoginId));

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value.Items);
        Assert.Equal(world.OwnKeyId, row.Id);
        Assert.Equal("existing", row.Label);
        Assert.Equal("ssh-ed25519", row.Algorithm);
        Assert.Equal(SftpTestKeys.Ed25519Fingerprint, row.Fingerprint);
        Assert.Equal(SftpKeyTestWorld.Epoch, row.CreatedAt);
        Assert.Equal(1, result.Value.Total);
    }

    /// <summary>A listing never carries a key body: the row type has no member for it.</summary>
    [Fact]
    public void A_listing_never_carries_a_key_body()
    {
        Assert.DoesNotContain(
            typeof(SftpUserKeyDto).GetProperties(),
            property =>
            {
                return property.Name.Contains("PublicKey", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Contains("Body", StringComparison.OrdinalIgnoreCase);
            });
    }

    /// <summary>Another account's login answers not found, however many keys it holds.</summary>
    [Fact]
    public async Task Another_accounts_login_answers_not_found()
    {
        var world = await SftpKeyTestWorld.CreateAsync();

        var result = await ListAsync(world, new ListSftpUserKeysQuery(SftpUserId: world.StrangerLoginId));

        Assert.False(result.IsSuccess);
        Assert.Equal("SftpUserNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>A login id that names nothing answers exactly as another tenant's does.</summary>
    [Fact]
    public async Task A_login_id_that_names_nothing_answers_exactly_as_another_tenants_does()
    {
        var world = await SftpKeyTestWorld.CreateAsync();

        var result = await ListAsync(world, new ListSftpUserKeysQuery(SftpUserId: Guid.NewGuid()));

        Assert.Equal("SftpUserNotFound", result.Error!.Code);
    }

    /// <summary>An administrator can list any login's keys, which proves the 404 above comes from the filter.</summary>
    [Fact]
    public async Task An_administrator_can_list_any_logins_keys()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        using var context = world.AdminContext();

        var result = await new ListSftpUserKeysQueryHandler(context).HandleAsync(
            new ListSftpUserKeysQuery(SftpUserId: world.StrangerLoginId), CancellationToken.None);

        Assert.Equal("stranger", Assert.Single(result.Value.Items).Label);
    }

    /// <summary>Only the named login's keys are listed, never a sibling login's of the same account.</summary>
    [Fact]
    public async Task Only_the_named_logins_keys_are_listed()
    {
        var world = await SftpKeyTestWorld.CreateAsync();

        var result = await ListAsync(world, new ListSftpUserKeysQuery(SftpUserId: world.SiblingLoginId));

        Assert.Equal([world.SiblingKeyId], result.Value.Items.Select(row =>
        {
            return row.Id;
        }));
    }

    /// <summary>With no sort named, keys are listed oldest first.</summary>
    [Fact]
    public async Task With_no_sort_named_keys_are_listed_oldest_first()
    {
        var world = await SeededAsync(("bravo", 2), ("alpha", 1), ("charlie", 3));

        var result = await ListAsync(world, new ListSftpUserKeysQuery(SftpUserId: world.OwnLoginId));

        Assert.Equal(["existing", "alpha", "bravo", "charlie"], Labels(result));
    }

    /// <summary>A label sort orders by the label, either way.</summary>
    [Fact]
    public async Task A_label_sort_orders_by_the_label_either_way()
    {
        var world = await SeededAsync(("bravo", 2), ("alpha", 1), ("charlie", 3));

        var ascending = await ListAsync(
            world, new ListSftpUserKeysQuery(Sort: SortRequest.Parse("label"), SftpUserId: world.OwnLoginId));
        var descending = await ListAsync(
            world, new ListSftpUserKeysQuery(Sort: SortRequest.Parse("-label"), SftpUserId: world.OwnLoginId));

        Assert.Equal(["alpha", "bravo", "charlie", "existing"], Labels(ascending));
        Assert.Equal(["existing", "charlie", "bravo", "alpha"], Labels(descending));
    }

    /// <summary>A descending created-at sort lists the newest key first.</summary>
    [Fact]
    public async Task A_descending_created_at_sort_lists_the_newest_key_first()
    {
        var world = await SeededAsync(("bravo", 2), ("alpha", 1), ("charlie", 3));

        var result = await ListAsync(
            world, new ListSftpUserKeysQuery(Sort: SortRequest.Parse("-createdAt"), SftpUserId: world.OwnLoginId));

        Assert.Equal(["charlie", "bravo", "alpha", "existing"], Labels(result));
    }

    /// <summary>The second page holds the rows after the first, and the total counts every key.</summary>
    [Fact]
    public async Task The_second_page_holds_the_rows_after_the_first_and_the_total_counts_every_key()
    {
        var world = await SeededAsync([.. Enumerable.Range(1, 11).Select(day =>
        {
            return ($"key{day:D2}", day);
        })]);

        var result = await ListAsync(
            world, new ListSftpUserKeysQuery(Page: 2, PageSize: 10, SftpUserId: world.OwnLoginId));

        Assert.Equal(12, result.Value.Total);
        Assert.Equal(2, result.Value.Page);
        Assert.Equal(10, result.Value.PageSize);
        Assert.Equal(["key10", "key11"], Labels(result));
    }

    /// <summary>A page past the last answers empty with the true total and the page asked for.</summary>
    [Fact]
    public async Task A_page_past_the_last_answers_empty_with_the_true_total()
    {
        var world = await SftpKeyTestWorld.CreateAsync();

        var result = await ListAsync(
            world, new ListSftpUserKeysQuery(Page: 5, PageSize: 10, SftpUserId: world.OwnLoginId));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(1, result.Value.Total);
        Assert.Equal(5, result.Value.Page);
    }

    /// <summary>The listing does not call any agent: the handler cannot even be handed one.</summary>
    [Fact]
    public void The_listing_cannot_be_handed_the_agent()
    {
        var parameters = typeof(ListSftpUserKeysQueryHandler).GetConstructors().SelectMany(constructor =>
        {
            return constructor.GetParameters();
        });

        Assert.Equal([typeof(SftpDbContext)], parameters.Select(parameter =>
        {
            return parameter.ParameterType;
        }));
    }

    /// <summary>Seeds more keys into the caller's <c>deploy</c> login, registered the given days after the epoch.</summary>
    /// <param name="keys">Each key's label and its registration day.</param>
    private static async Task<SftpKeyTestWorld> SeededAsync(params (string Label, int Day)[] keys)
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        using var context = world.AdminContext();
        var login = await context.SftpUsers.Include(row => row.Keys).SingleAsync(row => row.Id == world.OwnLoginId);
        foreach (var (label, day) in keys)
        {
            var added = login.AddKey(
                Guid.NewGuid(),
                label,
                SftpPublicKey.Parse(SftpTestKeys.Distinct(day)).Value,
                SftpKeyTestWorld.Epoch.AddDays(day));
            Assert.True(added.IsSuccess);
        }

        await context.SaveChangesAsync();

        return world;
    }

    /// <summary>Runs the handler as the caller.</summary>
    /// <param name="world">The world.</param>
    /// <param name="query">The list request.</param>
    private static async Task<Result<PagedResult<SftpUserKeyDto>>> ListAsync(
        SftpKeyTestWorld world,
        ListSftpUserKeysQuery query)
    {
        using var context = world.CallerContext();

        return await new ListSftpUserKeysQueryHandler(context).HandleAsync(query, CancellationToken.None);
    }

    /// <summary>The labels on a page, in order.</summary>
    /// <param name="result">The handler's answer.</param>
    private static List<string> Labels(Result<PagedResult<SftpUserKeyDto>> result)
    {
        return result.Value.Items.Select(row =>
        {
            return row.Label;
        }).ToList();
    }
}
