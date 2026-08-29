namespace Maran.SharedKernel.Results;

/// <summary>
/// A typed domain failure. <paramref name="Code"/> is machine-stable
/// ("module.reason", drives HTTP mapping and i18n); <paramref name="Message"/>
/// is operator-facing English and never shown raw to customers.
/// </summary>
public sealed record Error(string Code, string Message)
{
    /// <summary>Creates an error, guarding against empty codes.</summary>
    public static Error Of(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new Error(code, message);
    }
}
