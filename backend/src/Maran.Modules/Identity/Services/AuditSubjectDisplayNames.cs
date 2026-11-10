using System.Globalization;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Resolves the operator-facing text of an audit entry's subject in the current request's culture, for
/// the entries whose stored subject is a machine line: <c>intact rowsChecked=164</c>,
/// <c>disk=90;cpu=off;memory=off -> disk=85;cpu=80;memory=off</c>, <c>Warning -> Information</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The stored subject is not rewritten and not translated.</b> The journal is hash-chained, an
/// export and a grep read the stored line, and a row written in one language would stay in it. The
/// line is read back here, per request, and spelled in the reader's language, exactly as
/// <see cref="AuditActionDisplayNames"/> names the action beside the constant. The DTO keeps both.
/// </para>
/// <para>
/// <b>The set is closed and small on purpose:</b> only the actions whose producers spell their subject
/// as a machine line are read. Every other subject is already a name, an address or an id the reader
/// recognises, and is returned as it is. So is a line that does not have the shape its producer writes
/// (an older release's, a marketplace module's): the fallback is the stored text, which is what the
/// screen showed before, and never a half-parsed fragment.
/// </para>
/// <para>
/// The spellings read here are the producers' own: <c>VerifyAuditChainCommandHandler</c> and
/// <c>AuditMaintenanceHandler</c> (the chain verdict), <see cref="Maran.Modules.Identity.Domain.Policies.AuditRetentionSubjectText"/>
/// (the retention window and run) and the Monitoring module's <c>SaveAlertThresholdsCommandHandler</c> and
/// <c>ChangeLogLevelCommandHandler</c>. A module may not reference another, so the thresholds' and the
/// log level's spellings are restated here and pinned by this module's tests.
/// </para>
/// </remarks>
public sealed class AuditSubjectDisplayNames
{
    /// <summary>The prefix a subject text's resx key carries, so these cannot collide with other names.</summary>
    private const string KeyPrefix = "AuditSubject";

    /// <summary>The word the chain verdict starts with when the chain held.</summary>
    private const string Intact = "intact";

    /// <summary>The word the chain verdict starts with when it did not.</summary>
    private const string Broken = "broken";

    /// <summary>The word the retention window is spelled with when there is no limit.</summary>
    private const string Indefinite = "indefinite";

    /// <summary>The word the retention cutoff is spelled with when retention was off.</summary>
    private const string NoCutoff = "none";

    /// <summary>The word a threshold is spelled with when the monitor does not watch it.</summary>
    private const string Off = "off";

    /// <summary>The word that ends a retention run line the run did not finish.</summary>
    private const string Failed = "failed";

    /// <summary>The separator the change lines put between the value before and the value after.</summary>
    private const string ChangeArrow = " -> ";

    /// <summary>Resolves this module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public AuditSubjectDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Spells one entry's subject as an operator reads it.</summary>
    /// <param name="action">The machine-stable action the entry recorded.</param>
    /// <param name="subject">The stored subject.</param>
    /// <returns>
    /// The localized text for an action whose subject is a machine line in the shape its producer
    /// writes; otherwise <paramref name="subject"/> itself.
    /// </returns>
    public string Of(string action, string subject)
    {
        var text = action switch
        {
            IdentityAuditActions.AuditChainVerified => ChainVerdict(subject),
            IdentityAuditActions.AuditRetentionChanged => RetentionChange(subject),
            IdentityAuditActions.AuditRetentionRun => RetentionRun(subject),
            MonitoringAuditActions.AlertThresholdsSaved => ThresholdsChange(subject),
            MonitoringAuditActions.PanelLogLevelChanged => LogLevelChange(subject),
            _ => null,
        };

        return text ?? subject;
    }

