using Maran.SharedKernel.Apps;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>An <see cref="IStackStatus"/> double reporting one installed site web server.</summary>
public sealed class FakeStackStatus : IStackStatus
{
    /// <summary>Creates the double.</summary>
    /// <param name="webServer">The installed site web server; nginx when omitted.</param>
    public FakeStackStatus(StackWebServer webServer = StackWebServer.Nginx)
    {
        WebServer = webServer;
    }

    /// <inheritdoc />
    public bool IsReady
    {
        get { return WebServer != StackWebServer.None; }
    }

    /// <inheritdoc />
    public StackWebServer WebServer { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> PhpVersions
    {
        get { return ["8.3"]; }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> NodeMajors
    {
        get { return []; }
    }
}
