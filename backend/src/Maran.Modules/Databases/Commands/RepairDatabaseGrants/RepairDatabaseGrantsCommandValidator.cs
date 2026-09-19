using FluentValidation;
using Maran.Modules.Databases.Resources;

namespace Maran.Modules.Databases.Commands.RepairDatabaseGrants;

/// <summary>
/// Validates <see cref="RepairDatabaseGrantsCommand"/> before it reaches the handler
/// (rules/security.md "Input").
/// </summary>
/// <remarks>
/// There is exactly one thing to check, and it exists so that two different mistakes are answered
/// differently. A NEGATIVE confirmation is not a figure any report ever produced, so it is a malformed
/// request and is answered as one; a figure that is merely no longer true is answered by the handler as
/// a stale plan, which tells the operator to read the report again. Without this rule both would arrive
/// as "your report has moved", sending an operator to re-read a report that was never the problem.
///
/// The message is a bare resx key, not an English sentence: <c>ExceptionMiddleware</c> forwards a
/// validation message only when it is entirely alphanumeric, and then resolves it as an error code
/// against this module's resources. An English sentence is silently discarded and the caller gets the
/// generic failure instead.
/// </remarks>
public sealed class RepairDatabaseGrantsCommandValidator : AbstractValidator<RepairDatabaseGrantsCommand>
{
    /// <summary>Builds the rule set.</summary>
    public RepairDatabaseGrantsCommandValidator()
    {
        RuleFor(command => command.ExpectedRepairCount)
            .GreaterThanOrEqualTo(0)
            .WithMessage(nameof(ErrorMessages.DatabaseGrantRepairCountInvalid));
    }
}