    /// <summary>Reads <c>intact rowsChecked=N</c> or <c>broken break=B firstBrokenChainIndex=I rowsChecked=N</c>.</summary>
    /// <param name="subject">The stored subject.</param>
    /// <returns>The localized verdict, or <c>null</c> when the line is not in that shape.</returns>
    private string? ChainVerdict(string subject)
    {
        var fields = FieldsOf(subject, out var verdict);
        if (!TryCount(fields, "rowsChecked", out var rows))
        {
            return null;
        }

        if (string.Equals(verdict, Intact, StringComparison.Ordinal))
        {
            return _displayNames[KeyPrefix + "ChainIntact", rows];
        }

        if (!string.Equals(verdict, Broken, StringComparison.Ordinal)
            || !fields.TryGetValue("break", out var breakName)
            || !Enum.TryParse<AuditChainBreak>(breakName, ignoreCase: false, out var kind))
        {
            return null;
        }

        var kindName = Resolve(KeyPrefix + "ChainBreak" + kind, breakName);

        return TryCount(fields, "firstBrokenChainIndex", out var index)
            ? _displayNames[KeyPrefix + "ChainBroken", kindName, index, rows]
            : _displayNames[KeyPrefix + "ChainBrokenNoIndex", kindName, rows];
    }

    /// <summary>Reads <c>from &lt;old&gt; to &lt;new&gt;</c>, each a number of days or <c>indefinite</c>.</summary>
    /// <param name="subject">The stored subject.</param>
    /// <returns>The localized change, or <c>null</c> when the line is not in that shape.</returns>
    private string? RetentionChange(string subject)
    {
        var words = subject.Split(' ');
        if (words.Length != 4
            || !string.Equals(words[0], "from", StringComparison.Ordinal)
            || !string.Equals(words[2], "to", StringComparison.Ordinal)
            || !TryWindow(words[1], out var before)
            || !TryWindow(words[3], out var after))
        {
            return null;
        }

        return _displayNames[KeyPrefix + "RetentionChange", before, after];
    }

    /// <summary>
    /// Reads <c>retentionDays=W cutoff=C purged=P throughChainIndex=I</c> or <c>retentionDays=W failed</c>.
    /// </summary>
    /// <param name="subject">The stored subject.</param>
    /// <returns>The localized run, or <c>null</c> when the line is not in that shape.</returns>
    private string? RetentionRun(string subject)
    {
        var fields = FieldsOf(subject, out _);
        if (!fields.TryGetValue("retentionDays", out var windowText) || !TryWindow(windowText, out var window))
        {
            return null;
        }

        if (subject.EndsWith(" " + Failed, StringComparison.Ordinal))
        {
            return _displayNames[KeyPrefix + "RetentionRunFailed", window];
        }

        if (!fields.TryGetValue("cutoff", out var cutoffText)
            || !TryCount(fields, "purged", out var purged)
            || !TryCount(fields, "throughChainIndex", out var index))
        {
            return null;
        }

        string cutoff;
        if (string.Equals(cutoffText, NoCutoff, StringComparison.Ordinal))
        {
            cutoff = _displayNames[KeyPrefix + "NoCutoff"];
        }
        else if (DateTimeOffset.TryParse(
            cutoffText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var instant))
        {
            cutoff = _displayNames[KeyPrefix + "Cutoff", instant];
        }
        else
        {
            return null;
        }

        return _displayNames[KeyPrefix + "RetentionRun", window, cutoff, purged, index];
    }

    /// <summary>Reads <c>disk=D;cpu=C;memory=M -> disk=D;cpu=C;memory=M</c>, a figure being a percent or <c>off</c>.</summary>
    /// <param name="subject">The stored subject.</param>
    /// <returns>The localized change, or <c>null</c> when the line is not in that shape.</returns>
    private string? ThresholdsChange(string subject)
    {
        var halves = subject.Split(ChangeArrow);
        if (halves.Length != 2 || !TryThresholds(halves[0], out var before) || !TryThresholds(halves[1], out var after))
        {
            return null;
        }

        return _displayNames[KeyPrefix + "Change", before, after];
    }

    /// <summary>Reads <c>Warning -> Information</c>, each side the name of a <see cref="PanelLogLevel"/>.</summary>
    /// <param name="subject">The stored subject.</param>
    /// <returns>The localized change, or <c>null</c> when the line is not in that shape.</returns>
    private string? LogLevelChange(string subject)
    {
        var halves = subject.Split(ChangeArrow);
        if (halves.Length != 2
            || !Enum.TryParse<PanelLogLevel>(halves[0], ignoreCase: false, out var before)
            || !Enum.TryParse<PanelLogLevel>(halves[1], ignoreCase: false, out var after))
        {
            return null;
        }

        return _displayNames[KeyPrefix + "Change", LevelName(before), LevelName(after)];
    }

