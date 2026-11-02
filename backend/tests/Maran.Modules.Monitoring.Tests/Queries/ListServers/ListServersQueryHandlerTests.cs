using System.Globalization;
using Maran.Modules.Monitoring.Domain.Enums;
using Maran.Modules.Monitoring.Queries.ListServers;
using Maran.Modules.Monitoring.Tests.TestSupport;

namespace Maran.Modules.Monitoring.Tests.Queries.ListServers;

/// <summary>
/// The server selector's read (issue #319): exactly one entry, the local server, named by the panel's
/// configured host name, with its kind spelled by the real resource files in every language.
/// </summary>
public sealed class ListServersQueryHandlerTests
{
    /// <summary>The panel manages exactly one server today, and it is the local one.</summary>
    [Fact]
    public void The_panel_lists_exactly_one_server_and_it_is_the_local_one()
    {
        var handler = new ListServersQueryHandler(
            new StubPanelHostName("panel.example.com"),
            MonitoringTestContext.DisplayNamesText());

        var result = handler.Handle(new ListServersQuery());

        Assert.True(result.IsSuccess);
        var server = Assert.Single(result.Value);
        Assert.Equal(ServerKind.Local, server.Kind);
    }

    /// <summary>The server carries the host name the panel is configured to be reached by.</summary>
    [Fact]
    public void The_server_carries_the_configured_host_name()
    {
        var handler = new ListServersQueryHandler(
            new StubPanelHostName("panel.example.com"),
            MonitoringTestContext.DisplayNamesText());

        var server = Assert.Single(handler.Handle(new ListServersQuery()).Value);

        Assert.Equal("panel.example.com", server.HostName);
    }

    /// <summary>With no host name configured the entry carries none, rather than a made-up one.</summary>
    [Fact]
    public void With_no_host_name_configured_the_server_carries_none()
    {
        var handler = new ListServersQueryHandler(
            new StubPanelHostName(null),
            MonitoringTestContext.DisplayNamesText());

        var server = Assert.Single(handler.Handle(new ListServersQuery()).Value);

        Assert.Null(server.HostName);
        Assert.False(string.IsNullOrWhiteSpace(server.KindDisplayName));
    }

    /// <summary>The kind is named in every shipped language, never by its resource key.</summary>
    /// <param name="culture">The request culture.</param>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public void The_kind_is_named_in_every_language_never_by_its_key(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var handler = new ListServersQueryHandler(
                new StubPanelHostName("panel.example.com"),
                MonitoringTestContext.DisplayNamesText());

            var server = Assert.Single(handler.Handle(new ListServersQuery()).Value);

            Assert.NotEqual("ServerKindLocal", server.KindDisplayName);
            Assert.False(string.IsNullOrWhiteSpace(server.KindDisplayName));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
