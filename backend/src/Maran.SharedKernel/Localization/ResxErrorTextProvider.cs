using System.Collections;
using System.Globalization;
using System.Resources;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.SharedKernel.Localization;

/// <summary>
/// Resolves user-facing text from the <c>.resx</c> resources each module registers for itself
/// (rules/csharp.md "The backend owns all user-facing message text"). A module never shares its
/// resource file with another module; this single, generic implementation tries every registered
/// <see cref="ResourceManager"/> in registration order and returns the first hit for the current
/// UI culture — set per request by <c>RequestLocalizationMiddleware</c> — so a module's error
/// codes and any other resx-backed key (e.g. its display-name key) resolve through the same
/// mechanism without SharedKernel knowing anything about a specific module's resources.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two ways this can be wrong, and both are now said out loud.</b> The fallback for a key no
/// module claims is the key itself, which is safe to render and impossible to notice: the customer
/// reads <c>SiteDomainTaken</c> where a sentence belongs, and nothing anywhere records that the
/// panel failed to translate its own error. So an unclaimed key is logged, once per resolution,
/// naming the key.
/// </para>
/// <para>
/// <b>And "first hit wins" is a rule with a loser — but only sometimes.</b> Several modules
/// deliberately declare the same key: <c>AccountNotFound</c> is owned six times over, because a
/// module may not read another module's resources and each therefore ships its own copy. That
/// duplication is correct and must stay silent. What is NOT correct is two tables giving one key
/// two different sentences — then registration order decides which text the customer reads for
/// which module's failure, and the module that lost renders somebody else's words. So the check is
/// on the TEXT, not on the key: a key claimed twice with identical text is the intended pattern,
/// and a key claimed twice with different text is reported once, at construction, naming both
/// tables. A warning rather than a throw, because a mistranslated message must not take a server
/// down and the warning already carries everything needed to fix it.
/// </para>
/// </remarks>
public sealed class ResxErrorTextProvider : IErrorTextProvider
{
    /// <summary>Pre-compiled log delegate for a key no registered table claims.</summary>
    private static readonly Action<ILogger, string, Exception?> LogUnclaimedKey =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(ResxErrorTextProvider)),
            "No module's resources claim the message key {MessageKey}; the key itself was rendered.");

    /// <summary>Pre-compiled log delegate for a key two modules give two different sentences.</summary>
    private static readonly Action<ILogger, string, string, Exception?> LogCollidingKey =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(2, nameof(ResxErrorTextProvider)),
            "Message key {MessageKey} has different text in more than one module ({ResourceTables}); "
            + "registration order decides which text is rendered.");

    /// <summary>The resource managers registered by every module that has shipped, in registration order.</summary>
    private readonly IReadOnlyList<ResourceManager> _resourceManagers;

    /// <summary>Where an unclaimed key is reported.</summary>
    private readonly ILogger<ResxErrorTextProvider> _logger;

    /// <summary>Creates the provider over every module-registered resource manager.</summary>
    /// <param name="resourceManagers">One <see cref="ResourceManager"/> per module's <c>Resources/Messages.resx</c> family.</param>
    /// <param name="logger">Where an unclaimed or colliding key is reported.</param>
    public ResxErrorTextProvider(IEnumerable<ResourceManager> resourceManagers, ILogger<ResxErrorTextProvider> logger)
    {
        _resourceManagers = resourceManagers.ToList();
        _logger = logger;

        ReportCollidingKeys();
    }

    /// <inheritdoc />
    public string Resolve(string code, params object[] arguments)
    {
        foreach (var manager in _resourceManagers)
        {
            var text = manager.GetString(code, CultureInfo.CurrentUICulture);
            if (text is not null)
            {
                return arguments.Length > 0 ? string.Format(CultureInfo.CurrentCulture, text, arguments) : text;
            }
        }

        // No module claims this key. The code itself is the safest fallback: still machine-stable,
        // never a stack trace or tool output (rules/security.md "Secrets"). It is logged because it
        // is otherwise indistinguishable, to everyone except the customer reading it, from a
        // message that resolved.
        LogUnclaimedKey(_logger, code, null);

        return code;
    }

    /// <summary>Logs every key two registered tables give two different sentences.</summary>
    /// <remarks>
    /// Reads the INVARIANT entries of each table, not a culture-specific set: the neutral resx is
    /// the file that defines what a module means by a key, and a translation missing from one
    /// language does not change that. A table that cannot be enumerated — a module shipped without
    /// its neutral resources — is skipped rather than fatal, because the consequence of missing a
    /// collision is a wrong sentence and the consequence of throwing here is no panel at all.
    /// </remarks>
    private void ReportCollidingKeys()
    {
        var texts = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var manager in _resourceManagers)
        {
            foreach (var (key, text) in EntriesOf(manager))
            {
                if (!texts.TryGetValue(key, out var byTable))
                {
                    byTable = new Dictionary<string, string>(StringComparer.Ordinal);
                    texts[key] = byTable;
                }

                byTable[manager.BaseName] = text;
            }
        }

        foreach (var (key, byTable) in texts)
        {
            if (byTable.Values.Distinct(StringComparer.Ordinal).Count() > 1)
            {
                LogCollidingKey(_logger, key, string.Join(", ", byTable.Keys), null);
            }
        }
    }

    /// <summary>Reads the key/text pairs one table defines, or nothing when the table cannot be read.</summary>
    /// <param name="manager">The table to enumerate.</param>
    /// <returns>The neutral-culture entries the table defines, in no particular order.</returns>
    /// <remarks>
    /// Declared as the concrete list it always builds rather than as <see cref="IEnumerable{T}"/>:
    /// every return path here is already materialised, so the interface bought nothing and cost the
    /// caller an interface dispatch per entry (CA1859).
    /// </remarks>
    private static List<KeyValuePair<string, string>> EntriesOf(ResourceManager manager)
    {
        ResourceSet? set;
        try
        {
            set = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
        }
        catch (MissingManifestResourceException)
        {
            return [];
        }

        if (set is null)
        {
            return [];
        }

        return set.Cast<DictionaryEntry>()
            .Where(entry => { return entry.Key is string && entry.Value is string; })
            .Select(entry => { return new KeyValuePair<string, string>((string)entry.Key, (string)entry.Value!); })
            .ToList();
    }
}
