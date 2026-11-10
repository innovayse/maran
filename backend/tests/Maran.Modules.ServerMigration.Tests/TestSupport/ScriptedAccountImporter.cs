using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.ServerMigration.Tests.TestSupport;

/// <summary>An importer whose outcome is set by the test; it records the target every call received.</summary>
public sealed class ScriptedAccountImporter : IAccountMigrationImporter
{
    /// <summary>The account this importer reports having created, or <c>null</c> when it creates none.</summary>
    private readonly Guid? _createsAccount;

    /// <summary>Creates the importer.</summary>
    /// <param name="moduleId">The module it answers for.</param>
    /// <param name="order">Where it runs.</param>
    /// <param name="createsAccount">The account it reports having created, if any.</param>
    public ScriptedAccountImporter(string moduleId, int order, Guid? createsAccount = null)
    {
        ModuleId = moduleId;
        Order = order;
        _createsAccount = createsAccount;
    }

    /// <summary>The error the import answers, or <c>null</c> to succeed.</summary>
    public Error? FailWith { get; set; }

    /// <summary>The targets every import received, in order.</summary>
    public List<MigrationImportTarget> Targets { get; } = [];

    /// <inheritdoc />
    public string ModuleId { get; }

    /// <inheritdoc />
    public int Order { get; }

    /// <inheritdoc />
    public Task<IReadOnlyList<MigrationFinding>> PlanAsync(
        MigrationSection section,
        MigrationPlanContext context,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<MigrationFinding>>([]);
    }

    /// <inheritdoc />
    public Task<Result<MigrationSectionOutcome>> ImportAsync(
        MigrationSection section,
        MigrationImportTarget target,
        CancellationToken cancellationToken)
    {
        Targets.Add(target);

        return Task.FromResult(
            FailWith is null
                ? Result<MigrationSectionOutcome>.Ok(new MigrationSectionOutcome(_createsAccount, []))
                : Result<MigrationSectionOutcome>.Fail(FailWith));
    }
}
