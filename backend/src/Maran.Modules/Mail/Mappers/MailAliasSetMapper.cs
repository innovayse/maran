using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Models;

namespace Maran.Modules.Mail.Mappers;

/// <summary>
/// Translates the alias rows of one domain into the complete set the agent replaces them with.
/// Restates; decides nothing — which rows make up the domain, and what changes, is the handler's.
/// </summary>
/// <remarks>
/// The aliases are written in the order of their local parts, so the same rows always make the same
/// call and a retry after a lost answer sends exactly what the first attempt did.
/// </remarks>
public static class MailAliasSetMapper
{
    /// <summary>Restates the rows as they are.</summary>
    /// <param name="aliases">Every alias row of ONE domain, with targets loaded.</param>
    /// <returns>The set the agent would hold if it were sent now.</returns>
    public static MailAliasSet From(IEnumerable<MailAlias> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);

        return Build(aliases, replacedLocalPart: null, replacementTargets: null);
    }

    /// <summary>Restates the rows with one address added, or with its targets replaced when the address exists.</summary>
    /// <param name="aliases">Every alias row of ONE domain, with targets loaded.</param>
    /// <param name="localPart">The address's local part, or the catch-all marker.</param>
    /// <param name="targets">The destinations the address will have, already normalised.</param>
    /// <returns>The set the agent should hold after the change.</returns>
    public static MailAliasSet With(IEnumerable<MailAlias> aliases, string localPart, IReadOnlyList<string> targets)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        ArgumentNullException.ThrowIfNull(localPart);
        ArgumentNullException.ThrowIfNull(targets);

        return Build(aliases, localPart, targets);
    }

    /// <summary>Restates the rows with one address left out.</summary>
    /// <param name="aliases">Every alias row of ONE domain, with targets loaded.</param>
    /// <param name="localPart">The local part of the address that is going away, or the catch-all marker.</param>
    /// <returns>The set the agent should hold after the deletion.</returns>
    public static MailAliasSet Without(IEnumerable<MailAlias> aliases, string localPart)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        ArgumentNullException.ThrowIfNull(localPart);

        return Build(aliases, localPart, replacementTargets: null);
    }

    /// <summary>Builds the set from the rows, dropping or replacing the one address named.</summary>
    /// <param name="aliases">Every alias row of one domain.</param>
    /// <param name="replacedLocalPart">The address to drop or replace, or <c>null</c> to change nothing.</param>
    /// <param name="replacementTargets">The targets that take its place, or <c>null</c> to drop it.</param>
    /// <returns>The complete set.</returns>
    private static MailAliasSet Build(
        IEnumerable<MailAlias> aliases,
        string? replacedLocalPart,
        IReadOnlyList<string>? replacementTargets)
    {
        var entries = new List<(string LocalPart, IReadOnlyList<string> Targets)>();

        foreach (var alias in aliases)
        {
            if (string.Equals(alias.LocalPart, replacedLocalPart, StringComparison.Ordinal))
            {
                continue;
            }

            entries.Add((alias.LocalPart, OrderedAddresses(alias)));
        }

        if (replacedLocalPart is not null && replacementTargets is not null)
        {
            entries.Add((replacedLocalPart, replacementTargets));
        }

        IReadOnlyList<string>? catchAllTargets = null;
        foreach (var entry in entries)
        {
            if (entry.LocalPart == MailboxAddressPolicy.CatchAllLocalPart)
            {
                catchAllTargets = entry.Targets;
            }
        }

        var agentAliases = entries
            .Where(entry =>
            {
                return entry.LocalPart != MailboxAddressPolicy.CatchAllLocalPart;
            })
            .OrderBy(entry =>
            {
                return entry.LocalPart;
            }, StringComparer.Ordinal)
            .Select(entry =>
            {
                return new AgentMailAlias(entry.LocalPart, entry.Targets);
            })
            .ToList();

        return new MailAliasSet(agentAliases, catchAllTargets);
    }

    /// <summary>The targets of a row in the order the owner gave them.</summary>
    /// <param name="alias">The row.</param>
    /// <returns>The addresses, by position.</returns>
    private static List<string> OrderedAddresses(MailAlias alias)
    {
        return alias.Targets
            .OrderBy(target =>
            {
                return target.Position;
            })
            .Select(target =>
            {
                return target.Address;
            })
            .ToList();
    }
}
