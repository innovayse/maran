using Maran.Modules.Accounts.Models;
using Maran.Modules.Accounts.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// Hands the account itself to another server: its name, primary domain, owner contact and language
/// (issue #127). The Accounts module's <see cref="IAccountMigrationExporter"/>.
/// </summary>
/// <remarks>
/// Reads through the module's tenant-scoped context, so a caller who may not see the account reads nothing and
/// the section is <c>null</c>. The plan is exported by name for the report and never by id: an id of one server
/// means nothing on another.
/// </remarks>
public sealed class AccountsMigrationExporter : IAccountMigrationExporter
{
    /// <summary>The version of <see cref="AccountsMigrationSection"/> this exporter writes.</summary>
    public const int SectionVersion = 1;

    /// <summary>The module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Creates the exporter.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public AccountsMigrationExporter(AccountsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public string ModuleId
    {
        get { return AccountsManifest.Instance.Id; }
    }

    /// <inheritdoc />
    public async Task<MigrationSection?> ExportAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await _dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == accountId, cancellationToken);

        if (account is null)
        {
            return null;
        }

        var planName = await _dbContext.Plans
            .AsNoTracking()
            .Where(plan => plan.Id == account.PlanId)
            .Select(plan => plan.Name)
            .FirstOrDefaultAsync(cancellationToken);

        return new AccountsMigrationSection(
            account.Name,
            account.PrimaryDomain,
            account.OwnerEmail,
            account.OwnerLanguage,
            planName,
            account.ShellAccess).ToSection(ModuleId, SectionVersion);
    }
}
