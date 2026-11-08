using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Constants;

namespace Maran.Modules.Accounts.Domain.Entities;

/// <summary>
/// A reseller: a named business that owns a set of customer accounts and plans and sells them inside
/// the <see cref="ResellerEnvelope"/> an administrator gave it. Exactly one login, created by
/// Identity from the <c>ResellerCreated</c> event, signs in for it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Name"/> is also that login's name, so it shares one namespace with account names (a
/// login name is unique across the panel); it follows the account name alphabet and length.
/// </para>
/// <para>
/// <b>Why <see cref="AllocationRevision"/> exists.</b> Two requests that each create the reseller's
/// last permitted account must not both succeed. Each reads the counts, finds one slot free and
/// writes; only a write to a row both of them must change can make the database refuse the second.
/// Every handler that changes what the reseller owns therefore calls
/// <see cref="NoteAllocationChanged"/>, which moves the revision, and the revision is the row's
/// concurrency token: the loser's update matches no row and the save raises a concurrency exception.
/// A counter and not the database's own row version, because an unmodified entity produces no UPDATE
/// at all, so a row version would never be compared.
/// </para>
/// </remarks>
public sealed class Reseller
{
    /// <summary>
    /// The longest reseller name, in characters: the same as an account name, because the name is
    /// also the login name and a login name is one namespace across the panel.
    /// </summary>
    public const int NameMaxLength = 32;

    /// <summary>The reseller's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The reseller's unique short name, which is also its login name.</summary>
    public string Name { get; private set; }

    /// <summary>The reseller's contact address: where its login's invitation and password-reset mails go.</summary>
    public string ContactEmail { get; private set; }

    /// <summary>The reseller's language, one of <see cref="PanelLanguages.Supported"/>: the language of its invitation.</summary>
    public string Language { get; private set; }

    /// <summary>The reseller's current lifecycle state.</summary>
    public ResellerStatus Status { get; private set; }

    /// <summary>What the administrator sold the reseller.</summary>
    public ResellerEnvelope Envelope { get; private set; }

    /// <summary>
    /// How many times what the reseller owns has changed; the row's concurrency token. See the type's
    /// remarks for why a counter is needed. Its value means nothing to anyone but the database.
    /// </summary>
    public int AllocationRevision { get; private set; }

    /// <summary>The instant the reseller was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Creates a new reseller in the <see cref="ResellerStatus.Active"/> state.</summary>
    /// <param name="id">The reseller's identity.</param>
    /// <param name="name">The reseller's unique, login-name-safe short name.</param>
    /// <param name="contactEmail">The reseller's contact address.</param>
    /// <param name="language">The reseller's language, one of <see cref="PanelLanguages.Supported"/>.</param>
    /// <param name="envelope">What the administrator sold the reseller.</param>
    /// <param name="createdAt">The instant of creation, taken from <see cref="IClock"/>.</param>
    /// <exception cref="ArgumentException">Thrown when a text argument is empty or white space.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="envelope"/> is null.</exception>
    public Reseller(
        Guid id,
        string name,
        string contactEmail,
        string language,
        ResellerEnvelope envelope,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(contactEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentNullException.ThrowIfNull(envelope);

        Id = id;
        Name = name;
        ContactEmail = contactEmail;
        Language = language;
        Envelope = envelope;
        Status = ResellerStatus.Active;
        CreatedAt = createdAt;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private Reseller()
    {
        Name = string.Empty;
        ContactEmail = string.Empty;
        Language = PanelLanguages.English;
        Envelope = ResellerEnvelope.Of(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, AccountFeature.None, RuntimeLimits.Unlimited());
    }

    /// <summary>
    /// Gives the reseller a new name. The name is also its login name, so the caller has already
    /// asked that the login follow it and that no account or reseller holds the name.
    /// </summary>
    /// <param name="name">The new, login-name-safe short name.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is empty or white space.</exception>
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
    }

    /// <summary>
    /// Replaces where the reseller is reached and in which language: the address its login's mails go
    /// to and the language of the mails written to it.
    /// </summary>
    /// <param name="contactEmail">The new contact address.</param>
    /// <param name="language">The new language, one of <see cref="PanelLanguages.Supported"/>.</param>
    /// <exception cref="ArgumentException">Thrown when a text argument is empty or white space.</exception>
    public void UpdateContact(string contactEmail, string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contactEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        ContactEmail = contactEmail;
        Language = language;
    }

    /// <summary>
    /// Replaces what the reseller was sold. Lowering it below what the reseller already owns is
    /// allowed: existing plans and accounts keep working and only new writes are refused.
    /// </summary>
    /// <param name="envelope">The new envelope.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="envelope"/> is null.</exception>
    public void ChangeEnvelope(ResellerEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        Envelope = envelope;
    }

    /// <summary>Marks the reseller suspended. Idempotent: suspending an already-suspended reseller is a no-op.</summary>
    public void Suspend()
    {
        Status = ResellerStatus.Suspended;
    }

    /// <summary>Marks the reseller active. Idempotent: reactivating an already-active reseller is a no-op.</summary>
    public void Reactivate()
    {
        Status = ResellerStatus.Active;
    }

    /// <summary>
    /// Records that what the reseller owns is changing in this transaction, so a concurrent change
    /// is refused by the database instead of both being accepted against one free slot. Call it in
    /// every handler that adds, moves or removes an account or a plan of the reseller, or edits a
    /// plan's disk quota.
    /// </summary>
    public void NoteAllocationChanged()
    {
        AllocationRevision++;
    }
}
