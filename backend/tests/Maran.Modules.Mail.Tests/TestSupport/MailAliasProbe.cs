using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>Reads what an alias test needs to compare out of rows and recorded agent calls.</summary>
public static class MailAliasProbe
{
    /// <summary>The local parts of a set sent to the agent, in the order sent.</summary>
    /// <param name="aliases">The aliases sent.</param>
    /// <returns>The local parts.</returns>
    public static string[] LocalParts(IEnumerable<AgentMailAlias> aliases)
    {
        return aliases
            .Select(alias =>
            {
                return alias.LocalPart;
            })
            .ToArray();
    }

    /// <summary>The targets one alias of a sent set carries.</summary>
    /// <param name="aliases">The aliases sent.</param>
    /// <param name="localPart">The alias to read.</param>
    /// <returns>Its targets, in the order sent.</returns>
    public static IReadOnlyList<string> TargetsOf(IEnumerable<AgentMailAlias> aliases, string localPart)
    {
        return aliases
            .Single(alias =>
            {
                return alias.LocalPart == localPart;
            })
            .Targets;
    }

    /// <summary>The destinations of a row, by position.</summary>
    /// <param name="alias">The row, with its targets loaded.</param>
    /// <returns>The addresses in the order the owner gave them.</returns>
    public static string[] AddressesOf(MailAlias alias)
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
            .ToArray();
    }

    /// <summary>How many times the recording agent was asked to replace a domain's aliases.</summary>
    /// <param name="context">The assembled module.</param>
    /// <returns>The number of <c>SetAliases</c> calls.</returns>
    public static int SetAliasesCalls(MailTestContext context)
    {
        return context.Agent.Calls.Count(call =>
        {
            return call.StartsWith("SetAliases", StringComparison.Ordinal);
        });
    }

    /// <summary>The local parts of every alias row the context shows, in ordinal order.</summary>
    /// <param name="context">The assembled module.</param>
    /// <returns>The stored local parts; the catch-all appears as its marker.</returns>
    public static async Task<string[]> StoredLocalPartsAsync(MailTestContext context)
    {
        var parts = await context.Database.MailAliases
            .Select(alias => alias.LocalPart)
            .ToListAsync();

        return parts.Order(StringComparer.Ordinal).ToArray();
    }
}
