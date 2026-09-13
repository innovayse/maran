namespace Maran.SharedKernel.Security;

/// <summary>
/// A string the panel must be able to send but must never print: a freshly minted database or SFTP
/// password on its way to the agent. Reading the value is an explicit call; every implicit reading —
/// <see cref="object.ToString"/>, string interpolation, a structured-logging argument, a debugger
/// display — yields a fixed mask instead.
/// </summary>
/// <remarks>
/// This type exists because of a leak this repository has already shipped once, in a different
/// costume. A private key reached a log because the text carrying it was written out by something
/// that was never asked whether the text was secret. The C# shape of the same mistake is a
/// <c>record</c>: a compiler-generated <c>ToString()</c> prints every property, so a request record
/// with a <c>Password</c> property leaks the password the first time anyone logs the request, or
/// interpolates it into a message, or lets an exception carry it. Nothing at that call site looks
/// wrong, which is exactly why the defence belongs in the type rather than in the discipline of
/// every future caller.
///
/// A sealed class rather than a <c>record</c> or a <c>record struct</c>, deliberately: both of those
/// generate the printing member this type exists to withhold, and a future edit that turns this into
/// a record would silently restore the leak.
///
/// The value is still a plain <see cref="string"/> in managed memory and this type makes no claim
/// about erasing it — the .NET string is immutable and copied by the gRPC serializer regardless.
/// What it removes is the accidental *printing* path, which is the one that has actually bitten.
/// </remarks>
public sealed class SensitiveString
{
    /// <summary>What every implicit rendering of this type produces instead of the value.</summary>
    private const string Mask = "[redacted]";

    /// <summary>The secret itself, reachable only through <see cref="Reveal"/>.</summary>
    private readonly string _value;

    /// <summary>Wraps a secret so that it cannot be printed by accident.</summary>
    /// <param name="value">The secret text. An empty string is allowed and stays the caller's problem to validate.</param>
    public SensitiveString(string value)
    {
        _value = value;
    }

    /// <summary>
    /// Returns the mask, never the value — so interpolation, <c>Console</c>, structured logging and
    /// the debugger all show <c>[redacted]</c>.
    /// </summary>
    /// <returns>The fixed mask.</returns>
    public override string ToString()
    {
        return Mask;
    }

    /// <summary>
    /// Hands out the secret for the one purpose it exists for: putting it on the wire. Named as a
    /// deliberate act so that a reviewer can find every place the value escapes with one search.
    /// </summary>
    /// <returns>The wrapped secret.</returns>
    public string Reveal()
    {
        return _value;
    }
}
