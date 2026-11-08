using Maran.Modules.Files.Queries.SearchFileEntries;
using Maran.Modules.Files.Tests.TestSupport;

namespace Maran.Modules.Files.Tests.Queries.SearchFileEntries;

/// <summary>Holds <see cref="SearchFileEntriesQueryValidator"/> to the one path rule and to an account being named.</summary>
public sealed class SearchFileEntriesQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly SearchFileEntriesQueryValidator Validator = new();

    /// <summary>Builds the request with a path.</summary>
    /// <param name="path">The path to put in it.</param>
    /// <returns>The request.</returns>
    private static SearchFileEntriesQuery Request(string path)
    {
        return new SearchFileEntriesQuery(Guid.NewGuid(), path, false, 1, 10, Maran.SharedKernel.Pagination.SearchTerm.Of("a"));
    }

    /// <summary>A path the rule refuses is refused, at the boundary, before any handler runs.</summary>
    /// <param name="path">A hostile path.</param>
    [Theory]
    [MemberData(nameof(FilesHostilePaths.Refused), MemberType = typeof(FilesHostilePaths))]
    public void A_hostile_path_is_refused(string path)
    {
        Assert.False(Validator.Validate(Request(path)).IsValid);
    }

    /// <summary>The control: a legal path is accepted, so a validator that refused everything would fail here.</summary>
    [Fact]
    public void A_legal_path_is_accepted()
    {
        Assert.True(Validator.Validate(Request(FilesHostilePaths.Legal)).IsValid);
    }

    /// <summary>A search with no term would list the whole tree and is refused.</summary>
    [Fact]
    public void A_search_with_no_term_is_refused()
    {
        Assert.False(Validator.Validate(new SearchFileEntriesQuery(Guid.NewGuid(), "docs")).IsValid);
    }
}
