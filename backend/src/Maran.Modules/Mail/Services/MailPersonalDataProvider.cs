using System.Globalization;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Mail.Services;

/// <summary>
/// This module's answer to <see cref="IPersonalDataProvider"/>: the mail domains, mailboxes and aliases an
/// account owns.
/// </summary>
/// <remarks>
/// <para>
/// <b>An allow-list, field by field.</b> Addresses and creation times are exported. The password hash, the
/// DKIM private key and the content of any mailbox are not: the first two are secrets, and the third is
/// the mail itself, which lives on the host under the account and is exported by the backup, not by this
/// listing. Alias targets are exported because a forwarder's destination is a person's address.
/// </para>
/// <para>The provider answers for an account subject only; a user subject gets an empty list.</para>
/// </remarks>
public sealed class MailPersonalDataProvider : IPersonalDataProvider
{
    /// <summary>The module's context, carrying the caller's tenant scope.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>Creates the provider over this module's context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public MailPersonalDataProvider(MailDbContext dbContext)
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

        var domains = await _dbContext.MailDomains
            .AsNoTracking()
            .Where(row => row.AccountId == subject.Id)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Select(row => new { row.Id, row.Name, row.CreatedAt })
            .ToListAsync(ct);

        var mailboxes = await _dbContext.Mailboxes
            .AsNoTracking()
            .Where(row => row.AccountId == subject.Id)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Select(row => new { row.Id, row.LocalPart, DomainName = row.MailDomain.Name, row.CreatedAt })
            .ToListAsync(ct);

        var aliases = await _dbContext.MailAliases
            .AsNoTracking()
            .Where(row => row.AccountId == subject.Id)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                row.LocalPart,
                DomainName = row.MailDomain.Name,
                row.Targets,
                row.CreatedAt,
            })
            .ToListAsync(ct);

        return
        [
            new PersonalDataSection(
                "mail",
                "domains",
                domains.Select(row =>
                {
                    return Record(row.Id, row.Name, row.CreatedAt);
                }).ToList()),
            new PersonalDataSection(
                "mail",
                "mailboxes",
                mailboxes.Select(row =>
                {
                    return Record(row.Id, MailboxAddressPolicy.Compose(row.LocalPart, row.DomainName), row.CreatedAt);
                }).ToList()),
            new PersonalDataSection(
                "mail",
                "aliases",
                aliases.Select(row =>
                {
                    var record = Fields(row.Id, MailboxAddressPolicy.Compose(row.LocalPart, row.DomainName), row.CreatedAt);
                    record["targets"] = string.Join(
                        ", ",
                        row.Targets
                            .OrderBy(target =>
                            {
                                return target.Position;
                            })
                            .Select(target =>
                            {
                                return target.Address;
                            }));

                    return new PersonalDataRecord(record);
                }).ToList()),
        ];
    }

    /// <summary>Builds a record of the three fields every listing carries.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="name">The domain name or the address.</param>
    /// <param name="createdAt">The creation instant.</param>
    /// <returns>The record.</returns>
    private static PersonalDataRecord Record(Guid id, string name, DateTimeOffset createdAt)
    {
        return new PersonalDataRecord(Fields(id, name, createdAt));
    }

    /// <summary>Builds the field map shared by every listing.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="name">The domain name or the address.</param>
    /// <param name="createdAt">The creation instant.</param>
    /// <returns>The mutable field map.</returns>
    private static Dictionary<string, string?> Fields(Guid id, string name, DateTimeOffset createdAt)
    {
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["id"] = id.ToString(),
            ["name"] = name,
            ["createdAt"] = createdAt.ToString("O", CultureInfo.InvariantCulture),
        };
    }
}
