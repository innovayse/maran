using Maran.Agent.Client.Services.DnsService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.DnsService;

/// <summary>Mapping contract of AgentDnsClient: every request field and every oneof arm.</summary>
public sealed class AgentDnsClientTests
{
    /// <summary>A zone that sets every field, each to a value no other field shares.</summary>
    /// <returns>The zone definition.</returns>
    private static DnsZoneDefinition FullZone()
    {
        return new DnsZoneDefinition(
            "example.com",
            2026110701,
            "ns1.example.net",
            "hostmaster.example.net",
            ["ns1.example.net", "ns2.example.net"],
            [
                new DnsRecordDefinition("www", DnsRecordKind.A, 300, "203.0.113.10", 0, 0, 0, 0, string.Empty),
                new DnsRecordDefinition("www", DnsRecordKind.Aaaa, 301, "2001:db8::10", 0, 0, 0, 0, string.Empty),
                new DnsRecordDefinition("alias", DnsRecordKind.Cname, 302, "www.example.com", 0, 0, 0, 0, string.Empty),
                new DnsRecordDefinition("@", DnsRecordKind.Mx, 303, "mail.example.com", 10, 0, 0, 0, string.Empty),
                new DnsRecordDefinition("@", DnsRecordKind.Txt, 304, "v=spf1 -all", 0, 0, 0, 0, string.Empty),
                new DnsRecordDefinition("_sip._tcp", DnsRecordKind.Srv, 305, "sip.example.com", 5, 20, 5060, 0, string.Empty),
                new DnsRecordDefinition("@", DnsRecordKind.Caa, 306, "letsencrypt.org", 0, 0, 0, 128, "issue"),
                new DnsRecordDefinition("sub", DnsRecordKind.Ns, 307, "ns.sub.example.com", 0, 0, 0, 0, string.Empty),
            ]);
    }

    /// <summary>A wire status that sets every field the panel reads, each to a distinct value.</summary>
    /// <returns>The wire status.</returns>
    private static DnsDaemonStatus FullWireStatus()
    {
        return new DnsDaemonStatus
        {
            Running = true,
            Port53Answering = false,
            EnabledAtBoot = true,
            RecursionRefused = DnsPolicyObservation.InForce,
            ResponseRateLimiting = DnsPolicyObservation.NotInForce,
        };
    }

    /// <summary>Builds a client over a stub with a logger that discards everything.</summary>
    /// <param name="stub">The stubbed transport.</param>
    /// <returns>The client.</returns>
    private static AgentDnsClient Client(StubDnsService stub)
    {
        return new AgentDnsClient(stub, NullLogger<AgentDnsClient>.Instance);
    }

