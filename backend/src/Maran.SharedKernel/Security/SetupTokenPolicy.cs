namespace Maran.SharedKernel.Security;

/// <summary>
/// The panel's one answer to "may this panel run with this first-administrator setup token": the
/// values this repository publishes are refused, and so is a token too short or too uniform to
/// resist a guess. It sits beside <see cref="SecretKeyPolicy"/> because the token is the same kind of
/// thing — permission to take the panel over, set by an operator, with a sample file anyone can read.
/// </summary>
/// <remarks>
/// <para>
/// An EMPTY token answers <c>false</c> to every question. A panel whose setup is finished has no
/// token, and refusing that would stop every server that has been running for a week; an empty token
/// admits nobody (the setup handler matches nothing against it).
/// </para>
/// <para>
/// The floor is sized against the installer's own token (<c>openssl rand -hex 24</c>, 48 characters
/// from sixteen symbols) and a hand-made one from <c>openssl rand -hex 16</c>, both of which pass: 32
/// characters and ten distinct ones. A token the installer generated can never trip it (the chance
/// that 48 random hex digits use fewer than ten of the sixteen symbols is below 1e-9). What the floor
/// cannot see: a random-looking token copied from another server or a template. It detects the values
/// this repository publishes and the structure of a typed word, not secrecy.
/// </para>
/// </remarks>
public static class SetupTokenPolicy
{
    /// <summary>The fewest characters an accepted token holds.</summary>
    public const int MinimumLength = 32;

    /// <summary>The fewest different characters an accepted token holds.</summary>
    public const int MinimumDistinctCharacters = 10;

    /// <summary>
    /// Setup token values this repository has published (in <c>.env.example</c>, the development
    /// self-check and the browser golden path). Anyone who has read the repository holds them.
    /// </summary>
    private static readonly string[] PublishedSamples =
    [
        "dev-setup-token",
    ];

    /// <summary>Whether <paramref name="token"/> is one of the setup tokens this repository publishes.</summary>
    /// <param name="token">The configured token.</param>
    /// <returns>True when it equals a published sample, ignoring case.</returns>
    public static bool IsPublishedSample(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        foreach (var published in PublishedSamples)
        {
            if (string.Equals(token.Trim(), published, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="token"/> is non-empty yet shorter than <see cref="MinimumLength"/> or
    /// made of fewer than <see cref="MinimumDistinctCharacters"/> different characters.
    /// </summary>
    /// <param name="token">The configured token.</param>
    /// <returns>True when a non-empty token is too weak to resist a guess.</returns>
    public static bool IsWeak(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        return token.Length < MinimumLength || token.Distinct().Count() < MinimumDistinctCharacters;
    }
}
