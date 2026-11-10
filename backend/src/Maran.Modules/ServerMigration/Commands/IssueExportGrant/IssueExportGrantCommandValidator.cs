using FluentValidation;
using Maran.Modules.ServerMigration.Domain.Policies;

namespace Maran.Modules.ServerMigration.Commands.IssueExportGrant;

/// <summary>Checks a grant's lifetime is within the bound and its address range, when stated, is a range.</summary>
public sealed class IssueExportGrantCommandValidator : AbstractValidator<IssueExportGrantCommand>
{
    /// <summary>Declares the rules.</summary>
    public IssueExportGrantCommandValidator()
    {
        RuleFor(command => command.LifetimeMinutes)
            .InclusiveBetween(1, (int)MigrationPackageLimits.MaxGrantLifetime.TotalMinutes)
            .When(command =>
            {
                return command.LifetimeMinutes is not null;
            });
        RuleFor(command => command.AllowedFrom)
            .Must(text =>
            {
                return AllowedFromPolicy.TryRead(text, out _);
            })
            .When(command =>
            {
                return !string.IsNullOrWhiteSpace(command.AllowedFrom);
            });
    }
}
