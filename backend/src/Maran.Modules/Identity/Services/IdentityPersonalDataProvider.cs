using System.Globalization;
using System.Linq.Expressions;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Answers a personal-data export with what the Identity module holds: for a user, the login, its
/// memberships, offers, sessions and pinned pages; for an account, its team and the offers made for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The allow-list is the projection.</b> Every query selects named columns into an anonymous type,
/// so a column that is not named here is never read, let alone written out: <c>PasswordHash</c>,
/// <c>TotpSecret</c>, the last accepted TOTP window, the lockout counters, session token digests and
/// family ids, recovery-code digests and API-key digests are absent because nothing selects them. A
/// new secret column added to an entity is therefore NOT exported until someone names it here.
/// </para>
/// <para>
/// Runs as the administrator who asked, so it reads through the same context every other Identity
/// handler uses and bypasses nothing. Every value is text: timestamps are ISO 8601 in UTC, enums are
/// their names, and a missing value is <c>null</c>.
/// </para>
/// </remarks>
public sealed class IdentityPersonalDataProvider : IPersonalDataProvider
{
    /// <summary>The module name every section of this provider carries.</summary>
    private const string ModuleName = "identity";

    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the provider.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public IdentityPersonalDataProvider(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PersonalDataSection>> ExportAsync(
        PersonalDataSubject subject,
        CancellationToken ct)
    {
        switch (subject.Kind)
        {
            case PersonalDataSubjectKind.User:
                return
                [
                    await UserAsync(subject.Id, ct),
                    await MembershipsAsync(subject.Id, ct),
                    await OffersAsync(offer => offer.UserId == subject.Id, ct),
                    await SessionsAsync(subject.Id, ct),
                    await PinsAsync(subject.Id, ct),
                    await DashboardLayoutAsync(subject.Id, ct),
                ];
            case PersonalDataSubjectKind.Account:
                return
                [
                    await TeamAsync(subject.Id, ct),
                    await OffersAsync(offer => offer.AccountId == subject.Id, ct),
                ];
            default:
                return [];
        }
    }

    /// <summary>Reads the login itself: who it is, never how it proves it.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="ct">Cancels the read.</param>
    private async Task<PersonalDataSection> UserAsync(Guid userId, CancellationToken ct)
    {
        var rows = await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.Id,
                user.Username,
                user.Email,
                user.Role,
                user.Lifecycle,
                user.CreatedAt,
                user.LastLoginAt,
            })
            .ToListAsync(ct);

