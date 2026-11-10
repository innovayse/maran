using FluentValidation;
using Maran.Modules.ServerMigration.Domain.Policies;

namespace Maran.Modules.ServerMigration.Commands.SetMigrationLimits;

/// <summary>Validates <see cref="SetMigrationLimitsCommand"/> (issue #197, phase 2).</summary>
public sealed class SetMigrationLimitsCommandValidator : AbstractValidator<SetMigrationLimitsCommand>
{
    /// <summary>Declares the rules.</summary>
    public SetMigrationLimitsCommandValidator()
    {
        RuleFor(command => command.Concurrency)
            .InclusiveBetween(MigrationJobLimits.MinConcurrency, MigrationJobLimits.MaxConcurrency);
        RuleFor(command => command.BandwidthLimitKib!.Value)
            .InclusiveBetween(1, MigrationJobLimits.MaxBandwidthLimitKib)
            .When(command =>
            {
                return command.BandwidthLimitKib is not null;
            });
    }
}
