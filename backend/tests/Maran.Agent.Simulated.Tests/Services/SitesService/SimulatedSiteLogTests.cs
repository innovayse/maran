using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Simulated.Services.SitesService;

namespace Maran.Agent.Simulated.Tests.Services.SitesService;

/// <summary>The log lines a simulated site "has served" are a pure function of their inputs.</summary>
public sealed class SimulatedSiteLogTests
{
    /// <summary>The moment the newest log line is stamped with.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The same arguments always give the same line, so a log looks the same each time.</summary>
    [Fact]
    public void The_same_arguments_give_the_same_line()
    {
        var first = SimulatedSiteLog.Line("shop.example.test", SiteLogSource.Access, 3, Now);
        var second = SimulatedSiteLog.Line("shop.example.test", SiteLogSource.Access, 3, Now);

        Assert.Equal(first, second);
    }

    /// <summary>Different positions in the log give different lines.</summary>
    [Fact]
    public void Different_positions_give_different_lines()
    {
        var lines = Enumerable.Range(0, 20)
            .Select(index => { return SimulatedSiteLog.Line("shop.example.test", SiteLogSource.Access, index, Now); })
            .Distinct(StringComparer.Ordinal)
            .Count();

        Assert.Equal(20, lines);
    }

    /// <summary>Every client address is from the documentation range, never one that could be a person's.</summary>
    [Fact]
    public void Client_addresses_come_from_the_documentation_range()
    {
        for (var index = 0; index < 50; index++)
        {
            var access = SimulatedSiteLog.Line("a.example.test", SiteLogSource.Access, index, Now);
            var error = SimulatedSiteLog.Line("a.example.test", SiteLogSource.Error, index, Now);

            Assert.Contains("203.0.113.", access, StringComparison.Ordinal);
            Assert.Contains("203.0.113.", error, StringComparison.Ordinal);
        }
    }

    /// <summary>The error log names the site it belongs to.</summary>
    [Fact]
    public void An_error_line_names_the_site()
    {
        var line = SimulatedSiteLog.Line("shop.example.test", SiteLogSource.Error, 0, Now);

        Assert.Contains("server: shop.example.test", line, StringComparison.Ordinal);
        Assert.StartsWith("2026/11/07 09:00:00 [", line, StringComparison.Ordinal);
    }
}
