using Maran.Modules.Files.Queries.ListFileEntries;
using Maran.Modules.Files.Tests.TestSupport;

namespace Maran.Modules.Files.Tests.Queries.ListFileEntries;

/// <summary>Holds <see cref="ListFileEntriesQueryValidator"/> to the one path rule and to an account being named.</summary>
public sealed class ListFileEntriesQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private static readonly ListFileEntriesQueryValidator Validator = new();

    /// <summary>Builds the request with a path.</summary>
    /// <param name="path">The path to put in it.</param>
    /// <returns>The request.</returns>
    private static ListFileEntriesQuery Request(string path)
    {
        return new ListFileEntriesQuery(Guid.NewGuid(), path);
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

    /// <summary>The home itself, the empty path, is a folder to list.</summary>
    [Fact]
    public void The_home_is_a_folder_that_can_be_listed()
    {
        Assert.True(Validator.Validate(Request(string.Empty)).IsValid);
    }

    /// <summary>A request naming no account is refused.</summary>
    [Fact]
    public void A_request_naming_no_account_is_refused()
    {
        Assert.False(Validator.Validate(new ListFileEntriesQuery(Guid.Empty, FilesHostilePaths.Legal)).IsValid);
    }

    /// <summary>A page size the list does not offer is refused by the shared paging rules.</summary>
    [Fact]
    public void A_page_size_the_list_does_not_offer_is_refused()
    {
        Assert.False(Validator.Validate(new ListFileEntriesQuery(Guid.NewGuid(), string.Empty, false, 1, 7)).IsValid);
    }
}
