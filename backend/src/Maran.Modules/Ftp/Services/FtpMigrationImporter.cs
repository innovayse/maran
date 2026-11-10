using Maran.Modules.Ftp.Commands.CreateFtpUser;
using Maran.Modules.Ftp.Common;
using Maran.Modules.Ftp.Models;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;
using Wolverine;

namespace Maran.Modules.Ftp.Services;

/// <summary>
/// Creates an imported account's FTPS logins (issue #127). The Ftp module's
/// <see cref="IAccountMigrationImporter"/>.
/// </summary>
/// <remarks>
/// Each login is created through <see cref="CreateFtpUserCommand"/>, which generates its password; it is
/// returned once and kept by nobody (decision D3).
/// </remarks>
public sealed class FtpMigrationImporter : IAccountMigrationImporter
{
    /// <summary>The bus each login is created through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the importer.</summary>
    /// <param name="bus">The bus each login is created through.</param>
    public FtpMigrationImporter(IMessageBus bus)
    {
        _bus = bus;
    }

    /// <inheritdoc />
    public string ModuleId
    {
        get { return FtpManifest.Instance.Id; }
    }

    /// <inheritdoc />
    public int Order
    {
        get { return 50; }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MigrationFinding>> PlanAsync(
        MigrationSection section,
        MigrationPlanContext context,
        CancellationToken cancellationToken)
    {
        var readable = section.ReadPayload<FtpMigrationSection>(ModuleId, FtpMigrationExporter.SectionVersion) is not null;

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
        var payload = section.ReadPayload<FtpMigrationSection>(ModuleId, FtpMigrationExporter.SectionVersion);

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

        var secrets = new List<MigrationIssuedSecret>();

        foreach (var name in payload.UserNames)
        {
            var created = await _bus.InvokeAsync<Result<CreatedFtpUserDto>>(
                new CreateFtpUserCommand(accountId, name),
                cancellationToken);

            if (!created.IsSuccess)
            {
                return Result<MigrationSectionOutcome>.Fail(created.Error!);
            }

            secrets.Add(new MigrationIssuedSecret(created.Value!.FullName, created.Value.Password));
        }

        return Result<MigrationSectionOutcome>.Ok(new MigrationSectionOutcome(null, secrets));
    }
}
