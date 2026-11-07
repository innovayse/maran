using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// A module's answer to "what do you hold about this person or this account", for the administrator's
/// personal-data export. Implemented and registered by every module that stores a hosting account's
/// rows; the Identity module, which owns the export, gathers every registered provider.
/// </summary>
/// <remarks>
/// <para>
/// <b>An implementation is an allow-list, never a copy of an entity.</b> It names each field it returns.
/// It never returns a password hash, a key, a token or its digest, a TOTP secret, a recovery code, an
/// encrypted column, the output of a task or a cron run, or anything read from a customer's home
/// directory. A column nobody has named is not exported, so adding a secret to an entity cannot leak it.
/// </para>
/// <para>
/// <b>It runs under the administrator's principal.</b> The tenant query filters admit an administrator,
/// so an implementation reads its tables the ordinary way and needs no filter bypass.
/// </para>
/// </remarks>
public interface IPersonalDataProvider
{
    /// <summary>Reads what the module holds about <paramref name="subject"/>.</summary>
    /// <param name="subject">The person or account the export is about.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The module's sections about the subject, or an empty list when it holds nothing for that kind of
    /// subject: a module that stores rows against accounts only answers an empty list for a user.
    /// </returns>
    Task<IReadOnlyList<PersonalDataSection>> ExportAsync(PersonalDataSubject subject, CancellationToken ct);
}
