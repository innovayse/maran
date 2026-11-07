using System.Globalization;

namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>
/// The stand-ins an erasure writes into the audit journal in place of a person or an account, and the
/// rule that makes them stand-ins and nothing more.
/// </summary>
/// <remarks>
/// <para>
/// <b>A pseudonym is random and has no table behind it.</b> <see cref="NewId"/> draws a fresh random
/// identifier per erasure, and the readable text is cut from that same identifier, so nothing that
/// was in the row before — an id, a login name, an address — can be recomputed from what replaced it.
/// A pseudonym derived from the original (a hash of the user name, say) would be a lookup key for
/// anyone holding a candidate name, which is the opposite of erasing it.
/// </para>
/// <para>
/// <b>The text is short on purpose, and the shortness is a stated limit.</b> Eight hexadecimal digits
/// are a label to tell two erased people apart on a screen, not an identity: two erasures may share
/// one, and nothing may treat the text as unique. The identifier beside it (the actor id column) is
/// what ties one person's rows together.
/// </para>
/// <para>
/// <b>The user prefix is also how a row says it was already pseudonymised.</b> An erased actor's id
/// points at no login, which is exactly what the orphaned-actor sweep looks for; without a marker the
/// sweep would erase the same rows again every day. The marker is the actor NAME rather than a flag
/// because a row erased as part of an account keeps its actor (an administrator's accountability is not
/// the account's personal data) and is therefore not marked as erased in the actor columns at all.
/// </para>
/// </remarks>
public static class AuditPseudonym
{
    /// <summary>The prefix of the name that stands in for an erased person.</summary>
    public const string UserNamePrefix = "erased-user-";

    /// <summary>The prefix of the text that stands in for the subject of a deleted account's row.</summary>
    public const string AccountSubjectPrefix = "erased-account-";

    /// <summary>How many hexadecimal digits of the identifier the readable text keeps.</summary>
    private const int SuffixLength = 8;

    /// <summary>Draws the random identifier that stands in for one person or one account.</summary>
    /// <returns>A fresh random identifier that points at nothing.</returns>
    public static Guid NewId()
    {
        return Guid.NewGuid();
    }

    /// <summary>The name that stands in for an erased person.</summary>
    /// <param name="id">The identifier returned by <see cref="NewId"/>.</param>
    /// <returns>The prefix followed by the first eight hexadecimal digits of <paramref name="id"/>.</returns>
    public static string UserName(Guid id)
    {
        return UserNamePrefix + Suffix(id);
    }

    /// <summary>The subject text that stands in for a deleted account.</summary>
    /// <param name="id">The identifier returned by <see cref="NewId"/>.</param>
    /// <returns>The prefix followed by the first eight hexadecimal digits of <paramref name="id"/>.</returns>
    public static string AccountSubject(Guid id)
    {
        return AccountSubjectPrefix + Suffix(id);
    }

    /// <summary>The label part of a pseudonym.</summary>
    /// <param name="id">The identifier the label is cut from.</param>
    /// <returns>The first <see cref="SuffixLength"/> hexadecimal digits.</returns>
    private static string Suffix(Guid id)
    {
        return id.ToString("N", CultureInfo.InvariantCulture)[..SuffixLength];
    }
}
