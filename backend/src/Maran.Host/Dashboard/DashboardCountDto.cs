namespace Maran.Host.Dashboard;

/// <summary>One count on the landing page, with whether it could be taken.</summary>
/// <param name="State">Whether the module answered.</param>
/// <param name="Count">
/// The number when <paramref name="State"/> is <see cref="DashboardCountState.Counted"/>; zero and
/// meaningless when it is <see cref="DashboardCountState.Unavailable"/>, which the screen draws as
/// "unavailable" and never as a number.
/// </param>
public sealed record DashboardCountDto(DashboardCountState State, int Count);
