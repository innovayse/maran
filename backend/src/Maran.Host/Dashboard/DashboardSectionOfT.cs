using System.Text.Json.Serialization;
using Maran.Host.Serialization;

namespace Maran.Host.Dashboard;

/// <summary>
/// One section of the landing page: either the data a module answered with, or the fact that it
/// could not be read.
/// </summary>
/// <remarks>
/// <para>
/// <b>A closed choice of two, not a value beside a flag.</b> The only ways to make one are
/// <see cref="DashboardSection.Loaded{T}"/> and <see cref="DashboardSection.Unavailable{T}"/>, and the only ways to reach the data are
/// <see cref="Match{TResult}"/> and <see cref="Switch"/>, which make the caller answer both cases.
/// So no reader can take the data of a section that has none, and no empty list, zero or null is
/// ever left standing for "could not be read" — the defect issue #248 was about: a failed backups
/// read counted as zero, and the screen announced that nothing needed attention.
/// </para>
/// <para>
/// On the wire it is <c>{"state":"loaded","data":…}</c> or <c>{"state":"unavailable"}</c>, written
/// by <see cref="DashboardSectionJsonConverterFactory"/>; the SPA's <c>DashboardSection&lt;T&gt;</c>
/// is the same union.
/// </para>
/// </remarks>
/// <typeparam name="T">What the section carries when it was read.</typeparam>
[JsonConverter(typeof(DashboardSectionJsonConverterFactory))]
public sealed class DashboardSection<T>
{
    /// <summary>
    /// The data; reached only through <see cref="Match{TResult}"/> and <see cref="Switch"/>, and only
    /// when loaded.
    /// </summary>
    private readonly T _data;

    /// <summary>Whether the module answered.</summary>
    public DashboardSectionState State { get; }

    /// <summary>
    /// Creates a section in the given state; reached only through the two factories on
    /// <see cref="DashboardSection"/>, which keep the state and the data in agreement.
    /// </summary>
    /// <param name="state">Whether the module answered.</param>
    /// <param name="data">The data when loaded; never read otherwise.</param>
    internal DashboardSection(DashboardSectionState state, T data)
    {
        State = state;
        _data = data;
    }

    /// <summary>Answers both cases of the section.</summary>
    /// <typeparam name="TResult">What both cases produce.</typeparam>
    /// <param name="loaded">Called with the data when the section was read.</param>
    /// <param name="unavailable">Called when it could not be.</param>
    /// <returns>Whichever case applied.</returns>
    public TResult Match<TResult>(Func<T, TResult> loaded, Func<TResult> unavailable)
    {
        return State == DashboardSectionState.Loaded ? loaded(_data) : unavailable();
    }

    /// <summary>Acts on both cases of the section, for a caller that produces nothing.</summary>
    /// <param name="loaded">Called with the data when the section was read.</param>
    /// <param name="unavailable">Called when it could not be.</param>
    public void Switch(Action<T> loaded, Action unavailable)
    {
        if (State == DashboardSectionState.Loaded)
        {
            loaded(_data);
            return;
        }

        unavailable();
    }

    /// <summary>
    /// Derives a section from this one: the data transformed when loaded, still unavailable when not.
    /// </summary>
    /// <remarks>
    /// How an attention figure is taken from a listing — the failed tasks counted from the task list —
    /// without the listing's failure turning into a count of zero on the way.
    /// </remarks>
    /// <typeparam name="TResult">What the derived section carries.</typeparam>
    /// <param name="transform">Turns the data into the derived data.</param>
    /// <returns>The derived section.</returns>
    public DashboardSection<TResult> Map<TResult>(Func<T, TResult> transform)
    {
        return Match(
            data =>
            {
                return DashboardSection.Loaded(transform(data));
            },
            () =>
            {
                return DashboardSection.Unavailable<TResult>();
            });
    }
}
