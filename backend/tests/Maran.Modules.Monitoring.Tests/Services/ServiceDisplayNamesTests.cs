using System.Globalization;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Tests.TestSupport;

namespace Maran.Modules.Monitoring.Tests.Services;

/// <summary>
/// Covers the operator-facing name of a watched service, and — the part that makes this a fix
/// rather than four translations — that EVERY service a row can carry has one.
/// </summary>
/// <remarks>
/// The pinned defect, measured in a browser: the services card rendered <c>webServer</c>,
/// <c>database</c>, <c>cron</c> and <c>ssh</c> — machine constants, verbatim — beside localized
/// Russian status badges. Naming the four that happened to be on screen would not have been a fix,
/// because a member with no entry falls back to the raw constant and IS the defect; so the closed
/// set the rows come from is walked here rather than sampled. That set is
/// <see cref="AgentManagedService"/> itself: the agent client's <c>ToPanelService</c> maps every
/// wire value onto a member of it (anything unknown onto <c>Unspecified</c>), so no row can carry
/// anything this walk does not visit.
/// </remarks>
public sealed class ServiceDisplayNamesTests
{
    /// <summary>Every service a row can carry has a name an operator can read.</summary>
    /// <remarks>
    /// The whole enum, no exemptions — <c>Unspecified</c> included, because a newer agent watching
    /// a unit this panel predates reaches the screen as exactly that member, and <c>PhpFpm</c> and
    /// <c>Ftp</c> included, because "the agent never reports it today" is a fact about the agent,
    /// not about what this resolver must be able to answer. A member added to the enum without an
    /// entry fails this test by name rather than reaching a card as a constant.
    /// </remarks>
    [Fact]
    public void Every_service_a_row_can_carry_has_a_name_an_operator_can_read()
    {
        var names = MonitoringTestContext.ServiceNames();
        var unnamed = new List<string>();

        foreach (var service in Enum.GetValues<AgentManagedService>())
        {
            // The resolver's own fallback is the camelCase wire spelling, so a member whose answer
            // is that spelling is a member nobody named.
            if (string.Equals(names.Of(service), WireSpellingOf(service), StringComparison.Ordinal))
            {
                unnamed.Add(service.ToString());
            }
        }

        Assert.Empty(unnamed);
    }

    /// <summary>The English name is the phrase it should be, stated exactly.</summary>
    /// <remarks>
    /// A test asserting "a name came back" would pass against a card full of raw constants, which
    /// is the defect. The VALUE is asserted (rules/testing.md).
    /// </remarks>
    [Fact]
    public void The_english_name_of_the_web_server_is_the_phrase_it_should_be()
    {
        var names = MonitoringTestContext.ServiceNames();

        Assert.Equal("Web server", names.Of(AgentManagedService.WebServer));
    }

    /// <summary>In Russian the name is Russian, which is the whole of what was measured as broken.</summary>
    /// <remarks>
    /// The exact card from the live run: <c>webServer</c>, rendered beside the word "Работает". The
    /// assertion is on the Russian VALUE, so a build that resolved the neutral English text — the
    /// state a missing <c>.ru</c> entry silently produces — fails here.
    /// </remarks>
    [Fact]
    public void The_russian_name_of_the_web_server_is_russian()
    {
        var names = MonitoringTestContext.ServiceNames();
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ru");

            Assert.Equal("Веб-сервер", names.Of(AgentManagedService.WebServer));
            Assert.Equal("База данных", names.Of(AgentManagedService.Database));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>The service the agent could not name is named honestly, not left as a constant.</summary>
    /// <remarks>
    /// <c>Unspecified</c> is the one member that reaches a row when a NEWER agent reports a unit
    /// this panel predates, so its name matters on the day nobody expects it to.
    /// </remarks>
    [Fact]
    public void The_unspecified_service_is_named_an_unknown_service()
    {
        var names = MonitoringTestContext.ServiceNames();

        Assert.Equal("Unknown service", names.Of(AgentManagedService.Unspecified));
    }

    /// <summary>The wire spelling of a member — the camelCase form the JSON surface carries.</summary>
    /// <param name="service">The member to spell.</param>
    /// <returns>The member's name with its first letter lowered, e.g. <c>webServer</c>.</returns>
    private static string WireSpellingOf(AgentManagedService service)
    {
        var member = service.ToString();

        return char.ToLowerInvariant(member[0]) + member[1..];
    }
}
