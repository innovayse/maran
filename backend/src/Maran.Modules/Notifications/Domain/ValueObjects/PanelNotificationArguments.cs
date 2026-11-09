using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Notifications.Domain.ValueObjects;

/// <summary>
/// The few short words a notification's sentence is built around — a domain, a figure, a service
/// name — kept apart from the sentence, which is resolved when the feed is read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stored as one column, parsed here and nowhere else.</b> The values are joined with the unit
/// separator (<see cref="Separator"/>), a control character, so an argument can never contain it:
/// <see cref="From"/> removes every control character from every value, which is also what makes
/// <see cref="Parse"/> exact. A comma-separated column would need an escaping rule, and an escaping
/// rule is a place for a stored value to read back as something it was not.
/// </para>
/// <para>
/// <b>Bounded on the way in.</b> At most <see cref="MaxCount"/> values of at most
/// <see cref="MaxValueLength"/> characters, so the column has a width that is known rather than
/// hoped for. Positions matter — the sentence says "the first value", "the second" — so an empty
/// value in the middle is kept, and only trailing empties are lost, which a sentence cannot tell
/// from missing ones.
/// </para>
/// <para>
/// <b>Plain text, always.</b> The values reach the SPA as text to display; the SPA never renders
/// them as markup. A value that came from a service manager or a task subject is attacker-
/// influenced, and the bound here is a bound on size, not a claim that the content is safe.
/// </para>
/// </remarks>
public sealed class PanelNotificationArguments
{
    /// <summary>How many values a notification keeps.</summary>
    public const int MaxCount = 4;

    /// <summary>How many characters one value keeps.</summary>
    public const int MaxValueLength = 200;

    /// <summary>
    /// The width of the stored column: every value at its longest and the separators between them,
    /// rounded up to a figure that reads as a decision rather than a sum.
    /// </summary>
    public const int StoredMaxLength = 1000;

    /// <summary>The character the stored values are joined with: ASCII unit separator.</summary>
    public const char Separator = '\u001f';

    /// <summary>No values at all.</summary>
    public static readonly PanelNotificationArguments None = new([]);

    /// <summary>The values, in order, each already cleaned and bounded.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>Creates the value from already-cleaned values.</summary>
    /// <param name="values">The cleaned values.</param>
    private PanelNotificationArguments(IReadOnlyList<string> values)
    {
        Values = values;
    }

    /// <summary>Cleans and bounds the values a producer supplied.</summary>
    /// <param name="values">The producer's values; <c>null</c> reads as none.</param>
    /// <returns>The kept values: control characters removed, each cut to its bound, at most four.</returns>
    public static PanelNotificationArguments From(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return None;
        }

        var kept = values
            .Take(MaxCount)
            .Select(value =>
            {
                return NotificationText.Clean(value, MaxValueLength);
            })
            .ToList();

        while (kept.Count > 0 && kept[^1].Length == 0)
        {
            kept.RemoveAt(kept.Count - 1);
        }

        return new PanelNotificationArguments(kept);
    }

    /// <summary>Reads back what <see cref="Serialize"/> wrote.</summary>
    /// <param name="stored">The stored column value.</param>
    /// <returns>The values, exactly as they were kept.</returns>
    public static PanelNotificationArguments Parse(string stored)
    {
        return stored.Length == 0
            ? None
            : new PanelNotificationArguments(stored.Split(Separator));
    }

    /// <summary>The value to store: the values joined with <see cref="Separator"/>.</summary>
    /// <returns>The stored column text; empty when there are no values.</returns>
    public string Serialize()
    {
        return string.Join(Separator, Values);
    }
}
