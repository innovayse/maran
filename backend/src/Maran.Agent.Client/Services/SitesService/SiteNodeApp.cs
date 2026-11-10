namespace Maran.Agent.Client.Services.SitesService;

/// <summary>What a Node-backed site runs: the installed Node major, the port it listens on and how it starts.</summary>
/// <remarks>
/// Required on every call that renders a vhost, like the rest of <see cref="SiteDescriptor"/>, so a
/// caller whose site has no Node backend says <see cref="None"/> out loud rather than leaving the
/// fields to default.
/// </remarks>
/// <param name="Version">The installed Node major, or the empty string when the backend is not Node.</param>
/// <param name="Port">The loopback port the application listens on, or zero when the backend is not Node.</param>
/// <param name="StartCommand">The command the per-site unit runs, or the empty string when the backend is not Node.</param>
public sealed record SiteNodeApp(string Version, uint Port, string StartCommand)
{
    /// <summary>The value for a site whose backend is not Node.</summary>
    public static readonly SiteNodeApp None = new(string.Empty, 0, string.Empty);
}