        return Section("user", rows.Select(row =>
        {
            return Record(
                ("id", Text(row.Id)),
                ("username", row.Username),
                ("email", row.Email),
                ("role", row.Role.ToString()),
                ("lifecycle", row.Lifecycle.ToString()),
                ("createdAt", Text(row.CreatedAt)),
                ("lastLoginAt", Text(row.LastLoginAt)));
        }));
    }

    /// <summary>Reads the accounts the user holds a seat in.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="ct">Cancels the read.</param>
    private async Task<PersonalDataSection> MembershipsAsync(Guid userId, CancellationToken ct)
    {
        var rows = await _dbContext.AccountMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId)
            .OrderBy(membership => membership.CreatedAt)
            .ThenBy(membership => membership.Id)
            .Select(membership => new
            {
                membership.AccountId,
                membership.Role,
                membership.State,
                membership.CreatedAt,
            })
            .ToListAsync(ct);

        return Section("memberships", rows.Select(row =>
        {
            return Record(
                ("accountId", Text(row.AccountId)),
                ("role", row.Role.ToString()),
                ("state", row.State.ToString()),
                ("createdAt", Text(row.CreatedAt)));
        }));
    }

    /// <summary>Reads the membership offers a filter selects, naming who they were addressed to by the typed identifier.</summary>
    /// <param name="scope">Selects the offers of one user or of one account.</param>
    /// <param name="ct">Cancels the read.</param>
    private async Task<PersonalDataSection> OffersAsync(
        Expression<Func<AccountMembershipOffer, bool>> scope,
        CancellationToken ct)
    {
        var rows = await _dbContext.AccountMembershipOffers
            .AsNoTracking()
            .Where(scope)
            .OrderBy(offer => offer.CreatedAt)
            .ThenBy(offer => offer.Id)
            .Select(offer => new
            {
                offer.AccountId,
                offer.Identifier,
                offer.Outcome,
                offer.CreatedAt,
                offer.ExpiresAt,
                offer.ResolvedAt,
            })
            .ToListAsync(ct);

        return Section("membershipOffers", rows.Select(row =>
        {
            return Record(
                ("accountId", Text(row.AccountId)),
                ("identifier", row.Identifier),
                ("outcome", row.Outcome?.ToString()),
                ("createdAt", Text(row.CreatedAt)),
                ("expiresAt", Text(row.ExpiresAt)),
                ("resolvedAt", Text(row.ResolvedAt)));
        }));
    }

    /// <summary>Reads the user's sign-ins: when, from where and with what client, never the token.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="ct">Cancels the read.</param>
    private async Task<PersonalDataSection> SessionsAsync(Guid userId, CancellationToken ct)
    {
        var rows = await _dbContext.Sessions
            .AsNoTracking()
            .Where(session => session.UserId == userId)
            .OrderBy(session => session.IssuedAt)
            .ThenBy(session => session.Id)
            .Select(session => new
            {
                session.IssuedAt,
                session.ExpiresAt,
                session.IpAddress,
                session.UserAgent,
                RevokedAt = session.Revocation != null ? (DateTimeOffset?)session.Revocation.At : null,
            })
            .ToListAsync(ct);

        return Section("sessions", rows.Select(row =>
        {
            return Record(
                ("issuedAt", Text(row.IssuedAt)),
                ("expiresAt", Text(row.ExpiresAt)),
                ("ipAddress", row.IpAddress),
                ("userAgent", row.UserAgent),
                ("revokedAt", Text(row.RevokedAt)));
        }));
    }

    /// <summary>Reads the pages the user pinned to their sidebar.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="ct">Cancels the read.</param>
    private async Task<PersonalDataSection> PinsAsync(Guid userId, CancellationToken ct)
    {
        var rows = await _dbContext.PinnedPages
            .AsNoTracking()
            .Where(pin => pin.UserId == userId)
            .OrderBy(pin => pin.Position)
            .ThenBy(pin => pin.Id)
            .Select(pin => new { pin.ItemKey, pin.Position, pin.PinnedAt })
            .ToListAsync(ct);

        return Section("pins", rows.Select(row =>
        {
            return Record(
                ("itemKey", row.ItemKey),
                ("position", row.Position.ToString(CultureInfo.InvariantCulture)),
                ("pinnedAt", Text(row.PinnedAt)));
        }));
    }

    /// <summary>Reads the dashboard layout the user arranged: which widgets and shortcuts, in what order, hidden or not.</summary>
    /// <param name="userId">The user.</param>
    /// <param name="ct">Cancels the read.</param>
    private async Task<PersonalDataSection> DashboardLayoutAsync(Guid userId, CancellationToken ct)
    {
        var rows = await _dbContext.DashboardPreferences
            .AsNoTracking()
            .Where(preference => preference.UserId == userId)
            .OrderBy(preference => preference.Kind)
            .ThenBy(preference => preference.Position)
            .ThenBy(preference => preference.Key)
            .Select(preference => new { preference.Kind, preference.Key, preference.Position, preference.Hidden })
            .ToListAsync(ct);

        return Section("dashboardLayout", rows.Select(row =>
        {
            return Record(
                ("kind", row.Kind.ToString()),
                ("key", row.Key),
                ("position", row.Position.ToString(CultureInfo.InvariantCulture)),
                ("hidden", row.Hidden ? "true" : "false"));
        }));
    }

    /// <summary>Reads the logins that hold a seat in the account: who they are and what their seat is.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="ct">Cancels the read.</param>
    private async Task<PersonalDataSection> TeamAsync(Guid accountId, CancellationToken ct)
    {
        var rows = await (
                from membership in _dbContext.AccountMemberships.AsNoTracking()
                join user in _dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
                where membership.AccountId == accountId
                orderby membership.CreatedAt, membership.Id
                select new
                {
                    UserId = user.Id,
                    user.Username,
                    user.Email,
                    membership.Role,
                    membership.State,
                    membership.CreatedAt,
                })
            .ToListAsync(ct);

        return Section("team", rows.Select(row =>
        {
            return Record(
                ("userId", Text(row.UserId)),
                ("username", row.Username),
                ("email", row.Email),
                ("role", row.Role.ToString()),
                ("state", row.State.ToString()),
                ("createdAt", Text(row.CreatedAt)));
        }));
    }

    /// <summary>Wraps records into one of this module's sections.</summary>
    /// <param name="name">The section's name.</param>
    /// <param name="records">The section's records.</param>
    private static PersonalDataSection Section(string name, IEnumerable<PersonalDataRecord> records)
    {
        return new PersonalDataSection(ModuleName, name, [.. records]);
    }

    /// <summary>Builds one record from named text fields.</summary>
    /// <param name="fields">The fields, in the order they should be written.</param>
    private static PersonalDataRecord Record(params (string Name, string? Value)[] fields)
    {
        return new PersonalDataRecord(fields.ToDictionary(field =>
        {
            return field.Name;
        }, field =>
        {
            return field.Value;
        }));
    }

    /// <summary>Spells an id the way every export spells it.</summary>
    /// <param name="value">The id.</param>
    private static string Text(Guid value)
    {
        return value.ToString("D", CultureInfo.InvariantCulture);
    }

    /// <summary>Spells an instant as ISO 8601, or leaves a missing one missing.</summary>
    /// <param name="value">The instant, or null.</param>
    private static string? Text(DateTimeOffset? value)
    {
        return value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
