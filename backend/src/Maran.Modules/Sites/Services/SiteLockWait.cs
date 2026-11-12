namespace Maran.Modules.Sites.Services;

/// <summary>How long an operation waits for a site's operation lock before it gives up on it.</summary>
/// <remarks>
/// Three values because there are three kinds of caller. A customer is looking at a spinner and is better
/// served by a quick, honest "try again" (<c>SiteBusy</c>) than by a minute of silence. A queued job has
/// nobody waiting on it, and a site that is briefly busy (a PHP version change re-renders the vhost, which
/// takes seconds) is cheaper to wait for than to fail and retry later. An account lifecycle cascade is in
/// between: it is queued behind nothing, but it runs INSIDE the administrator's request, so it may wait
/// only a bounded time in all, however many sites the account has (<see cref="SiteLockBudget"/>).
/// </remarks>
public static class SiteLockWait
{
    /// <summary>The wait for a request a person is watching.</summary>
    public static readonly TimeSpan Interactive = TimeSpan.FromSeconds(15);

    /// <summary>The wait for a queued job that handles one site.</summary>
    public static readonly TimeSpan Job = TimeSpan.FromSeconds(90);

    /// <summary>The longest an account cascade waits for ONE site.</summary>
    public static readonly TimeSpan Cascade = TimeSpan.FromSeconds(15);

    /// <summary>The longest an account cascade waits for ALL its sites together.</summary>
    public static readonly TimeSpan CascadeTotal = TimeSpan.FromSeconds(45);
}