    /// <summary>Putting a zone sends its apex, SOA, name servers and every record, in the field each type reads.</summary>
    [Fact]
    public async Task Putting_a_zone_sends_every_field_in_the_slot_its_type_reads()
    {
        var stub = new StubDnsService { PutZoneResponse = new PutZoneResponse { Ok = new PutZoneOk { Serial = 2026110702 } } };

        var result = await Client(stub).PutZoneAsync(FullZone(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("example.com", result.Value.Name);
        Assert.Equal(2026110702L, result.Value.Serial);
        var request = Assert.IsType<PutZoneRequest>(stub.LastPutZoneRequest);
        Assert.Equal("example.com", request.ZoneName);
        Assert.Equal("ns1.example.net", request.Soa.PrimaryNameserver);
        Assert.Equal("hostmaster.example.net", request.Soa.Hostmaster);
        Assert.Equal(2026110701u, request.Soa.Serial);
        Assert.Equal(["ns1.example.net", "ns2.example.net"], request.Nameservers);
        Assert.Equal(8, request.Records.Count);

        var a = request.Records[0];
        Assert.Equal(DnsRecordType.A, a.Type);
        Assert.Equal(300u, a.Ttl);
        Assert.Equal("203.0.113.10", a.Address);
        Assert.Equal(string.Empty, a.Target);
        Assert.Equal("2001:db8::10", request.Records[1].Address);
        Assert.Equal(DnsRecordType.Aaaa, request.Records[1].Type);
        Assert.Equal("www.example.com", request.Records[2].Target);
        Assert.Equal(DnsRecordType.Cname, request.Records[2].Type);
        var mx = request.Records[3];
        Assert.Equal(DnsRecordType.Mx, mx.Type);
        Assert.Equal("mail.example.com", mx.Target);
        Assert.Equal(10u, mx.Priority);
        Assert.Equal("v=spf1 -all", request.Records[4].Text);
        Assert.Equal(DnsRecordType.Txt, request.Records[4].Type);
        var srv = request.Records[5];
        Assert.Equal(DnsRecordType.Srv, srv.Type);
        Assert.Equal("sip.example.com", srv.Target);
        Assert.Equal(5u, srv.Priority);
        Assert.Equal(20u, srv.Weight);
        Assert.Equal(5060u, srv.Port);
        var caa = request.Records[6];
        Assert.Equal(DnsRecordType.Caa, caa.Type);
        Assert.Equal(128u, caa.CaaFlags);
        Assert.Equal("issue", caa.CaaTag);
        Assert.Equal("letsencrypt.org", caa.CaaValue);
        Assert.Equal(string.Empty, caa.Target);
        Assert.Equal(DnsRecordType.Ns, request.Records[7].Type);
        Assert.Equal("ns.sub.example.com", request.Records[7].Target);
    }

    /// <summary>Enable, disable and status all carry every observed fact of the daemon.</summary>
    [Fact]
    public async Task The_daemon_calls_map_every_observed_fact()
    {
        var stub = new StubDnsService
        {
            EnableDnsResponse = new EnableDnsResponse { Ok = new EnableDnsOk { Status = FullWireStatus() } },
            DisableDnsResponse = new DisableDnsResponse { Ok = new DisableDnsOk { Status = FullWireStatus() } },
            GetDnsStatusResponse = new GetDnsStatusResponse { Ok = new GetDnsStatusOk { Status = FullWireStatus() } },
        };
        var client = Client(stub);

        var results = new[]
        {
            await client.EnableAsync(CancellationToken.None),
            await client.DisableAsync(CancellationToken.None),
            await client.GetStatusAsync(CancellationToken.None),
        };

        foreach (var result in results)
        {
            Assert.True(result.IsSuccess);
            Assert.True(result.Value.Running);
            Assert.False(result.Value.Listening);
            Assert.True(result.Value.EnabledAtBoot);
            Assert.Equal(DnsPolicyState.InForce, result.Value.RecursionRefused);
            Assert.Equal(DnsPolicyState.NotInForce, result.Value.ResponseRateLimiting);
        }
    }

    /// <summary>A safety property the agent did not look at reads as not observed, never as in force.</summary>
    [Fact]
    public async Task A_policy_the_agent_did_not_observe_is_not_read_as_in_force()
    {
        var stub = new StubDnsService
        {
            GetDnsStatusResponse = new GetDnsStatusResponse { Ok = new GetDnsStatusOk { Status = new DnsDaemonStatus() } },
        };

        var result = await Client(stub).GetStatusAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DnsPolicyState.NotObserved, result.Value.RecursionRefused);
        Assert.Equal(DnsPolicyState.NotObserved, result.Value.ResponseRateLimiting);
    }

    /// <summary>Deleting a zone sends its name and an Ok answer succeeds.</summary>
    [Fact]
    public async Task Deleting_a_zone_sends_its_name()
    {
        var stub = new StubDnsService { DeleteZoneResponse = new DeleteZoneResponse { Ok = new DeleteZoneOk() } };

        var result = await Client(stub).DeleteZoneAsync("example.com", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("example.com", Assert.IsType<DeleteZoneRequest>(stub.LastDeleteZoneRequest).ZoneName);
    }

    /// <summary>A zone the daemon serves reads back its name and serial.</summary>
    [Fact]
    public async Task A_served_zone_reads_back_its_name_and_serial()
    {
        var stub = new StubDnsService
        {
            GetZoneResponse = new GetZoneResponse { Ok = new GetZoneOk { Served = true, Serial = 77 } },
        };

        var result = await Client(stub).GetZoneAsync("example.com", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("example.com", result.Value.Name);
        Assert.Equal(77L, result.Value.Serial);
        Assert.Equal("example.com", Assert.IsType<GetZoneRequest>(stub.LastGetZoneRequest).ZoneName);
    }

    /// <summary>A zone the daemon does not serve is a typed not-found, not a zero serial.</summary>
    [Fact]
    public async Task A_zone_the_daemon_does_not_serve_is_not_found()
    {
        var stub = new StubDnsService
        {
            GetZoneResponse = new GetZoneResponse { Ok = new GetZoneOk { Served = false, Serial = 0 } },
        };

        var result = await Client(stub).GetZoneAsync("example.com", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
    }

    /// <summary>The list of zones carries every name the daemon serves.</summary>
    [Fact]
    public async Task Listing_zones_carries_every_served_name()
    {
        var served = new ListZonesOk();
        served.ZoneNames.Add("example.com");
        served.ZoneNames.Add("example.org");
        var stub = new StubDnsService { ListZonesResponse = new ListZonesResponse { Ok = served } };

        var result = await Client(stub).ListZonesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["example.com", "example.org"], result.Value);
    }

    /// <summary>A failure keeps the agent code, hides its words from the result and logs them once.</summary>
    [Fact]
    public async Task A_failure_carries_the_agent_code_and_none_of_its_words()
    {
        var logger = new RecordingLogger<AgentDnsClient>();
        var stub = new StubDnsService
        {
            PutZoneResponse = new PutZoneResponse
            {
                Error = new AgentError
                {
                    Code = ErrorCode.InvalidInput,
                    Message = "named-checkzone: /etc/bind/maran/example.com.zone: bad owner",
                },
            },
        };

        var result = await new AgentDnsClient(stub, logger).PutZoneAsync(FullZone(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentInvalidInput", result.Error!.Code);
        Assert.DoesNotContain("/etc/bind", result.Error.Code, StringComparison.Ordinal);
        Assert.Contains("/etc/bind/maran", Assert.Single(logger.Messages), StringComparison.Ordinal);
    }

    /// <summary>Every call refuses a response that carries neither branch.</summary>
    [Fact]
    public async Task Every_call_refuses_a_response_with_neither_branch_set()
    {
        var client = Client(new StubDnsService());
        var token = CancellationToken.None;

        var results = new[]
        {
            (await client.EnableAsync(token)).Error,
            (await client.DisableAsync(token)).Error,
            (await client.GetStatusAsync(token)).Error,
            (await client.PutZoneAsync(FullZone(), token)).Error,
            (await client.DeleteZoneAsync("example.com", token)).Error,
            (await client.GetZoneAsync("example.com", token)).Error,
            (await client.ListZonesAsync(token)).Error,
        };

        Assert.All(results, error =>
        {
            Assert.Equal("AgentInvalidResponse", error!.Code);
        });
    }
}