    /// <summary>Spells one side of a thresholds line.</summary>
    /// <param name="text">For example <c>disk=90;cpu=off;memory=80</c>.</param>
    /// <param name="spelled">The localized sentence.</param>
    /// <returns><c>true</c> when the text has exactly the three named figures.</returns>
    private bool TryThresholds(string text, out string spelled)
    {
        spelled = string.Empty;
        var parts = text.Split(';');
        if (parts.Length != 3
            || !TryFigure(parts[0], "disk", out var disk)
            || !TryFigure(parts[1], "cpu", out var cpu)
            || !TryFigure(parts[2], "memory", out var memory))
        {
            return false;
        }

        spelled = _displayNames[KeyPrefix + "Thresholds", disk, cpu, memory];

        return true;
    }

    /// <summary>Spells one <c>name=figure</c> part of a thresholds line.</summary>
    /// <param name="part">For example <c>cpu=80</c> or <c>cpu=off</c>.</param>
    /// <param name="name">The name the part must carry.</param>
    /// <param name="spelled">The figure as a percent, or the word for "not watched".</param>
    /// <returns><c>true</c> when the part carries that name and a figure.</returns>
    private bool TryFigure(string part, string name, out string spelled)
    {
        spelled = string.Empty;
        var prefix = name + "=";
        if (!part.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var figure = part[prefix.Length..];
        if (string.Equals(figure, Off, StringComparison.Ordinal))
        {
            spelled = _displayNames[KeyPrefix + "ThresholdOff"];
            return true;
        }

        if (!int.TryParse(figure, NumberStyles.None, CultureInfo.InvariantCulture, out var percent))
        {
            return false;
        }

        spelled = _displayNames[KeyPrefix + "ThresholdPercent", percent];

        return true;
    }

    /// <summary>Spells a retention window.</summary>
    /// <param name="text">A number of days or <c>indefinite</c>.</param>
    /// <param name="spelled">The localized window.</param>
    /// <returns><c>true</c> when the text is one of those.</returns>
    private bool TryWindow(string text, out string spelled)
    {
        spelled = string.Empty;
        if (string.Equals(text, Indefinite, StringComparison.Ordinal))
        {
            spelled = _displayNames[KeyPrefix + "RetentionIndefinite"];
            return true;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var days))
        {
            return false;
        }

        spelled = _displayNames[KeyPrefix + "RetentionDays", days];

        return true;
    }

    /// <summary>Names one log level as an operator reads it.</summary>
    /// <param name="level">The level.</param>
    /// <returns>The localized name.</returns>
    private string LevelName(PanelLogLevel level)
    {
        return _displayNames[KeyPrefix + "LogLevel" + level];
    }

    /// <summary>Resolves a resx entry, or the given fallback for one this build has no entry for.</summary>
    /// <param name="key">The entry's key.</param>
    /// <param name="fallback">What to answer when there is none.</param>
    /// <returns>The localized text, or <paramref name="fallback"/>.</returns>
    private string Resolve(string key, string fallback)
    {
        var localized = _displayNames[key];

        return localized.ResourceNotFound ? fallback : localized.Value;
    }

    /// <summary>Splits a machine line into its leading word and its <c>name=value</c> fields.</summary>
    /// <param name="subject">The stored subject, words separated by one space.</param>
    /// <param name="leading">The first word when it carries no <c>=</c>; otherwise empty.</param>
    /// <returns>The fields by name; a word with no <c>=</c> after the first is skipped.</returns>
    private static Dictionary<string, string> FieldsOf(string subject, out string leading)
    {
        leading = string.Empty;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var words = subject.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var position = 0; position < words.Length; position++)
        {
            var equals = words[position].IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                if (position == 0)
                {
                    leading = words[position];
                }

                continue;
            }

            fields[words[position][..equals]] = words[position][(equals + 1)..];
        }

        return fields;
    }

    /// <summary>Reads one field as a non-negative whole number.</summary>
    /// <param name="fields">The line's fields.</param>
    /// <param name="name">The field's name.</param>
    /// <param name="value">The number.</param>
    /// <returns><c>true</c> when the field exists and is such a number.</returns>
    private static bool TryCount(Dictionary<string, string> fields, string name, out long value)
    {
        value = 0;

        return fields.TryGetValue(name, out var text)
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
