namespace Maran.Host.Dashboard;

/// <summary>
/// The two ways to make a <see cref="DashboardSection{T}"/>: a module answered, or it did not.
/// </summary>
/// <remarks>
/// On a type of their own rather than as static members of the generic one, because a static member
/// on a generic type is what CA1000 refuses, and the rule is kept rather than switched off. The
/// generic constructor is <c>internal</c>, and these two are its only callers; each fixes the state
/// to agree with the data it is given.
/// </remarks>
public static class DashboardSection
{
    /// <summary>The section a module answered.</summary>
    /// <typeparam name="T">What the section carries.</typeparam>
    /// <param name="data">What the module answered with.</param>
    /// <returns>A loaded section carrying <paramref name="data"/>.</returns>
    public static DashboardSection<T> Loaded<T>(T data)
    {
        return new DashboardSection<T>(DashboardSectionState.Loaded, data);
    }

    /// <summary>The section a module did not answer: it carries nothing.</summary>
    /// <typeparam name="T">What the section would have carried.</typeparam>
    /// <returns>An unavailable section.</returns>
    public static DashboardSection<T> Unavailable<T>()
    {
        return new DashboardSection<T>(DashboardSectionState.Unavailable, default!);
    }
}
