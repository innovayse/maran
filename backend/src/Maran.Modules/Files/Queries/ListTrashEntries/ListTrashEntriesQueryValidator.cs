using FluentValidation;
using Maran.Modules.Files.Common;
using Maran.Sdk.Validators;

namespace Maran.Modules.Files.Queries.ListTrashEntries;

/// <summary>Holds <see cref="ListTrashEntriesQuery"/> to the paging bounds.</summary>
public sealed class ListTrashEntriesQueryValidator : PagedQueryValidator<ListTrashEntriesQuery, TrashEntryDto>
{
    /// <summary>Configures the rules.</summary>
    public ListTrashEntriesQueryValidator()
        : base(ListTrashEntriesSorts.Map)
    {
        RuleFor(query => query.AccountId)
            .NotEmpty();
    }
}
