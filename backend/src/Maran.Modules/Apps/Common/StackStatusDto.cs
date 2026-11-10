namespace Maran.Modules.Apps.Common;

/// <summary>
/// What the server is set up to serve sites with: whether the first-run setup is finished and which
/// web server and runtimes sites may use. Readable by any signed-in role, because the SPA branches on it
/// before it shows a screen; it carries no tenant data.
/// </summary>
/// <param name="IsReady">Whether a site web server is installed. While it is not, no site can be created or changed.</param>
/// <param name="WebServer">The catalogue id of the installed site web server, or <see langword="null"/> while none is.</param>
/// <param name="PhpVersions">The installed PHP versions (<c>8.3</c>), newest first.</param>
/// <param name="NodeVersions">The installed Node.js major versions (<c>22</c>), newest first.</param>
public sealed record StackStatusDto(
    bool IsReady,
    string? WebServer,
    IReadOnlyList<string> PhpVersions,
    IReadOnlyList<string> NodeVersions);
