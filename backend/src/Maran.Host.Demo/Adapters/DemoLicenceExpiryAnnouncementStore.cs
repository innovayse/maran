using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Models;

namespace Maran.Host.Demo.Adapters;

/// <summary>
/// Where the demo keeps the last licence-expiry announcement: in memory, so the demo process writes no state file
/// when its licence falls inside an expiry threshold. It replaces the module's file-backed store.
/// </summary>
public sealed class DemoLicenceExpiryAnnouncementStore : ILicenceExpiryAnnouncementStore
{
    /// <summary>Guards <see cref="_announcement"/>.</summary>
    private readonly object _gate = new();

    /// <summary>The last announcement, or <c>null</c> when none was made.</summary>
    private LicenceExpiryAnnouncement? _announcement;

    /// <summary>Reads the last announcement.</summary>
    /// <param name="cancellationToken">Cancellation token; nothing here waits.</param>
    /// <returns>The announcement, or <c>null</c> when none was made.</returns>
    public Task<LicenceExpiryAnnouncement?> ReadAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_announcement);
        }
    }

    /// <summary>Keeps an announcement in memory, replacing any earlier one.</summary>
    /// <param name="announcement">The announcement.</param>
    /// <param name="cancellationToken">Cancellation token; nothing here waits.</param>
    /// <returns>A completed task.</returns>
    public Task RememberAsync(LicenceExpiryAnnouncement announcement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(announcement);

        lock (_gate)
        {
            _announcement = announcement;
        }

        return Task.CompletedTask;
    }

    /// <summary>Forgets the last announcement.</summary>
    /// <param name="cancellationToken">Cancellation token; nothing here waits.</param>
    /// <returns>A completed task.</returns>
    public Task ForgetAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _announcement = null;
        }

        return Task.CompletedTask;
    }
}
