using FluentValidation;
using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Resources;
using Maran.Sdk.Validators;

namespace Maran.Modules.Backups.Queries.ListBackupDestinations;

/// <summary>
/// Holds <see cref="ListBackupDestinationsQuery"/> to the panel's paging bounds and its kind filter
/// (rules/security.md "Input").
/// </summary>
public sealed class ListBackupDestinationsQueryValidator
    : PagedQueryValidator<ListBackupDestinationsQuery, BackupDestinationDto>
{
    /// <summary>Declares the paging rules against <see cref="ListBackupDestinationsSorts.Map"/>, then the kind.</summary>
    public ListBackupDestinationsQueryValidator()
        : base(ListBackupDestinationsSorts.Map)
    {
        // A number binds to an enum without naming a member; refuse it rather than filter by nothing.
        RuleFor(query => query.Kind)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.BackupDestinationKindUnknown))
            .WithMessage(nameof(ErrorMessages.BackupDestinationKindUnknown));
    }
}
