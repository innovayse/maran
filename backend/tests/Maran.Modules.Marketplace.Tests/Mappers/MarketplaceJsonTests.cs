using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Mappers;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Tests.TestSupport;

namespace Maran.Modules.Marketplace.Tests.Mappers;

/// <summary>The service's JSON is read strictly: a missing member or an unknown enum word is refused, an extra member is ignored.</summary>
public sealed class MarketplaceJsonTests
{
    /// <summary>The instant used in documents.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A well-formed page parses into the module's types, entitlement word included.</summary>
    [Fact]
    public void A_well_formed_page_is_read()
    {
        var module = MarketplaceDocuments.Module("mail", MarketplaceDocuments.Version("1.2.0", new string('a', 64), 10));
        var bytes = MarketplaceTestWorld.BytesOf(MarketplaceDocuments.Page(Now, Now.AddHours(1), module));

        var page = MarketplaceJson.TryParse<CataloguePage>(bytes);

        Assert.NotNull(page);
        Assert.Equal(EntitlementState.Entitled, page.Items[0].Entitlement.State);
        Assert.Equal("1.2.0", page.Items[0].Latest!.Version);
        Assert.Null(page.NextCursor);
    }

    /// <summary>A member the panel does not know is ignored, so the service can add fields within a version.</summary>
    [Fact]
    public void An_unknown_member_is_ignored()
    {
        var bytes = MarketplaceTestWorld.BytesOf(new { items = Array.Empty<object>(), signedAt = Now, validUntil = Now, later = 1 });

        Assert.NotNull(MarketplaceJson.TryParse<CataloguePage>(bytes));
    }

    /// <summary>A required member that is missing makes the answer unusable.</summary>
    [Fact]
    public void A_missing_required_member_is_refused()
    {
        var withDates = MarketplaceTestWorld.BytesOf(new { items = Array.Empty<object>(), signedAt = Now, validUntil = Now });
        var withoutValidUntil = MarketplaceTestWorld.BytesOf(new { items = Array.Empty<object>(), signedAt = Now });
        var withoutItems = MarketplaceTestWorld.BytesOf(new { signedAt = Now, validUntil = Now });

        Assert.NotNull(MarketplaceJson.TryParse<CataloguePage>(withDates));
        Assert.Null(MarketplaceJson.TryParse<CataloguePage>(withoutValidUntil));
        Assert.Null(MarketplaceJson.TryParse<CataloguePage>(withoutItems));
    }

    /// <summary>A null where the shape forbids it, or an enum word the panel does not know, makes the answer unusable.</summary>
    [Fact]
    public void A_null_or_an_unknown_enum_word_is_refused()
    {
        var nullItems = MarketplaceTestWorld.BytesOf(new { items = (object?)null, signedAt = Now, validUntil = Now });
        var module = new
        {
            id = "mail",
            displayName = "m",
            summary = "s",
            publisher = "p",
            tier = "AddOn",
            entitlement = new { state = "Bought" },
            revoked = false,
        };
        var unknownWord = MarketplaceTestWorld.BytesOf(MarketplaceDocuments.Page(Now, Now, module));
        var integerWord = MarketplaceTestWorld.BytesOf(
            MarketplaceDocuments.Page(Now, Now, new { id = "mail", displayName = "m", summary = "s", publisher = "p", tier = "AddOn", entitlement = new { state = 1 }, revoked = false }));

        Assert.Null(MarketplaceJson.TryParse<CataloguePage>(nullItems));
        Assert.Null(MarketplaceJson.TryParse<CataloguePage>(unknownWord));
        Assert.Null(MarketplaceJson.TryParse<CataloguePage>(integerWord));
    }

    /// <summary>Bytes that are not JSON, an empty body and a JSON null all parse to nothing and never throw.</summary>
    [Fact]
    public void Bytes_that_are_not_the_shape_parse_to_nothing()
    {
        Assert.Null(MarketplaceJson.TryParse<CataloguePage>("<html>"u8));
        Assert.Null(MarketplaceJson.TryParse<CataloguePage>([]));
        Assert.Null(MarketplaceJson.TryParse<CataloguePage>("null"u8));
        Assert.Null(MarketplaceJson.TryParse<CataloguePage>("[1,2]"u8));
    }

    /// <summary>The enum words are read case-insensitively, as the service's PascalCase is not a promise about case.</summary>
    [Fact]
    public void Enum_words_are_read_case_insensitively()
    {
        var module = new
        {
            id = "mail",
            displayName = "m",
            summary = "s",
            publisher = "p",
            tier = "AddOn",
            entitlement = new { state = "leaseexpired" },
            revoked = false,
        };
        var bytes = MarketplaceTestWorld.BytesOf(MarketplaceDocuments.Page(Now, Now, module));

        var page = MarketplaceJson.TryParse<CataloguePage>(bytes);

        Assert.Equal(EntitlementState.LeaseExpired, page!.Items[0].Entitlement.State);
    }

    /// <summary>A grant is read with its id and address wrapped, and neither prints, so a log line cannot carry them.</summary>
    [Fact]
    public void A_grant_never_prints_its_id_or_address()
    {
        var bytes = MarketplaceTestWorld.BytesOf(
            MarketplaceDocuments.Grant("https://marketplace.test/downloads/secret-path", new string('b', 64), 10, Now));

        var grant = MarketplaceJson.TryParse<DownloadGrant>(bytes)!;

        Assert.Equal("grant-secret-id", grant.GrantId.Reveal());
        Assert.Equal("https://marketplace.test/downloads/secret-path", grant.Url.Reveal());
        Assert.DoesNotContain("grant-secret-id", grant.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-path", grant.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A request body is written in the service's camelCase.</summary>
    [Fact]
    public void A_request_body_is_written_in_camel_case()
    {
        var bytes = MarketplaceJson.Serialize(new UpdatesRequest("stable", [new InstalledModuleReference("mail", "1.0.0", "ab")]));

        Assert.Equal("{\"channel\":\"stable\",\"installed\":[{\"id\":\"mail\",\"version\":\"1.0.0\",\"sha256\":\"ab\"}]}", System.Text.Encoding.UTF8.GetString(bytes));
    }
}
