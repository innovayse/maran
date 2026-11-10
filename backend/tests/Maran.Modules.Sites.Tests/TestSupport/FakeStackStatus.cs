using Maran.SharedKernel.Apps;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>
/// An <see cref="IStackStatus"/> double reporting a fixed set of installed apps, so a test states in
/// one line what the host has and the Sites module's reaction to it is the only thing under test.
/// </summary>
public sealed class FakeStackStatus : IStackStatus
{
    /// <summary>Creates a stack with the given installed apps.</summary>
    /// <param name="webServer">The installed site web server, or <see cref="StackWebServer.None"/>.</param>
    /// <param name="phpVersions">The installed PHP versions.</param>
    /// <param name="nodeMajors">The installed Node majors.</param>
    public FakeStackStatus(
        StackWebServer webServer = StackWebServer.Nginx,
        IReadOnlyList<string>? phpVersions = null,
        IReadOnlyList<string>? nodeMajors = null)
    {
        WebServer = webServer;
        PhpVersions = phpVersions ?? ["8.3", "8.4"];
        NodeMajors = nodeMajors ?? ["22"];
    }

    /// <inheritdoc />
    public bool IsReady
    {
        get { return WebServer != StackWebServer.None; }
    }

    /// <inheritdoc />
    public StackWebServer WebServer { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> PhpVersions { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> NodeMajors { get; }
}
