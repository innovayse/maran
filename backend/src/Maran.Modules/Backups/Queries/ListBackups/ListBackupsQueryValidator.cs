using FluentValidation;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Resources;
using Maran.Sdk.Validators;

namespace Maran.Modules.Backups.Queries.ListBackups;

/// <summary>
/// Holds <see cref="ListBackupsQuery"/> to the panel's paging bounds and its own filters
/// (rules/security.md "Input").
/// </summary>
/// <remarks>Each message is a bare resx key: only a key reaches the customer, localized by the panel.</remarks>
public sealed class ListBackupsQueryValidator : PagedQueryValidator<ListBackupsQuery, Backup>
{
    /// <summary>Declares the paging rules against <see cref="ListBackupsSorts.Map"/>, then the filters.</summary>
    public ListBackupsQueryValidator()
        : base(ListBackupsSorts.Map)
    {
        // Over HTTP, ?status=7 never reaches this rule: MVC's binder refuses a number naming no
        // member, answered as RequestParameterInvalid. The rule holds the query to its members for
        // a caller that dispatches it on the bus without HTTP binding.
        RuleFor(query => query.Status)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.BackupStatusUnknown))
            .WithMessage(nameof(ErrorMessages.BackupStatusUnknown));

        RuleFor(query => query.Kind)
            .IsInEnum()
            .WithErrorCode(nameof(ErrorMessages.BackupKindUnknown))
            .WithMessage(nameof(ErrorMessages.BackupKindUnknown));

        // An inverted window matches nothing; refusing it says why the page would be empty instead
        // of answering a silent zero the caller cannot tell from "no backups then".
        RuleFor(query => query.From)
            .Must((query, from) =>
            {
                return from is null || query.To is null || from.Value <= query.To.Value;
            })
            .WithErrorCode(nameof(ErrorMessages.BackupDateRangeInverted))
            .WithMessage(nameof(ErrorMessages.BackupDateRangeInverted));
    }
}
