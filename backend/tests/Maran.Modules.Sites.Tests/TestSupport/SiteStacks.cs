using Maran.Modules.Sites.Services;
using Maran.SharedKernel.Apps;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>Builds the <see cref="SiteStackGate"/> a handler test runs against.</summary>
public static class SiteStacks
{
    /// <summary>A host with nginx, PHP 8.3 and 8.4 and Node 22 installed: the ordinary case.</summary>
    /// <returns>The gate over that stack.</returns>
    public static SiteStackGate Ready()
    {
        return new SiteStackGate(new FakeStackStatus());
    }

    /// <summary>A fresh host: nothing is installed, so no site operation may run.</summary>
    /// <returns>The gate over an empty stack.</returns>
    public static SiteStackGate NotReady()
    {
        return new SiteStackGate(new FakeStackStatus(StackWebServer.None, [], []));
    }

    /// <summary>A host with a web server and no runtime at all.</summary>
    /// <param name="webServer">The installed web server.</param>
    /// <returns>The gate over that stack.</returns>
    public static SiteStackGate WithoutRuntimes(StackWebServer webServer = StackWebServer.Nginx)
    {
        return new SiteStackGate(new FakeStackStatus(webServer, [], []));
    }

    /// <summary>A host with the given web server and runtimes installed.</summary>
    /// <param name="webServer">The installed web server.</param>
    /// <param name="phpVersions">The installed PHP versions.</param>
    /// <param name="nodeMajors">The installed Node majors.</param>
    /// <returns>The gate over that stack.</returns>
    public static SiteStackGate With(
        StackWebServer webServer,
        IReadOnlyList<string> phpVersions,
        IReadOnlyList<string> nodeMajors)
    {
        return new SiteStackGate(new FakeStackStatus(webServer, phpVersions, nodeMajors));
    }
}
