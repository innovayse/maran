using Maran.Modules.Databases.Commands.ChangeDatabaseUserPrivileges;
using Maran.Modules.Databases.Commands.CreateDatabase;
using Maran.Modules.Databases.Commands.CreateDatabaseUser;
using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Domain.Enums;
using Maran.Modules.Databases.Models;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;
using Wolverine;

namespace Maran.Modules.Databases.Services;

/// <summary>
/// Creates an imported account's databases and their logins (issue #127). The Databases module's
/// <see cref="IAccountMigrationImporter"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>It runs before the restore and creates empty databases.</b> The agent's restore only replaces databases
/// the panel's own rows name, so each must exist first; the restore then loads the dump into it through the
/// restricted loader (never as the database superuser).
/// </para>
/// <para>
/// <b>Each login gets a new password, returned once</b> (decision D3). A package never carries one, so a
/// migrated application's configuration must be updated with the password the report shows.
/// </para>
/// <para>
/// <b>Additional users and their grants</b> follow the databases (a grant names one). The dry run refuses a
/// section whose grant names a database it does not hold or a privilege this build does not know, so the import
/// does not stop halfway on a name a package invented.
/// </para>
/// <para>
/// A database name cannot collide with another account's, because names are prefixed with the account name and
/// the account is new; so the dry run has nothing to report beyond a section it cannot read.
/// </para>
/// </remarks>
public sealed class DatabasesMigrationImporter : IAccountMigrationImporter
{
    /// <summary>The bus each database is created through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the importer.</summary>
    /// <param name="bus">The bus each database is created through.</param>
    public DatabasesMigrationImporter(IMessageBus bus)
    {
        _bus = bus;
    }

    /// <inheritdoc />
    public string ModuleId
    {
        get { return DatabasesManifest.Instance.Id; }
    }

    /// <inheritdoc />
    public int Order
    {
        get { return 10; }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MigrationFinding>> PlanAsync(
        MigrationSection section,
        MigrationPlanContext context,
        CancellationToken cancellationToken)
    {
        var readable = section.ReadPayload<DatabasesMigrationSection>(ModuleId, DatabasesMigrationExporter.SectionVersion)
            is { } payload && IsConsistent(payload);

        IReadOnlyList<MigrationFinding> findings = readable
            ? []
            : [new MigrationFinding(ModuleId, MigrationFindingSeverity.Conflict, MigrationCodes.SectionInvalid, context.AccountName)];

        return Task.FromResult(findings);
    }

    /// <inheritdoc />
    public async Task<Result<MigrationSectionOutcome>> ImportAsync(
        MigrationSection section,
        MigrationImportTarget target,
        CancellationToken cancellationToken)
    {
        var payload = section.ReadPayload<DatabasesMigrationSection>(ModuleId, DatabasesMigrationExporter.SectionVersion);

        if (payload is null)
        {
            return Result<MigrationSectionOutcome>.Fail(
                Error.Of(MigrationCodes.SectionInvalid, ErrorType.Validation));
        }

        if (target.AccountId is not { } accountId)
        {
            return Result<MigrationSectionOutcome>.Fail(
                Error.Of(MigrationCodes.TargetAccountMissing, ErrorType.Failure));
        }

        if (!IsConsistent(payload))
        {
            return Result<MigrationSectionOutcome>.Fail(Error.Of(MigrationCodes.SectionInvalid, ErrorType.Validation));
        }

        var secrets = new List<MigrationIssuedSecret>();
        var createdIds = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var database in payload.Databases)
        {
            var created = await _bus.InvokeAsync<Result<CreatedDatabaseDto>>(
                new CreateDatabaseCommand(accountId, database.Name, database.DbUserNameSuffix),
                cancellationToken);

            if (!created.IsSuccess)
            {
                return Result<MigrationSectionOutcome>.Fail(created.Error!);
            }

            createdIds[database.Name] = created.Value!.Id;
            secrets.Add(new MigrationIssuedSecret(created.Value.DbUserName, created.Value.Password));
        }

        // The additional users come after every database exists, because a grant names one. Each gets a new
        // password, shown once like the databases' own (decision D3); a package never carries one.
        foreach (var user in payload.Users)
        {
            var created = await _bus.InvokeAsync<Result<CreatedDatabaseUserDto>>(
                new CreateDatabaseUserCommand(accountId, user.NameSuffix),
                cancellationToken);

            if (!created.IsSuccess)
            {
                return Result<MigrationSectionOutcome>.Fail(created.Error!);
            }

            secrets.Add(new MigrationIssuedSecret(created.Value!.FullName, created.Value.Password));

            foreach (var grant in user.Grants)
            {
                var privileges = grant.Privileges.Select(Enum.Parse<DatabasePrivilege>).ToList();
                var changed = await _bus.InvokeAsync<Result>(
                    new ChangeDatabaseUserPrivilegesCommand(privileges, created.Value.Id, createdIds[grant.DatabaseName]),
                    cancellationToken);

                if (!changed.IsSuccess)
                {
                    return Result<MigrationSectionOutcome>.Fail(changed.Error!);
                }
            }
        }

        return Result<MigrationSectionOutcome>.Ok(new MigrationSectionOutcome(null, secrets));
    }

    /// <summary>Whether every grant of every user names a database of the section and only known privileges.</summary>
    /// <param name="payload">The section as it was read.</param>
    /// <returns>
    /// <c>true</c> when the section can be imported without guessing. Checked before anything is created, in the
    /// dry run and again at the import, so a package cannot make the import stop halfway on a name it invented.
    /// </returns>
    private static bool IsConsistent(DatabasesMigrationSection payload)
    {
        // A document that leaves out a list deserialises it as null; that is not a section this build wrote.
        if (payload.Databases is null || payload.Users is null || payload.Users.Any(user => { return user.Grants is null; }))
        {
            return false;
        }

        var names = payload.Databases.Select(database => { return database.Name; }).ToHashSet(StringComparer.Ordinal);

        return payload.Users.All(user =>
        {
            return user.Grants.All(grant =>
            {
                return names.Contains(grant.DatabaseName)
                    && grant.Privileges.All(privilege =>
                    {
                        return Enum.TryParse<DatabasePrivilege>(privilege, ignoreCase: false, out var parsed)
                            && Enum.IsDefined(parsed);
                    });
            });
        });
    }
}
