using Maran.Modules.Mail.Domain.ValueObjects;

namespace Maran.Modules.Mail.Domain.Policies;

/// <summary>
/// How many deliveries outside the host one message to an alias or a catch-all of an account makes, the WHOLE
/// expansion counted: the figure the mail filter charges the sender's hourly limit for the recipient.
/// </summary>
/// <remarks>
/// <para>
/// <b>The MTA's expansion is recursive, so the count is too.</b> An alias that lists another alias of the same
/// account makes as many deliveries as that alias does; counting only the first list would let one recipient
/// stand for a hundred. Resolution follows the MTA's own order for an address: the entry of the exact address,
/// then the entry of the address without its extension (<c>name+tag@example.org</c> is delivered as
/// <c>name@example.org</c>), and for an address no entry and no mailbox claims, the domain's catch-all. A
/// mailbox is a place mail ends, not a list: delivered once, whatever the catch-all says.
/// </para>
/// <para>
/// <b>A cycle ends where it closes.</b> Each entry is expanded at most once per recipient, so two aliases that
/// list each other add what they both reach exactly once, never forever.
/// </para>
/// <para>
/// <b>The figure is capped at <see cref="ExpansionLimit"/></b>, the MTA's own
/// <c>virtual_alias_expansion_limit</c>: it stops expanding there, so a larger count is not a larger message.
/// A destination counts once however many lists name it, as the MTA delivers once per distinct address.
/// </para>
/// <para>
/// <b>Scope.</b> Only this account's entries are followed. A destination on another account's domain is a
/// delivery to someone else and counts one; if that account has an alias there, its expansion is not added to
/// this account's figure (the threat note names this as an accepted residual: it needs a second account whose
/// owner lists outside addresses).
/// </para>
/// </remarks>
public static class MailAliasFanoutPolicy
{
    /// <summary>The most deliveries the MTA makes for one recipient: the rendered <c>virtual_alias_expansion_limit</c>.</summary>
    public const int ExpansionLimit = 100;

    /// <summary>The character the MTA cuts an address extension at: the rendered <c>recipient_delimiter</c>.</summary>
    public const char ExtensionDelimiter = '+';

    /// <summary>The key the mail filter reads for a recipient: <c>local@domain</c>, or <c>@domain</c> for a catch-all.</summary>
    /// <param name="entry">The alias or catch-all.</param>
    /// <returns>The lower-cased key.</returns>
    public static string KeyOf(MailAliasEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var domain = entry.DomainName.ToLowerInvariant();

        return entry.LocalPart == MailboxAddressPolicy.CatchAllLocalPart
            ? string.Concat("@", domain)
            : MailboxAddressPolicy.Compose(entry.LocalPart.ToLowerInvariant(), domain);
    }

    /// <summary>Counts the outside deliveries of every alias and catch-all of the account.</summary>
    /// <param name="accountDomainNames">The names of the account's own mail domains.</param>
    /// <param name="entries">Every alias, forwarder and catch-all of the account.</param>
    /// <param name="mailboxAddresses">The address of every mailbox of the account.</param>
    /// <returns>
    /// The recipient key (see <see cref="KeyOf"/>) and its count, <c>0..=<see cref="ExpansionLimit"/></c>. An
    /// entry whose whole expansion stays inside the account is present with count <c>0</c>: it IS an alias, so
    /// the filter must not fall through to the domain's catch-all for it, and charges it the one delivery a
    /// mailbox costs.
    /// </returns>
    public static IReadOnlyDictionary<string, int> Resolve(
        IReadOnlyCollection<string> accountDomainNames,
        IReadOnlyCollection<MailAliasEntry> entries,
        IReadOnlyCollection<string> mailboxAddresses)
    {
        ArgumentNullException.ThrowIfNull(accountDomainNames);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(mailboxAddresses);

        var byKey = new Dictionary<string, MailAliasEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            byKey[KeyOf(entry)] = entry;
        }

        var mailboxes = new HashSet<string>(mailboxAddresses, StringComparer.OrdinalIgnoreCase);
        var fanouts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, entry) in byKey)
        {
            fanouts[key] = Math.Min(CountOutside(entry, accountDomainNames, byKey, mailboxes), ExpansionLimit);
        }

        return fanouts;
    }

    /// <summary>Counts the distinct outside destinations one entry reaches, following the entries of the account that it names.</summary>
    /// <param name="start">The entry to expand.</param>
    /// <param name="accountDomainNames">The names of the account's own mail domains.</param>
    /// <param name="byKey">Every entry of the account by its recipient key.</param>
    /// <param name="mailboxes">The address of every mailbox of the account.</param>
    /// <returns>The count, at most <see cref="ExpansionLimit"/>.</returns>
    /// <remarks>
    /// Walks with an explicit stack, not recursion, so a chain of any length costs heap and not call depth. An
    /// entry is expanded once per recipient (<c>expanded</c>), which ends a cycle or a diamond.
    /// </remarks>
    private static int CountOutside(
        MailAliasEntry start,
        IReadOnlyCollection<string> accountDomainNames,
        Dictionary<string, MailAliasEntry> byKey,
        HashSet<string> mailboxes)
    {
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        var outside = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<MailAliasEntry>();
        pending.Push(start);

        while (pending.Count > 0 && outside.Count < ExpansionLimit)
        {
            var entry = pending.Pop();
            if (!expanded.Add(KeyOf(entry)))
            {
                continue;
            }

            foreach (var target in entry.Targets)
            {
                var address = target.ToLowerInvariant();
                if (MailAliasTargetPolicy.IsExternal(address, accountDomainNames))
                {
                    outside.Add(address);

                    continue;
                }

                var next = Find(address, byKey, mailboxes);
                if (next is not null)
                {
                    pending.Push(next);
                }
            }
        }

        return outside.Count;
    }

    /// <summary>Finds the entry the MTA expands an address of the account by, or <c>null</c> when it ends in a mailbox or nowhere.</summary>
    /// <param name="address">A lower-cased destination on one of the account's domains.</param>
    /// <param name="byKey">Every entry of the account by its recipient key.</param>
    /// <param name="mailboxes">The address of every mailbox of the account.</param>
    /// <returns>The entry, or <c>null</c>.</returns>
    private static MailAliasEntry? Find(string address, Dictionary<string, MailAliasEntry> byKey, HashSet<string> mailboxes)
    {
        if (byKey.TryGetValue(address, out var exact))
        {
            return exact;
        }

        var domain = MailboxAddressPolicy.DomainOf(address);
        var at = address.LastIndexOf('@');
        var cut = address.IndexOf(ExtensionDelimiter, StringComparison.Ordinal);
        var bare = cut >= 0 && cut < at ? string.Concat(address.AsSpan(0, cut), "@", domain) : address;
        if (bare != address && byKey.TryGetValue(bare, out var stripped))
        {
            return stripped;
        }

        if (mailboxes.Contains(address) || mailboxes.Contains(bare))
        {
            return null;
        }

        return byKey.GetValueOrDefault(string.Concat("@", domain));
    }
}
