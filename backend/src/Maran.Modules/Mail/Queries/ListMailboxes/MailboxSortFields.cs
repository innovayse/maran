namespace Maran.Modules.Mail.Queries.ListMailboxes;

/// <summary>
/// The public names the mailboxes list sorts by — the <c>sort</c> parameter's vocabulary, written once and
/// read by <see cref="ListMailboxesSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class MailboxSortFields
{
    /// <summary>The full address, as the list shows it.</summary>
    public const string Address = "address";

    /// <summary>When the mailbox was created.</summary>
    public const string CreatedAt = "createdAt";

    /// <summary>The mailbox's size limit.</summary>
    public const string QuotaMib = "quotaMib";
}
