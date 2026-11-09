using FluentValidation;
using Maran.Modules.Files.Common;
using Maran.Modules.Files.Queries.ListFileEntries;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Validators;
using Maran.Sdk.Validators;

namespace Maran.Modules.Files.Queries.SearchFileEntries;

/// <summary>Holds <see cref="SearchFileEntriesQuery"/> to the paging bounds, a path the panel accepts and a term to look for.</summary>
public sealed class SearchFileEntriesQueryValidator : PagedQueryValidator<SearchFileEntriesQuery, FileEntryDto>
{
    /// <summary>Configures the rules.</summary>
    public SearchFileEntriesQueryValidator()
        : base(ListFileEntriesSorts.Map)
    {
        RuleFor(query => query.AccountId)
            .NotEmpty();

        RuleFor(query => query.Path)
            .Must(FilesPathRule.IsWellFormed)
            .WithMessage(nameof(ErrorMessages.FilesPathInvalid));

        // The trash directory is reached through the trash routes and no other.
        RuleFor(query => query.Path)
            .Must(path => { return !FilesPathRule.IsReserved(path); })
            .WithMessage(nameof(ErrorMessages.FilesPathReserved));

        // A search with no term would be a listing of the whole tree, which is not what this route is for.
        RuleFor(query => query.Search)
            .Must(search => { return !search.IsNone; })
            .WithMessage(nameof(ErrorMessages.FilesSearchTermRequired));
    }
}
