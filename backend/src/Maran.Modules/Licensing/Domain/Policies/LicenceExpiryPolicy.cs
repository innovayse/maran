using Maran.Modules.Licensing.Domain.Entities;

namespace Maran.Modules.Licensing.Domain.Policies;

/// <summary>
/// The one place a licence's expiry is compared against the clock. Isolated into its own stateless
/// policy so the "expiry ignored" mutant named in this slice's proof table
/// has exactly one place to land, and one
/// test to kill it.
/// </summary>
public static class LicenceExpiryPolicy
{
    /// <summary>Whether a licence's stated expiry is on or before the clock's current instant.</summary>
    /// <param name="licence">The licence to check.</param>
    /// <param name="clock">The panel's injected time source (<c>DateTimeOffset.UtcNow</c> is banned, rules/csharp.md).</param>
    /// <returns><c>true</c> when the licence has expired.</returns>
    public static bool IsExpired(Licence licence, IClock clock)
    {
        return clock.UtcNow >= licence.Expiry;
    }
}
