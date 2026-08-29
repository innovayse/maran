using Maran.Host.HealthChecks;

namespace Maran.Host.Tests.HealthChecks;

public sealed class DatabaseHealthProbeTests
{
    [Fact]
    public async Task Probe_reports_not_configured_when_no_connection_string_is_set()
    {
        var probe = new DatabaseHealthProbe(string.Empty);

        Assert.Equal(DatabaseHealthProbe.NotConfigured, await probe.ProbeAsync());
    }

    [Fact]
    public async Task Probe_reports_unreachable_instead_of_throwing_when_the_database_refuses()
    {
        // Port 1 has nothing listening: the probe must answer, not propagate the transport
        // exception — a readiness endpoint that throws is a 500, which reads as "the panel is
        // broken" rather than "the database is down".
        var probe = new DatabaseHealthProbe("Host=127.0.0.1;Port=1;Database=maran;Username=panel;Timeout=1");

        Assert.Equal(DatabaseHealthProbe.Unreachable, await probe.ProbeAsync());
    }
}
