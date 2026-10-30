namespace Maran.Host.Dashboard;

/// <summary>Whether a dashboard count was taken.</summary>
/// <remarks>
/// A state of its own rather than a zero or a null for "could not be read": zero is a real count,
/// and a server whose module failed to answer is not a server that holds none.
/// </remarks>
public enum DashboardCountState
{
    /// <summary>The module answered; <see cref="DashboardCountDto.Count"/> is the number.</summary>
    Counted = 0,

    /// <summary>The module refused or failed; there is no number to show.</summary>
    Unavailable = 1,
}
