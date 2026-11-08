namespace Maran.Modules.Mail.Domain.Entities;

/// <summary>
/// One destination of an alias, forwarder or catch-all: an address the mail is delivered to.
/// </summary>
/// <remarks>
/// A row of its own, and not a delimited string on the alias, so a destination is never parsed back out
/// of a field a stray separator could split. Scoped through its required alias, like a DKIM key is
/// through its domain.
/// </remarks>
public sealed class MailAliasTarget
{
    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The alias this destination belongs to.</summary>
    public Guid MailAliasId { get; private set; }

    /// <summary>The alias, loaded when the target is read through it.</summary>
    public MailAlias MailAlias { get; private set; } = null!;

    /// <summary>The destination address, lower-case.</summary>
    public string Address { get; private set; }

    /// <summary>The destination's place in the alias's list, so the order a customer typed is the order shown.</summary>
    public int Position { get; private set; }

    /// <summary>Records one destination.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="mailAliasId">The alias it belongs to.</param>
    /// <param name="address">The destination address, already normalised.</param>
    /// <param name="position">Its place in the list, from zero.</param>
    public MailAliasTarget(Guid id, Guid mailAliasId, string address, int position)
    {
        Id = id;
        MailAliasId = mailAliasId;
        Address = address;
        Position = position;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private MailAliasTarget()
    {
        Address = string.Empty;
    }
}
