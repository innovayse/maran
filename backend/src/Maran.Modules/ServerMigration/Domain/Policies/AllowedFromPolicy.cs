using System.Net;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.ServerMigration.Domain.Policies;

/// <summary>
/// What the panel accepts as the list of addresses a remote server may take its key from: addresses and ranges
/// separated by commas, each read by the strict rule the panel reads a firewall range with. The list is written
/// into the <c>from=</c> option of the line the administrator installs, so only text this policy produced ever
/// reaches that line.
/// </summary>
public static class AllowedFromPolicy
{
    /// <summary>The separator between entries, in the text stored and in the <c>from=</c> option.</summary>
    private const char Separator = ',';

    /// <summary>Reads an <c>allowedFrom</c> text into the canonical list that is stored.</summary>
    /// <param name="text">The text as typed; <c>null</c> or white space means no restriction.</param>
    /// <param name="canonical">
    /// The canonical list (<c>address/prefix</c> entries joined by commas), or <c>null</c> for no restriction;
    /// <c>null</c> as well when the text was refused.
    /// </param>
    /// <returns>True when the text is empty or every entry is a strictly valid address or range.</returns>
    public static bool TryRead(string? text, out string? canonical)
    {
        canonical = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (text.Length > RemoteServerFieldLimits.AllowedFromMaxLength)
        {
            return false;
        }

        var entries = text.Split(Separator);
        if (entries.Length > RemoteServerFieldLimits.AllowedFromMaxEntries)
        {
            return false;
        }

        var rules = new List<string>(entries.Length);
        foreach (var entry in entries)
        {
            if (!CidrRule.TryParse(entry.Trim(), out var rule))
            {
                return false;
            }

            rules.Add(rule!.ToString());
        }

        canonical = string.Join(Separator, rules);
        return true;
    }

    /// <summary>Says whether a caller's address falls inside a canonical list this policy produced.</summary>
    /// <param name="canonical">The canonical list, or <c>null</c> for no restriction.</param>
    /// <param name="address">The caller's address, spelled by <c>ClientAddress</c>.</param>
    /// <returns>
    /// <c>true</c> when there is no restriction or the address is inside one of the ranges. An address that cannot
    /// be read as an address (the placeholder for an unknown peer included) is outside every range, so a restriction
    /// never admits a caller it could not identify.
    /// </returns>
    public static bool Admits(string? canonical, string address)
    {
        if (canonical is null)
        {
            return true;
        }

        if (!IPAddress.TryParse(address, out var candidate))
        {
            return false;
        }

        foreach (var entry in canonical.Split(Separator))
        {
            if (CidrRule.TryParse(entry, out var rule) && rule!.Contains(candidate))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads an <c>allowedFrom</c> text the validator already accepted, refusing loudly if it did not.</summary>
    /// <param name="text">The text as typed; <c>null</c> or white space means no restriction.</param>
    /// <returns>The canonical list, or <c>null</c> for no restriction.</returns>
    /// <exception cref="ArgumentException">
    /// The text is not a valid list. A handler calls this after its validator, so reaching it with such text means the
    /// validator was bypassed — and a restriction that cannot be read must never become "no restriction" by
    /// accident, because it is written into the line the administrator installs.
    /// </exception>
    public static string? Require(string? text)
    {
        if (!TryRead(text, out var canonical))
        {
            throw new ArgumentException("The key restriction is not a list of addresses and ranges.", nameof(text));
        }

        return canonical;
    }
}
