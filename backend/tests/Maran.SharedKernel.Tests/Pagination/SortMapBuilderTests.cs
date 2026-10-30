using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Tests.Fixtures;

namespace Maran.SharedKernel.Tests.Pagination;

/// <summary>Behavioural contract of <see cref="SortMapBuilder{T}"/>: a broken map fails at the call that breaks it.</summary>
public sealed class SortMapBuilderTests
{
    /// <summary>A well-built map builds, the inverse control for every refusal below.</summary>
    [Fact]
    public void A_well_built_map_builds()
    {
        var map = new SortMapBuilder<PagedRow>()
            .By("name", row => row.Name)
            .DefaultTo("name", SortDirection.Descending)
            .TieBreaker(row => row.Id);

        Assert.Equal(new SortKey("name", SortDirection.Descending), map.Default);
    }

    /// <summary>A default naming no registered field throws at <c>DefaultTo</c>, before any tie-breaker is named.</summary>
    [Fact]
    public void A_default_naming_no_registered_field_throws()
    {
        var builder = new SortMapBuilder<PagedRow>().By("name", row => row.Name);

        Assert.Throws<ArgumentException>(() => { return builder.DefaultTo("missing", SortDirection.Ascending); });
    }

    /// <summary>A field registered twice throws at the second registration.</summary>
    [Fact]
    public void A_field_registered_twice_throws()
    {
        var builder = new SortMapBuilder<PagedRow>().By("name", row => row.Name);

        Assert.Throws<ArgumentException>(() => { return builder.By("name", row => row.Rank); });
    }

    /// <summary>A name that is not a well-formed sort field throws.</summary>
    /// <param name="field">A malformed field name.</param>
    [Theory]
    [InlineData("")]
    [InlineData("-name")]
    [InlineData("na me")]
    [InlineData("1name")]
    public void A_malformed_field_name_throws(string field)
    {
        Assert.Throws<ArgumentException>(() => { return new SortMapBuilder<PagedRow>().By(field, row => row.Name); });
    }
}
