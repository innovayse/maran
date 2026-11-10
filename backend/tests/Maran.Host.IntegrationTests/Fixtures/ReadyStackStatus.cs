using Maran.SharedKernel.Apps;
using Maran.SharedKernel.Interfaces;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An <see cref="IStackStatus"/> double for tests that build a Sites handler by hand: nginx and PHP 8.3
/// installed, so the stack gate lets the handler through and the test keeps its own subject.
/// </summary>
public sealed class ReadyStackStatus : IStackStatus
{
    /// <inheritdoc />
    public bool IsReady
    {
        get { return true; }
    }

    /// <inheritdoc />
    public StackWebServer WebServer
    {
        get { return StackWebServer.Nginx; }
    }

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
