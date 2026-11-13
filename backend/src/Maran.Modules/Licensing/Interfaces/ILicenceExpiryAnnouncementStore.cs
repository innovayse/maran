using Maran.Modules.Licensing.Models;

namespace Maran.Modules.Licensing.Interfaces;

/// <summary>
/// Keeps what the operator was last told about the licence's approaching expiry across a panel restart, so an
/// update, a restart or a crash loop never announces the same threshold again.
/// </summary>
/// <remarks>
/// Neither method throws for a storage fault the caller could do anything about: a read that fails answers "nothing
/// announced" (the safe direction is a repeated notice, never a missed one) and a write that fails is reported to the
/// caller, which logs it and keeps the answer in memory for the life of the process.
/// </remarks>
public interface ILicenceExpiryAnnouncementStore
{
    /// <summary>Reads the last announcement.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The announcement, or null when none is stored or the stored one cannot be read.</returns>
    Task<LicenceExpiryAnnouncement?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Stores an announcement, replacing any earlier one.</summary>
    /// <param name="announcement">The announcement.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolves once the announcement is stored whole.</returns>
    /// <exception cref="IOException">The announcement could not be stored; the earlier one is untouched.</exception>
    Task RememberAsync(LicenceExpiryAnnouncement announcement, CancellationToken cancellationToken);

    /// <summary>Forgets the last announcement, so the next threshold the licence crosses is announced.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolves once nothing is stored.</returns>
    /// <exception cref="IOException">The announcement could not be removed.</exception>
    Task ForgetAsync(CancellationToken cancellationToken);
}
