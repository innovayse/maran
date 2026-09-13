using FluentValidation;
using Maran.Modules.Firewall.Domain.ValueObjects;
using Maran.Modules.Firewall.Resources;

namespace Maran.Modules.Firewall.Commands.AddWhitelistEntry;

/// <summary>
/// Validates <see cref="AddWhitelistEntryCommand"/> before it reaches the handler
/// (rules/security.md "Input").
/// </summary>
/// <remarks>
/// A row that cannot be parsed matches no packet that ever arrives, so an administrator reading it
/// back would believe they were exempt from the automatic bans while they were not — which is worse
/// than having no row at all, because the false one stops them adding a real one. That is why the
/// range is parsed HERE, at the only point one is created, rather than left for the matcher to shrug
/// at.
///
/// Host bits beyond the prefix are refused rather than masked, as everywhere else in this module:
/// <c>203.0.113.7/24</c> exempts either one machine or two hundred and fifty-six of them depending
/// on which reading is silently chosen, and an exemption is precisely the thing that must not be
/// wider than the person who wrote it believes.
/// </remarks>
public sealed class AddWhitelistEntryCommandValidator : AbstractValidator<AddWhitelistEntryCommand>
{
    /// <summary>The longest note the column holds.</summary>
    private const int MaximumNoteLength = 200;

    /// <summary>Configures the field rules for <see cref="AddWhitelistEntryCommand"/>.</summary>
    public AddWhitelistEntryCommandValidator()
    {
        RuleFor(command => command.Cidr)
            .NotEmpty()
            .Must(CidrRange.IsUsable)
            .WithMessage(nameof(ErrorMessages.WhitelistCidrInvalid));

        RuleFor(command => command.Note)
            .NotNull()
            .MaximumLength(MaximumNoteLength);
    }
}
