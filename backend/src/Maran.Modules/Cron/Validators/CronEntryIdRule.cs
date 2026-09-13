namespace Maran.Modules.Cron.Validators;

/// <summary>
/// What this panel accepts as a cron entry identifier. Shared by every operation that names one
/// entry, so update, delete, enablement and output cannot drift into accepting different ids.
/// </summary>
/// <remarks>
/// <para>
/// A mirror of the agent's own <c>CronEntryId</c>: a plain lowercase hyphenated uuid, and nothing
/// else. No braces, no <c>urn:uuid:</c> prefix, no uppercase, no unhyphenated 32-character form —
/// an id has one spelling because on the far side it names a file.
/// </para>
/// <para>
/// <b>The narrowness is the point, and it is a security property rather than tidiness.</b> The agent
/// interpolates this id into three paths under the account's home, and a path join with an absolute
/// string REPLACES the path it is joined to. The agent refuses anything but this shape for exactly
/// that reason; refusing it here too means a caller who sends <c>../../../etc/cron.d/evil</c> is
/// told their id is malformed rather than being handed the agent's own refusal, and the panel never
/// becomes the layer that widened what the agent narrowed.
/// </para>
/// <para>
/// Expressed as a pattern rather than by parsing as a <see cref="Guid"/>. <see cref="Guid"/> would
/// accept braces, uppercase and the unhyphenated form and then re-emit a canonical spelling, which
/// is a normalisation this panel must not perform: the id the agent minted is the id the agent
/// stores, and quietly accepting other spellings of it hides from a caller that they sent one.
/// </para>
/// </remarks>
public static class CronEntryIdRule
{
    /// <summary>The lowercase hyphenated uuid form, anchored at both ends.</summary>
    /// <remarks>
    /// Anchored with <c>\A</c> and <c>\z</c> rather than <c>^</c> and <c>$</c>. In .NET <c>$</c>
    /// also matches immediately before a trailing newline, so an id followed by one would satisfy a
    /// <c>$</c>-anchored pattern — and a newline in a value bound for a file path is precisely what
    /// this rule exists to refuse.
    /// </remarks>
    public const string Pattern = @"\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z";
}
