namespace Maran.Modules.Notifications.Domain.Entities;

/// <summary>
/// One address operator alerts are mailed to. The panel holds at most <see cref="MaxCount"/> of them,
/// and the one at position zero is the first recipient (the one older releases know as THE alert
/// address).
/// </summary>
/// <remarks>
/// <para>
/// <b>The list is replaced as a whole, so the entity has one change: where it stands.</b> Adding and
/// removing is the set's business (<c>SaveAlertRecipientsCommandHandler</c> keeps the rows whose
/// address stays, so a replace never deletes and re-inserts the same unique address in one save).
/// </para>
/// <para>
/// <b>The address is stored lower-cased.</b> The unique index is on the stored text, so two spellings
/// of one mailbox cannot both be rows; mail addresses are compared case-insensitively here on
/// purpose, because no real provider distinguishes the local part's case.
/// </para>
/// </remarks>
public sealed class AlertRecipient
{
    /// <summary>The most recipients the panel holds; more is a list nobody reads and a mail storm.</summary>
    public const int MaxCount = 10;

    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The address, lower-cased.</summary>
    public string Address { get; private set; }

    /// <summary>Where the address stands in the list; zero is the first recipient.</summary>
    public int Position { get; private set; }

    /// <summary>When the address was added to the list.</summary>
    public DateTimeOffset AddedAt { get; private set; }

    /// <summary>Creates a recipient.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="address">The address; stored lower-cased.</param>
    /// <param name="position">Where it stands in the list.</param>
    /// <param name="addedAt">When it was added, from the panel's clock.</param>
    public AlertRecipient(Guid id, string address, int position, DateTimeOffset addedAt)
    {
        Id = id;
        Address = address.ToLowerInvariant();
        Position = position;
        AddedAt = addedAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private AlertRecipient()
    {
        Address = string.Empty;
    }

    /// <summary>Moves the recipient to a place in the list.</summary>
    /// <param name="position">The new place; zero is the first recipient.</param>
    public void MoveTo(int position)
    {
        Position = position;
    }
}
