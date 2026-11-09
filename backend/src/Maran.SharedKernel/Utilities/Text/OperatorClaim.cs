namespace Maran.SharedKernel.Utilities.Text;

/// <summary>
/// The rule for an operator's name as the server's console reports it (the invoking
/// <c>SUDO_USER</c>) before it is written to a log-like column: no control characters, a bounded
/// length, and a stated placeholder when nothing usable remains.
/// </summary>
/// <remarks>
/// The name is a CLAIM, never an authentication — the environment variable is whatever the caller's
/// shell handed over — so it is tidied here once rather than trusted by each verb that records it.
/// </remarks>
public static class OperatorClaim
{
    /// <summary>The longest claim kept.</summary>
    public const int MaxLength = 64;

    /// <summary>What a claim with nothing usable in it becomes.</summary>
    public const string Unknown = "unknown";

    /// <summary>Drops control characters from the claimed name and bounds its length.</summary>
    /// <param name="claim">The name as the console reported it.</param>
    /// <returns>The claim safe to write to a log-like column; <see cref="Unknown"/> when nothing usable remains.</returns>
    public static string Tidy(string claim)
    {
        ArgumentNullException.ThrowIfNull(claim);

        var cleaned = new string(claim.Where(character =>
        {
            return !char.IsControl(character);
        }).ToArray()).Trim();
        if (cleaned.Length > MaxLength)
        {
            cleaned = cleaned[..MaxLength];
        }

        return cleaned.Length == 0 ? Unknown : cleaned;
    }
}
