using Maran.Modules.Mail.Domain.Enums;

namespace Maran.Modules.Mail.Domain.Entities;

/// <summary>
/// A domain an account receives and sends mail for. The globally unique name is what stops two accounts
/// claiming one domain, and the site that proved the account may use it is recorded beside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This entity is the tenant boundary of every mail path.</b> A mailbox, an alias and a DKIM key are
/// reached through a domain row the caller's tenant filter shows, so another account's domain answers
/// 404 and takes everything under it with it.
/// </para>
/// <para>
/// <b>Ownership is a fact about a site, not a claim.</b> A domain is accepted only when it is the
/// primary domain of a site the same account owns (no outbound lookup, no first-come claim that could
/// be used to hijack someone else's name). <see cref="SiteId"/> records that site; the name is unique
/// across the whole server by a database index, because two accounts receiving mail for one domain is
/// a delivery error the host could not resolve.
/// </para>
/// </remarks>
public sealed class MailDomain
{
    /// <summary>The DKIM keys of this domain, newest last as the database returns them; ordered by callers.</summary>
    private readonly List<DkimKey> _dkimKeys = [];

    /// <summary>The row's identity, and the only identifier a customer's request may name.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account that owns the domain. Every tenant-scoped query is closed over this column.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The domain name, lower-case, unique across the server.</summary>
    public string Name { get; private set; }

    /// <summary>The site whose primary domain this is, which is what proved the account may use the name.</summary>
    public Guid SiteId { get; private set; }

    /// <summary>The instant the domain was added.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The domain's DKIM keys, active, retiring and retired.</summary>
    public IReadOnlyCollection<DkimKey> DkimKeys
    {
        get
        {
            return _dkimKeys;
        }
    }

    /// <summary>The key the host signs outgoing mail with, or <c>null</c> for a domain that has none yet.</summary>
    public DkimKey? ActiveKey
    {
        get
        {
            return _dkimKeys.SingleOrDefault(key =>
            {
                return key.State == DkimKeyState.Active;
            });
        }
    }

    /// <summary>Records a domain the agent has been asked to host.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="name">The domain, lower-case.</param>
    /// <param name="siteId">The site that proves ownership.</param>
    /// <param name="createdAt">The creation instant, taken from <see cref="IClock"/>.</param>
    public MailDomain(Guid id, Guid accountId, string name, Guid siteId, DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        Name = name;
        SiteId = siteId;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private MailDomain()
    {
        Name = string.Empty;
    }

    /// <summary>Makes <paramref name="next"/> the signing key; the previous active key keeps its record while it retires.</summary>
    /// <param name="next">A freshly generated key for this domain.</param>
    /// <param name="at">The instant of the rotation.</param>
    /// <remarks>
    /// A key already retiring is expired first, so at most one key is active and one retiring: a message
    /// signed before the rotation still verifies, and the set of published records stays bounded.
    /// </remarks>
    public void RotateDkim(DkimKey next, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(next);

        foreach (var key in _dkimKeys)
        {
            key.Expire();
            key.Retire(at);
        }

        _dkimKeys.Add(next);
    }
}
