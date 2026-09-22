namespace Maran.Modules.Licensing.Domain.ValueObjects;

/// <summary>
/// The licence artefact's own opaque identifier, as Innovayse's cabinet issued it (spec §228's
/// <c>id</c> field). Not a secret and not derived from anything on this server — safe to journal
/// and safe to show an operator.
/// </summary>
/// <param name="Value">The raw identifier text, exactly as the licence envelope carried it.</param>
public sealed record LicenceId(string Value)
{
    /// <summary>Validates and wraps a licence id.</summary>
    /// <param name="value">The raw identifier text.</param>
    /// <returns>The wrapped id.</returns>
    /// <exception cref="ArgumentException">The value is null, empty, or whitespace.</exception>
    public static LicenceId Of(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new LicenceId(value);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }
}
