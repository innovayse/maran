namespace Maran.Host.Dashboard;

/// <summary>Whether a dashboard section could be read.</summary>
/// <remarks>
/// A state of its own rather than an empty list, a zero or a null for "could not be read": an empty
/// list is a real answer (no services watched), so is a zero (no backup failed), and a server whose
/// module failed to answer is not a server that holds none. Written on the wire as <c>state</c> by
/// <see cref="Maran.Host.Serialization.DashboardSectionJsonConverter{T}"/>.
/// </remarks>
public enum DashboardSectionState
{
    /// <summary>The module answered; the section carries its data.</summary>
    Loaded = 0,

    /// <summary>The module refused or failed; the section carries nothing.</summary>
    Unavailable = 1,
}
