using Maran.Modules.Sites.Services;
using Wolverine;
using Wolverine.ErrorHandling;

namespace Maran.Host.Extensions;

/// <summary>
/// The failure policies of the panel's message bus: which exceptions a handler may throw to say "not now",
/// and how the bus then tries again. Applied once, in <see cref="MessagingExtensions"/>, so how a failed
/// message is retried is read in one place and not inferred from the library's defaults.
/// </summary>
public static class MessagingFailurePolicies
{
    /// <summary>
    /// How long the bus waits before it tries a message again after a site stayed busy: half a minute,
    /// two minutes, ten minutes.
    /// </summary>
    /// <remarks>
    /// A handler that throws <see cref="SiteBusyException"/> has already waited the job wait (90 s) for the
    /// site, so an immediate retry would only queue behind the same operation. Scheduling the retry frees the
    /// queue meanwhile, and the three delays together cover an operation that is slow and not stuck. When they
    /// are spent the message is dealt with as any failed message is, and the start-up pass converges the
    /// vhosts at the next panel start.
    /// </remarks>
    public static readonly IReadOnlyList<TimeSpan> SiteBusyRetryDelays = Array.AsReadOnly(new[]
    {
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
    });

    /// <summary>Adds the panel's failure policies to the bus options.</summary>
    /// <param name="options">The Wolverine options being configured.</param>
    public static void Apply(WolverineOptions options)
    {
        options.OnException<SiteBusyException>().ScheduleRetry(SiteBusyRetryDelays.ToArray());
    }
}
