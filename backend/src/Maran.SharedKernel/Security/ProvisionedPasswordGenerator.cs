using System.Security.Cryptography;

namespace Maran.SharedKernel.Security;

/// <summary>
/// Mints the password for a credential the panel provisions on the host — a database user, an SFTP
/// login — and the replacement a reset installs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Generation lives in the panel, not the agent.</b> The panel is what shows the value to the
/// customer, and a value generated where it is displayed has one fewer hop to leak from. The agent
/// therefore never invents a credential: it is handed one, passes it to the host and forgets it.
/// </para>
/// <para>
/// <b>It lives in the SharedKernel because two modules mint the same kind of secret against the same
/// downstream alphabet</b> (rules/csharp.md — "Anything shared by two modules moves down to
/// SharedKernel or Sdk — never sideways between modules"). A second generator beside the first is
/// the failure this placement prevents: two alphabets and two length floors that agree on the day
/// they are written, drift on the day one of them is widened, and are then wrong in the module
/// nobody edited.
/// </para>
/// <para>
/// <b>The alphabet is exactly the one the agent's <c>Password</c> type accepts, and that is not a
/// coincidence to be tidied away.</b> That type refuses the quote, the double quote, the backtick,
/// the backslash, the colon, the space and every control character, because the value reaches the
/// host through places that take no placeholders: <c>IDENTIFIED BY '&lt;value&gt;'</c> in a root
/// MySQL session, and a <c>user:password</c> line on <c>chpasswd</c>'s standard input. The colon and
/// the newline it refuses are precisely the two characters that would let a value break out of that
/// line and set the password of a login that is not the caller's. What makes both safe is the
/// alphabet and not any escaping. A generator that emitted one character outside it would produce a
/// provisioning the agent refuses AFTER the customer has been promised the resource, which is the
/// worst place to discover a disagreement about a character set.
/// </para>
/// <para>
/// <see cref="RandomNumberGenerator"/> and never <see cref="Random"/>: this value is a credential on
/// a customer's data, and <see cref="Random"/> is a seeded pseudo-random sequence that another
/// customer holding one output of can continue.
/// </para>
/// </remarks>
public static class ProvisionedPasswordGenerator
{
    /// <summary>
    /// Every character a generated password may contain: ASCII letters, ASCII digits, and the five
    /// symbols the agent's <c>Password</c> type allows.
    /// </summary>
    /// <remarks>
    /// Kept as an explicit literal rather than assembled from ranges, so that the exact set is
    /// visible to a reader and to a test comparing it against the agent's own list. Widening it is
    /// not a style choice — see this type's remarks.
    /// </remarks>
    public const string Alphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.=+";

    /// <summary>How many characters every generated password has.</summary>
    /// <remarks>
    /// Well above <see cref="SecretRedactionPolicy.ShortestRecognisableSecret"/>, which is the floor
    /// below which the agent-error boundary stops stripping the value it was handed. Below that
    /// floor nothing visibly breaks — the call still succeeds and the log line is still written, the
    /// only difference being the password in it — so the relationship is pinned by a test rather
    /// than by this sentence, since a constant cannot be constrained by another at compile time.
    ///
    /// Twenty-four characters of this alphabet is roughly 145 bits, comfortably beyond anything that
    /// is brute-forced, and far under the agent's 128-byte ceiling.
    /// </remarks>
    public const int PasswordLength = 24;

    /// <summary>Mints one password.</summary>
    /// <returns>
    /// The new value in a non-printing carrier, so that no log line, interpolation or exception
    /// message can render it by accident on its way to the agent and to the one response that shows
    /// it (<see cref="SensitiveString"/>).
    /// </returns>
    public static SensitiveString Generate()
    {
        // GetItems is the unbiased draw: choosing with a modulo over random bytes skews towards the
        // first characters of the alphabet, because 256 is not a multiple of its length.
        return new SensitiveString(new string(RandomNumberGenerator.GetItems<char>(Alphabet, PasswordLength)));
    }
}
