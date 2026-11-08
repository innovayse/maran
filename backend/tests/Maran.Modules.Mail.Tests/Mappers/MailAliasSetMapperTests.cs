using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Models;
using Maran.Modules.Mail.Tests.TestSupport;

namespace Maran.Modules.Mail.Tests.Mappers;

/// <summary>
/// The complete alias set the agent replaces a domain with: restated from the rows, with one address added,
/// replaced or left out, and the catch-all separated from the rest.
/// </summary>
public sealed class MailAliasSetMapperTests
{
    /// <summary>The instant every row is created at.</summary>
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The domain the rows live in.</summary>
    private static readonly Guid DomainId = new("0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");

    /// <summary>Rows are restated in local-part order with their targets in the order given, and the catch-all is separated.</summary>
    [Fact]
    public void Rows_are_restated_in_local_part_order_and_the_catch_all_is_separated()
    {
        var set = MailAliasSetMapper.From(
        [
            Row("sales", MailAliasKind.Alias, "z@example.com", "a@example.com"),
            Row("*", MailAliasKind.CatchAll, "boss@example.com"),
            Row("info", MailAliasKind.Alias, "bob@example.com"),
        ]);

        Assert.Equal(["info", "sales"], LocalParts(set));
        Assert.Equal(["z@example.com", "a@example.com"], set.Aliases[1].Targets);
        Assert.Equal(["boss@example.com"], set.CatchAllTargets);
    }

    /// <summary>A domain with no rows is an empty set and no catch-all.</summary>
    [Fact]
    public void No_rows_make_an_empty_set_and_no_catch_all()
    {
        var set = MailAliasSetMapper.From([]);

        Assert.Empty(set.Aliases);
        Assert.Null(set.CatchAllTargets);
    }

    /// <summary>An address that is not there yet is added; one that is there has its targets replaced.</summary>
    [Fact]
    public void With_adds_a_new_address_and_replaces_an_existing_one()
    {
        var rows = new[] { Row("info", MailAliasKind.Alias, "old@example.com") };

        var added = MailAliasSetMapper.With(rows, "sales", ["x@example.com"]);
        var replaced = MailAliasSetMapper.With(rows, "info", ["new@example.com"]);

        Assert.Equal(["info", "sales"], LocalParts(added));
        Assert.Equal(["old@example.com"], added.Aliases[0].Targets);
        Assert.Equal(["info"], LocalParts(replaced));
        Assert.Equal(["new@example.com"], replaced.Aliases[0].Targets);
    }

    /// <summary>The catch-all marker adds or replaces the catch-all and never appears among the aliases.</summary>
    [Fact]
    public void With_the_marker_sets_the_catch_all_and_not_an_alias()
    {
        var rows = new[] { Row("info", MailAliasKind.Alias, "bob@example.com") };

        var set = MailAliasSetMapper.With(rows, "*", ["boss@example.com"]);

        Assert.Equal(["info"], LocalParts(set));
        Assert.Equal(["boss@example.com"], set.CatchAllTargets);
    }

    /// <summary>Leaving an address out removes only that address; leaving out the catch-all clears only the catch-all.</summary>
    [Fact]
    public void Without_leaves_out_only_the_named_address()
    {
        var rows = new[]
        {
            Row("info", MailAliasKind.Alias, "bob@example.com"),
            Row("sales", MailAliasKind.Alias, "x@example.com"),
            Row("*", MailAliasKind.CatchAll, "boss@example.com"),
        };

        var withoutInfo = MailAliasSetMapper.Without(rows, "info");
        var withoutCatchAll = MailAliasSetMapper.Without(rows, "*");

        Assert.Equal(["sales"], LocalParts(withoutInfo));
        Assert.Equal(["boss@example.com"], withoutInfo.CatchAllTargets);
        Assert.Equal(["info", "sales"], LocalParts(withoutCatchAll));
        Assert.Null(withoutCatchAll.CatchAllTargets);
    }

    /// <summary>Builds an alias row in the test domain.</summary>
    /// <param name="localPart">The local part, or the catch-all marker.</param>
    /// <param name="kind">What the address is.</param>
    /// <param name="targets">Its targets, in order.</param>
    /// <returns>The row.</returns>
    private static MailAlias Row(string localPart, MailAliasKind kind, params string[] targets)
    {
        return new MailAlias(Guid.NewGuid(), Guid.NewGuid(), DomainId, localPart, kind, targets, Created);
    }

    /// <summary>The local parts of a set, in the order sent.</summary>
    /// <param name="set">The set.</param>
    /// <returns>The local parts.</returns>
    private static string[] LocalParts(MailAliasSet set)
    {
        return MailAliasProbe.LocalParts(set.Aliases);
    }
}
