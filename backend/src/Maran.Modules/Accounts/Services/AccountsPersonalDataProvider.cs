using System.Globalization;
using Maran.Modules.Accounts.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// This module's answer to <see cref="IPersonalDataProvider"/>: the account row itself, the one place the
/// panel records who an account's owner is.
/// </summary>
/// <remarks>
/// <para>
/// <b>An allow-list, field by field.</b> The query projects the named columns and nothing else, so a
/// column added to the entity later is not exported until it is named here. The account row holds no
/// secret today; the list is still written out so that stays true by construction and not by luck.
/// </para>
/// <para>
/// The provider answers for an account subject only. A user subject gets an empty list, because this
/// module holds no row about a login: Identity does.
/// </para>
/// </remarks>
public sealed class AccountsPersonalDataProvider : IPersonalDataProvider
{
    /// <summary>The module's context, carrying the caller's tenant scope.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Creates the provider over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public AccountsPersonalDataProvider(AccountsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PersonalDataSection>> ExportAsync(
        PersonalDataSubject subject,
        CancellationToken ct)
    {
        if (subject.Kind != PersonalDataSubjectKind.Account)
        {
            return [];
        }

        var account = await _dbContext.Accounts
            .AsNoTracking()
            .Where(row => row.Id == subject.Id)
            .Select(row => new
            {
                row.Id,
                row.Name,
                row.PrimaryDomain,
                row.OwnerEmail,
                row.OwnerLanguage,
                row.Status,
                row.CreatedAt,
                row.PlanId,
                row.ResellerId,
            })
            .SingleOrDefaultAsync(ct);

        if (account is null)
        {
            return [new PersonalDataSection("accounts", "account", [])];
        }

        var planName = await _dbContext.Plans
            .AsNoTracking()
            .Where(plan => plan.Id == account.PlanId)
            .Select(plan => plan.Name ?? plan.DisplayNameKey)
            .SingleOrDefaultAsync(ct);

        var record = new PersonalDataRecord(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["id"] = account.Id.ToString(),
            ["name"] = account.Name,
            ["primaryDomain"] = account.PrimaryDomain,
            ["ownerEmail"] = account.OwnerEmail,
            ["ownerLanguage"] = account.OwnerLanguage,
            ["status"] = account.Status.ToString(),
            ["createdAt"] = account.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            ["planName"] = planName,
            ["resellerId"] = account.ResellerId?.ToString(),
        });

        return [new PersonalDataSection("accounts", "account", [record])];
    }
}
