using Maran.Agent.Client.Services.SitesService;
using Maran.SharedKernel.Apps;
using Maran.SharedKernel.Interfaces;

namespace Maran.Agent.Client.Tests.Services.SitesService;

/// <summary>
/// The one translation of the app store's installed web server into the agent client's, which the Sites and
/// Ssl modules share. A wrong arm renders every vhost for the wrong server.
/// </summary>
public sealed class SiteWebServerMapperTests
{
    /// <summary>Each installed web server maps to its own agent-client counterpart.</summary>
    [Theory]
    [InlineData(StackWebServer.Nginx, SiteWebServer.Nginx)]
    [InlineData(StackWebServer.Apache, SiteWebServer.Apache)]
    public void Each_installed_web_server_maps_to_its_own_counterpart(StackWebServer installed, SiteWebServer expected)
    {
        Assert.Equal(expected, SiteWebServerMapper.From(installed));
    }

    /// <summary>No installed web server maps to null and is never defaulted to nginx.</summary>
    [Fact]
    public void No_installed_web_server_maps_to_null_and_is_never_defaulted_to_nginx()
    {
        Assert.Null(SiteWebServerMapper.From(StackWebServer.None));
    }

    /// <summary>A value outside the closed set is a defect and throws.</summary>
    [Fact]
    public void A_value_outside_the_closed_set_is_a_defect_and_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return SiteWebServerMapper.From((StackWebServer)99);
        });
    }

    /// <summary>Requiring the web server answers the installed one and refuses when none is installed.</summary>
    [Fact]
    public void Requiring_the_web_server_answers_the_installed_one_and_refuses_when_none_is_installed()
    {
        Assert.Equal(SiteWebServer.Apache, SiteWebServerMapper.Require(new StackOf(StackWebServer.Apache)));
        Assert.Throws<InvalidOperationException>(() =>
        {
            return SiteWebServerMapper.Require(new StackOf(StackWebServer.None));
        });
    }

    /// <summary>An <see cref="IStackStatus"/> reporting one web server.</summary>
    private sealed class StackOf : IStackStatus
    {
        /// <summary>Creates the double.</summary>
        /// <param name="webServer">The web server reported.</param>
        public StackOf(StackWebServer webServer)
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
            get { return []; }
        }

        /// <inheritdoc />
        public IReadOnlyList<string> NodeMajors
        {
            get { return []; }
        }
    }
}
