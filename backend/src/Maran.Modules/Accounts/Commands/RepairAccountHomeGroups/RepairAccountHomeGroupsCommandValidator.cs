using FluentValidation;
using Maran.Modules.Accounts.Resources;

namespace Maran.Modules.Accounts.Commands.RepairAccountHomeGroups;

/// <summary>
/// Validates <see cref="RepairAccountHomeGroupsCommand"/> before it reaches the handler
/// (rules/security.md "Input").
/// </summary>
/// <remarks>
/// Mirrors <c>RepairDatabaseGrantsCommandValidator</c> exactly, for the same reason: a NEGATIVE
/// confirmation is not a figure any report ever produced, so it is a malformed request and is
/// answered as one, while a figure that is merely no longer true is answered by the handler as a
/// stale plan. Without this rule both would arrive as "your report has moved."
///
/// The message is a bare resx key, not an English sentence: <c>ExceptionMiddleware</c> forwards a
/// validation message only when it is entirely alphanumeric, and then resolves it as an error code
/// against this module's resources.
/// </remarks>
public sealed class RepairAccountHomeGroupsCommandValidator : AbstractValidator<RepairAccountHomeGroupsCommand>
{
    /// <summary>Builds the rule set.</summary>
    public RepairAccountHomeGroupsCommandValidator()
    {
        RuleFor(command => command.ExpectedRepairCount)
            .GreaterThanOrEqualTo(0)
            .WithMessage(nameof(ErrorMessages.AccountHomeGroupRepairCountInvalid));
    }
}
