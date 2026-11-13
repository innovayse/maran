using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Domain.Policies;
using Maran.Modules.Licensing.Models;
using Maran.Modules.Licensing.Options;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Answers whether a licence status is within an expiry warning threshold now: the one place the licence screen,
/// the install answer, <c>maran status</c> and the notification get the answer from, so the four can never
/// disagree about whether a warning exists.
/// </summary>
/// <remarks>
/// It holds no judgement of its own: the thresholds come from <see cref="LicenceExpiryWarningOptions"/> and the
/// rule from <see cref="LicenceExpiryWarningPolicy"/>. It reads the clock on every call and remembers nothing, so
/// installing a new licence changes the very next answer.
/// </remarks>
public sealed class LicenceExpiryWarningSource
{
    /// <summary>The panel's injected time source.</summary>
    private readonly IClock _clock;

    /// <summary>The configured thresholds.</summary>
    private readonly IOptions<LicenceExpiryWarningOptions> _options;

    /// <summary>Creates the source.</summary>
    /// <param name="clock">The panel's injected time source.</param>
    /// <param name="options">The configured thresholds.</param>
    public LicenceExpiryWarningSource(IClock clock, IOptions<LicenceExpiryWarningOptions> options)
    {
        _clock = clock;
        _options = options;
    }

    /// <summary>Judges a licence status now.</summary>
    /// <param name="status">The verified status of the installed licence.</param>
    /// <returns>The warning, or null for no licence, a refused one, an expired one or one not yet within a threshold.</returns>
    public LicenceExpiryWarning? For(LicenceStatus status)
    {
        if (status is not LicenceStatus.Valid valid)
        {
            return null;
        }

        return LicenceExpiryWarningPolicy.Evaluate(
            valid.Licence,
            _clock,
            LicenceExpiryWarningPolicy.Resolve(_options.Value.ExpiryWarningDays));
    }
}
