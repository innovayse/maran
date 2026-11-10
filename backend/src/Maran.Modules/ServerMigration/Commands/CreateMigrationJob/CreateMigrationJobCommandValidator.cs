using FluentValidation;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Policies;

namespace Maran.Modules.ServerMigration.Commands.CreateMigrationJob;

/// <summary>Validates <see cref="CreateMigrationJobCommand"/> (issue #197, phase 2).</summary>
public sealed class CreateMigrationJobCommandValidator : AbstractValidator<CreateMigrationJobCommand>
{
    /// <summary>Declares the rules.</summary>
    public CreateMigrationJobCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(MigrationJob.NameMaxLength);
        RuleFor(command => command.DiscoveredAccountIds)
            .NotEmpty()
            .Must(ids =>
            {
                return ids.Count <= MigrationJobLimits.MaxAccounts && ids.Distinct().Count() == ids.Count;
            });
        RuleFor(command => command.Concurrency!.Value)
            .InclusiveBetween(MigrationJobLimits.MinConcurrency, MigrationJobLimits.MaxConcurrency)
            .When(command =>
            {
                return command.Concurrency is not null;
            });
        RuleFor(command => command.BandwidthLimitKib!.Value)
            .InclusiveBetween(1, MigrationJobLimits.MaxBandwidthLimitKib)
            .When(command =>
            {
                return command.BandwidthLimitKib is not null;
            });
        RuleFor(command => command.SourceUrl!)
            .MaximumLength(MigrationJob.SourceUrlMaxLength)
            .Must(url =>
            {
                return Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps;
            })
            .When(command =>
            {
                return command.SourceUrl is not null;
            });
        RuleFor(command => command.ExpectedKeyFingerprint!)
            .Length(MigrationJob.FingerprintLength)
            .Matches("^[0-9a-f]+$")
            .When(command =>
            {
                return command.ExpectedKeyFingerprint is not null;
            });
    }
}
