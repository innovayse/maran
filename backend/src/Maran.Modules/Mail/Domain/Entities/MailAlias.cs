using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.Policies;

namespace Maran.Modules.Mail.Domain.Entities;

/// <summary>
/// An address that is not a mailbox: an alias to mailboxes of the account, a forwarder to an outside
/// address, or the catch-all of a domain.
/// </summary>
/// <remarks>
/// <para>
/// The catch-all is stored as an alias whose local part is <see cref="MailboxAddressPolicy.CatchAllLocalPart"/>,
/// a value no mailbox can have, so one unique index over the domain and the local part makes both "one
/// address, one meaning" and "at most one catch-all per domain" the database's rule and not a handler's.
/// That an alias and a mailbox may not share an address is a rule across two tables, which no index can
/// state; the handlers check it and say so.
/// </para>
/// <para>
/// Whether an alias is an <see cref="MailAliasKind.Alias"/> or a <see cref="MailAliasKind.Forwarder"/>
/// is decided by its targets and stated by the caller; the entity only carries the answer.
/// </para>
/// </remarks>
public sealed class MailAlias
{
    /// <summary>The destinations, in the order they were given.</summary>
    private readonly List<MailAliasTarget> _targets = [];

    /// <summary>The row's identity, and the only identifier a request may name.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account that owns the alias. Every tenant-scoped query is closed over this column.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The domain the alias lives in.</summary>
    public Guid MailDomainId { get; private set; }

    /// <summary>The domain, loaded when the alias is read with it.</summary>
    public MailDomain MailDomain { get; private set; } = null!;

    /// <summary>The part before the <c>@</c>, or <see cref="MailboxAddressPolicy.CatchAllLocalPart"/> for a catch-all.</summary>
    public string LocalPart { get; private set; }

    /// <summary>What the address is.</summary>
    public MailAliasKind Kind { get; private set; }

    /// <summary>The instant the alias was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The destinations; each carries its <see cref="MailAliasTarget.Position"/>, which orders them.</summary>
    public IReadOnlyCollection<MailAliasTarget> Targets
    {
        get
        {
            return _targets;
        }
    }

    /// <summary>Records an alias, forwarder or catch-all with its destinations.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="mailDomainId">The domain it lives in.</param>
    /// <param name="localPart">The local part, or the catch-all marker.</param>
    /// <param name="kind">What the address is.</param>
    /// <param name="targets">The destinations, already normalised, de-duplicated and bounded.</param>
    /// <param name="createdAt">The creation instant, taken from <see cref="IClock"/>.</param>
    public MailAlias(
        Guid id,
        Guid accountId,
        Guid mailDomainId,
        string localPart,
        MailAliasKind kind,
        IReadOnlyList<string> targets,
        DateTimeOffset createdAt)
    {
        Id = id;
        AccountId = accountId;
        MailDomainId = mailDomainId;
        LocalPart = localPart;
        Kind = kind;
        CreatedAt = createdAt;
        ReplaceTargetRows(targets);
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private MailAlias()
    {
        LocalPart = string.Empty;
    }

    /// <summary>Replaces the destinations with exactly the given list.</summary>
    /// <param name="targets">The new destinations, already normalised, de-duplicated and bounded.</param>
    /// <param name="kind">What the address is now that its destinations changed.</param>
    /// <returns>The rows that were dropped, so the caller can delete them from the context.</returns>
    public IReadOnlyList<MailAliasTarget> ReplaceTargets(IReadOnlyList<string> targets, MailAliasKind kind)
    {
        var dropped = _targets.ToList();
        _targets.Clear();
        Kind = kind;
        ReplaceTargetRows(targets);

        return dropped;
    }

    /// <summary>Builds the target rows for a list.</summary>
    /// <param name="targets">The destinations.</param>
    private void ReplaceTargetRows(IReadOnlyList<string> targets)
    {
        for (var position = 0; position < targets.Count; position++)
        {
            _targets.Add(new MailAliasTarget(Guid.NewGuid(), Id, targets[position], position));
        }
    }
}
