using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Models;
using Maran.Modules.Accounts.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;
using Wolverine;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// Creates the account of an imported package and judges, before anything exists, whether it can be created
/// (issue #127). The Accounts module's <see cref="IAccountMigrationImporter"/>, and the first of them to run.
/// </summary>
/// <remarks>
/// <para>
/// <b>It creates through <see cref="CreateAccountCommand"/>, the only way an account comes to exist</b>, so the
/// name rules, the reserved names, the reseller scoping and the plan bounds are the ordinary ones. The owner is
/// not mailed (<see cref="CreateAccountCommand.SuppressInvitationMail"/>): the administrator issues the link at
/// cut-over, so a customer is never invited to a server that is not live yet (decision D7).
/// </para>
/// <para>
/// <b>The name is kept, never renamed</b> (decision D1). The agent's restore requires the backup's manifest to
/// name the target account, and the dumps embed the account's database names, so a rename would change bytes
/// that are signed. A name already in use is a conflict to be reported by the dry run, not repaired.
/// </para>
/// <para>
/// <b>Limits are judged here for every resource kind.</b> The Accounts module owns the plan, so it is the one
/// that compares the package's counts with it; the other modules' importers judge only their own names.
/// </para>
/// </remarks>
public sealed class AccountsMigrationImporter : IAccountMigrationImporter
{
    /// <summary>The module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Tells whether a name or a primary domain is already claimed, across every owner.</summary>
    private readonly AccountNamespaceClaims _claims;

    /// <summary>The bus the account is created through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Creates the importer.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="claims">Tells whether a name or a primary domain is already claimed.</param>
    /// <param name="bus">The bus the account is created through.</param>
    public AccountsMigrationImporter(AccountsDbContext dbContext, AccountNamespaceClaims claims, IMessageBus bus)
    {
        _dbContext = dbContext;
        _claims = claims;
        _bus = bus;
    }

    /// <inheritdoc />
    public string ModuleId
    {
        get { return AccountsManifest.Instance.Id; }
    }

    /// <inheritdoc />
    public int Order
    {
        get { return 0; }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MigrationFinding>> PlanAsync(
        MigrationSection section,
        MigrationPlanContext context,
        CancellationToken cancellationToken)
    {
        var payload = section.ReadPayload<AccountsMigrationSection>(ModuleId, AccountsMigrationExporter.SectionVersion);

        if (payload is null)
        {
            return [new MigrationFinding(ModuleId, MigrationFindingSeverity.Conflict, MigrationCodes.SectionInvalid, context.AccountName)];
        }

        var findings = new List<MigrationFinding>();

        if (await _claims.IsNameTakenAsync(payload.Name, cancellationToken))
        {
            findings.Add(new MigrationFinding(ModuleId, MigrationFindingSeverity.Conflict, MigrationCodes.NameTaken, payload.Name));
        }

        if (await _claims.IsDomainTakenAsync(payload.PrimaryDomain, cancellationToken))
        {
            findings.Add(new MigrationFinding(ModuleId, MigrationFindingSeverity.Conflict, MigrationCodes.DomainTaken, payload.PrimaryDomain));
        }

        var plan = await _dbContext.Plans.AsNoTracking().FirstOrDefaultAsync(row => row.Id == context.PlanId, cancellationToken);

        if (plan is null)
        {
            findings.Add(new MigrationFinding(ModuleId, MigrationFindingSeverity.Conflict, MigrationCodes.PlanMissing, payload.Name));

            return findings;
        }

        AddLimitFinding(findings, context, "sites", plan.MaxSites);
        AddLimitFinding(findings, context, "databases", plan.MaxDatabases);
        AddLimitFinding(findings, context, "sftp", plan.MaxSftpUsers);
        AddLimitFinding(findings, context, "ftp", plan.MaxFtpUsers);
        AddLimitFinding(findings, context, "cron", plan.MaxCronEntries);

        if (context.HomeBytes > (long)plan.DiskQuotaMb * 1024 * 1024)
        {
            findings.Add(new MigrationFinding(ModuleId, MigrationFindingSeverity.Conflict, MigrationCodes.PlanLimitExceeded, "disk"));
        }

        return findings;
    }

    /// <inheritdoc />
    public async Task<Result<MigrationSectionOutcome>> ImportAsync(
        MigrationSection section,
        MigrationImportTarget target,
        CancellationToken cancellationToken)
    {
        var payload = section.ReadPayload<AccountsMigrationSection>(ModuleId, AccountsMigrationExporter.SectionVersion);

        if (payload is null || string.IsNullOrWhiteSpace(payload.OwnerEmail))
        {
            return Result<MigrationSectionOutcome>.Fail(
                Error.Of(MigrationCodes.SectionInvalid, ErrorType.Validation));
        }

        var created = await _bus.InvokeAsync<Result<AccountDto>>(
            new CreateAccountCommand(
                payload.Name,
                payload.PrimaryDomain,
                target.Choices.PlanId,
                payload.OwnerEmail,
                payload.OwnerLanguage,
                target.Choices.ResellerId,
                ShellAccess: payload.ShellAccess,
                SuppressInvitationMail: true),
            cancellationToken);

        return created.IsSuccess
            ? Result<MigrationSectionOutcome>.Ok(new MigrationSectionOutcome(created.Value!.Id, []))
            : Result<MigrationSectionOutcome>.Fail(created.Error!);
    }

    /// <summary>Adds a conflict when the package holds more of a resource kind than the plan allows.</summary>
    /// <param name="findings">Where findings are added.</param>
    /// <param name="context">What the package holds.</param>
    /// <param name="kind">The module id that owns the kind of resource.</param>
    /// <param name="limit">The plan's bound for the kind.</param>
    private void AddLimitFinding(List<MigrationFinding> findings, MigrationPlanContext context, string kind, int limit)
    {
        if (context.ResourceCounts.TryGetValue(kind, out var count) && count > limit)
        {
            findings.Add(new MigrationFinding(ModuleId, MigrationFindingSeverity.Conflict, MigrationCodes.PlanLimitExceeded, kind));
        }
    }
}
