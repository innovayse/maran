namespace Maran.Modules.Mail.Queries.ListMailQueue;

/// <summary>
/// The public names the mail queue list sorts by — the <c>sort</c> parameter's vocabulary, written once
/// and read by <see cref="ListMailQueueSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class MailQueueSortFields
{
    /// <summary>When the message was accepted.</summary>
    public const string ArrivedAt = "arrivedAt";

    /// <summary>The envelope sender, compared ordinally ignoring case.</summary>
    public const string Sender = "sender";

    /// <summary>How many recipients the message still has.</summary>
    public const string Recipients = "recipients";

    /// <summary>The message size in bytes.</summary>
    public const string Size = "size";
}
