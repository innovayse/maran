namespace Maran.Sdk.Contracts;

/// <summary>What a Node-backed site runs, carried across modules so a vhost re-render keeps its unit.</summary>
/// <param name="Version">The installed Node major, or the empty string when the backend is not Node.</param>
/// <param name="Port">The loopback port the application listens on, or zero when the backend is not Node.</param>
/// <param name="StartCommand">The command the per-site unit runs, or the empty string when the backend is not Node.</param>
public sealed record SiteNodeSnapshot(string Version, int Port, string StartCommand)
{
    /// <summary>The value for a site whose backend is not Node.</summary>
    public static readonly SiteNodeSnapshot None = new(string.Empty, 0, string.Empty);
}
