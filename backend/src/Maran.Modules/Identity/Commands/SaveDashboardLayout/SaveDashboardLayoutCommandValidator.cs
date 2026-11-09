using System.Linq.Expressions;
using FluentValidation;
using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Resources;

namespace Maran.Modules.Identity.Commands.SaveDashboardLayout;

/// <summary>
/// Holds both lists of a layout to the policy's bounds (rules/security.md "Input"): present, at
/// most <see cref="DashboardLayoutPolicy.MaxEntriesPerList"/> entries, every key the shape of a
/// dashboard key, no key twice in a list. The server-established members are not validated here:
/// the action writes them from the caller's token and connection.
/// </summary>
public sealed class SaveDashboardLayoutCommandValidator : AbstractValidator<SaveDashboardLayoutCommand>
{
    /// <summary>Declares the same rules for both lists.</summary>
    public SaveDashboardLayoutCommandValidator()
    {
        DeclareListRules(command => command.Widgets);
        DeclareListRules(command => command.Actions);
    }

    /// <summary>Declares the rules of one list; the first one a list breaks is the only one reported for it.</summary>
    /// <param name="list">Selects the list from the command.</param>
    private void DeclareListRules(Expression<Func<SaveDashboardLayoutCommand, IReadOnlyList<DashboardLayoutEntryDto>>> list)
    {
        RuleFor(list)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(nameof(ErrorMessages.DashboardLayoutListMissing))
            .Must(entries =>
            {
                return entries.Count <= DashboardLayoutPolicy.MaxEntriesPerList;
            }).WithMessage(nameof(ErrorMessages.DashboardLayoutTooLong))
            .Must(entries =>
            {
                return entries.All(entry =>
                {
                    return entry is not null && DashboardLayoutPolicy.IsValidKey(entry.Key);
                });
            }).WithMessage(nameof(ErrorMessages.DashboardLayoutKeyInvalid))
            .Must(entries =>
            {
                // Ordinal, as the unique index (UserId, Kind, Key) compares.
                return entries.Select(entry =>
                {
                    return entry.Key;
                }).Distinct(StringComparer.Ordinal).Count() == entries.Count;
            }).WithMessage(nameof(ErrorMessages.DashboardLayoutKeyRepeated));
    }
}
