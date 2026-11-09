using FluentValidation;
using Maran.Modules.Files.Common;
using Maran.Modules.Files.Resources;
using Maran.Modules.Files.Validators;
using Maran.Sdk.Validators;

namespace Maran.Modules.Files.Queries.ListFileEntries;

/// <summary>Holds <see cref="ListFileEntriesQuery"/> to the paging bounds and to a path the panel accepts.</summary>
/// <remarks>
/// The path rule is the panel being a good citizen, not the control: containment is the agent's
/// descriptor walk (rules/security.md item 2), and a path that passes here is checked again there.
/// </remarks>
public sealed class ListFileEntriesQueryValidator : PagedQueryValidator<ListFileEntriesQuery, FileEntryDto>
{
    /// <summary>Configures the rules.</summary>
    public ListFileEntriesQueryValidator()
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
    }
}
