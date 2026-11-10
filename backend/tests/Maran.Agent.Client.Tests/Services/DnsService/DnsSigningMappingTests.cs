using Maran.Agent.Client.Services.DnsService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;
using Maran.SharedKernel.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.DnsService;

/// <summary>
/// How a zone's signing travels to and from the agent: the instruction and the whole key go out with every
/// publication, the observation and the host's capability come back with unknown values read as not observed,
/// and the private key is never printed by any type on the way.
/// </summary>
public sealed class DnsSigningMappingTests
{
    /// <summary>A recognisable private scalar, so every search for a leak looks for one exact string.</summary>
    private const string PlantedPrivateKey = "UExBTlRFRC1QUklWQVRFLVNDQUxBUi0zMi1CWVRFUw==";

    /// <summary>The public half sent beside it.</summary>
    private const string PublicKey = "GojIhhXUN/u4v54ZQqGSnyhWJwaubCvTmeexv7bR6edbkrSqQpF64cYbcB7wNcP+e+MAnLr+Wi9xMWyQLc8NAA==";

    /// <summary>A zone that carries the key in the given phase.</summary>
    /// <param name="phase">The signing phase.</param>
    /// <returns>The zone definition.</returns>
    private static DnsZoneDefinition SignedZone(DnsSigningPhase phase)
    {
        var key = new DnsSigningKeyDefinition(PublicKey, new SensitiveString(PlantedPrivateKey), 257, 55648);

        return new DnsZoneDefinition(
            "example.net",
            2026110901,
            "ns1.example.net",
            "hostmaster.example.net",
            ["ns1.example.net"],
            [],
            new DnsSigningDefinition(phase, key));
    }

    /// <summary>Builds a client over a stub with a logger that discards everything.</summary>
    /// <param name="stub">The stubbed transport.</param>
    /// <returns>The client.</returns>
    private static AgentDnsClient Client(StubDnsService stub)
    {
        return new AgentDnsClient(stub, NullLogger<AgentDnsClient>.Instance);
    }

    /// <summary>A signed zone is published with its mode and its whole key in the fields the contract names.</summary>
    [Theory]
    [InlineData(DnsSigningPhase.Active, DnsSigningMode.Signed)]
    [InlineData(DnsSigningPhase.Withdrawing, DnsSigningMode.Withdrawing)]
    public async Task A_signed_zone_is_published_with_its_mode_and_whole_key(DnsSigningPhase phase, DnsSigningMode mode)
    {
        var stub = new StubDnsService { PutZoneResponse = new PutZoneResponse { Ok = new PutZoneOk { Serial = 1 } } };

        await Client(stub).PutZoneAsync(SignedZone(phase), CancellationToken.None);

        var signing = Assert.IsType<PutZoneRequest>(stub.LastPutZoneRequest).Signing;
        Assert.Equal(mode, signing.Mode);
        Assert.Equal(PublicKey, signing.Key.PublicKey);
        Assert.Equal(PlantedPrivateKey, signing.Key.PrivateKey);
        Assert.Equal(257u, signing.Key.Flags);
        Assert.Equal(55648u, signing.Key.KeyTag);
    }

    /// <summary>Inverse control: a zone with no signing instruction is published with none, not with an empty key.</summary>
    [Fact]
    public async Task An_unsigned_zone_is_published_without_a_signing_instruction()
    {
        var stub = new StubDnsService { PutZoneResponse = new PutZoneResponse { Ok = new PutZoneOk { Serial = 1 } } };
        var zone = SignedZone(DnsSigningPhase.Active) with { Signing = null };

        await Client(stub).PutZoneAsync(zone, CancellationToken.None);

        Assert.Null(Assert.IsType<PutZoneRequest>(stub.LastPutZoneRequest).Signing);
    }

    /// <summary>An explicit unsigned instruction is sent as unsigned and carries no key.</summary>
    [Fact]
    public async Task An_explicitly_unsigned_zone_is_published_as_unsigned_with_no_key()
    {
        var stub = new StubDnsService { PutZoneResponse = new PutZoneResponse { Ok = new PutZoneOk { Serial = 1 } } };
        var zone = SignedZone(DnsSigningPhase.Active) with
        {
            Signing = new DnsSigningDefinition(DnsSigningPhase.Off, null),
        };

        await Client(stub).PutZoneAsync(zone, CancellationToken.None);

        var signing = Assert.IsType<PutZoneRequest>(stub.LastPutZoneRequest).Signing;
        Assert.Equal(DnsSigningMode.Unsigned, signing.Mode);
        Assert.Null(signing.Key);
    }

    /// <summary>
    /// The private key appears in no text a definition produces. The positive control is the wire request, which
    /// does print it, so the search is known to be able to see the planted value.
    /// </summary>
    [Fact]
    public async Task The_private_key_is_in_no_text_the_definition_produces_though_the_wire_request_prints_it()
    {
        var stub = new StubDnsService { PutZoneResponse = new PutZoneResponse { Ok = new PutZoneOk { Serial = 1 } } };
        var zone = SignedZone(DnsSigningPhase.Active);
        await Client(stub).PutZoneAsync(zone, CancellationToken.None);

        var wireText = Assert.IsType<PutZoneRequest>(stub.LastPutZoneRequest).ToString();
        Assert.Contains(PlantedPrivateKey, wireText, StringComparison.Ordinal);

        Assert.DoesNotContain(PlantedPrivateKey, zone.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(PlantedPrivateKey, zone.Signing!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(PlantedPrivateKey, zone.Signing!.Key!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(PlantedPrivateKey, $"{zone}", StringComparison.Ordinal);
        Assert.Contains(PublicKey, zone.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A refusal that quotes the key back is logged with the key removed, and the key is not in the returned error.</summary>
    [Fact]
    public async Task A_refusal_that_quotes_the_private_key_is_logged_without_it()
    {
        var wireFailure = new AgentError
        {
            Code = ErrorCode.InvalidInput,
            Message = $"the key {PlantedPrivateKey} was refused",
            ToolOutput = $"private {PlantedPrivateKey}",
        };
        var stub = new StubDnsService { PutZoneResponse = new PutZoneResponse { Error = wireFailure } };
        var logger = new RecordingLogger<AgentDnsClient>();

        var result = await new AgentDnsClient(stub, logger).PutZoneAsync(
            SignedZone(DnsSigningPhase.Active), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(PlantedPrivateKey, result.Error!.Code, StringComparison.Ordinal);
        Assert.NotEmpty(logger.Messages);
        Assert.All(logger.Messages, message =>
        {
            Assert.DoesNotContain(PlantedPrivateKey, message, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Positive control for the test above: with the sent secret not named, the translator would have logged
    /// the quoted value, so the logger and the search can see one.
    /// </summary>
    [Fact]
    public async Task A_refusal_about_an_unsigned_zone_is_logged_as_the_agent_said_it()
    {
        var wireFailure = new AgentError { Code = ErrorCode.InvalidInput, Message = "marker-not-a-key refused" };
        var stub = new StubDnsService { PutZoneResponse = new PutZoneResponse { Error = wireFailure } };
        var logger = new RecordingLogger<AgentDnsClient>();

        await new AgentDnsClient(stub, logger).PutZoneAsync(
            SignedZone(DnsSigningPhase.Active) with { Signing = null }, CancellationToken.None);

        Assert.Contains(logger.Messages, message =>
        {
            return message.Contains("marker-not-a-key", StringComparison.Ordinal);
        });
    }

    /// <summary>A served zone's observation maps each fact to its own answer.</summary>
    [Fact]
    public async Task A_served_zones_signing_observation_is_mapped_fact_by_fact()
    {
        var stub = new StubDnsService
        {
            GetZoneResponse = new GetZoneResponse
            {
                Ok = new GetZoneOk
                {
                    Served = true,
                    Serial = 5,
                    Signing = new DnsSigningObservation
                    {
                        Mode = DnsSigningMode.Withdrawing,
                        ModeObserved = DnsPolicyObservation.InForce,
                        PublishedKeyTag = 55648,
                        PublishedKeyObserved = DnsPolicyObservation.InForce,
                    },
                },
            },
        };

        var result = await Client(stub).GetZoneAsync("example.net", CancellationToken.None);

        var observed = result.Value.Signing!;
        Assert.Equal(DnsSigningPhase.Withdrawing, observed.Phase);
        Assert.Equal(DnsPolicyState.InForce, observed.PhaseObserved);
        Assert.Equal(55648, observed.PublishedKeyTag);
        Assert.Equal(DnsPolicyState.InForce, observed.PublishedKeyObserved);
    }

    /// <summary>An unknown wire value, or a fact the daemon could not be asked, reads as not observed and carries no number.</summary>
    [Fact]
    public async Task An_unknown_or_unasked_signing_fact_reads_as_not_observed()
    {
        var stub = new StubDnsService
        {
            GetZoneResponse = new GetZoneResponse
            {
                Ok = new GetZoneOk
                {
                    Served = true,
                    Serial = 5,
                    Signing = new DnsSigningObservation
                    {
                        Mode = (DnsSigningMode)99,
                        ModeObserved = DnsPolicyObservation.InForce,
                        PublishedKeyTag = 777,
                        PublishedKeyObserved = DnsPolicyObservation.NotInForce,
                    },
                },
            },
        };

        var observed = (await Client(stub).GetZoneAsync("example.net", CancellationToken.None)).Value.Signing!;

        Assert.Equal(DnsPolicyState.NotObserved, observed.PhaseObserved);
        Assert.Equal(DnsSigningPhase.Off, observed.Phase);
        Assert.Equal(DnsPolicyState.NotInForce, observed.PublishedKeyObserved);
        Assert.Equal(0, observed.PublishedKeyTag);
    }

    /// <summary>A mode that was not read from the daemon is not trusted even when the wire names one.</summary>
    [Fact]
    public async Task A_mode_the_daemon_was_not_asked_about_is_not_trusted()
    {
        var stub = new StubDnsService
        {
            GetZoneResponse = new GetZoneResponse
            {
                Ok = new GetZoneOk
                {
                    Served = true,
                    Signing = new DnsSigningObservation
                    {
                        Mode = DnsSigningMode.Signed,
                        ModeObserved = DnsPolicyObservation.NotObserved,
                    },
                },
            },
        };

        var observed = (await Client(stub).GetZoneAsync("example.net", CancellationToken.None)).Value.Signing!;

        Assert.Equal(DnsPolicyState.NotObserved, observed.PhaseObserved);
        Assert.Equal(DnsSigningPhase.Off, observed.Phase);
    }

    /// <summary>An answer with no observation (an agent older than the signing contract) reads as no observation.</summary>
    [Fact]
    public async Task An_answer_with_no_signing_observation_carries_none()
    {
        var stub = new StubDnsService
        {
            GetZoneResponse = new GetZoneResponse { Ok = new GetZoneOk { Served = true, Serial = 5 } },
        };

        var result = await Client(stub).GetZoneAsync("example.net", CancellationToken.None);

        Assert.Null(result.Value.Signing);
    }

    /// <summary>The host's capability is read from the status, and an answer that does not state it reads as unable.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_daemon_status_carries_whether_the_host_can_sign(bool supported)
    {
        var stub = new StubDnsService
        {
            GetDnsStatusResponse = new GetDnsStatusResponse
            {
                Ok = new GetDnsStatusOk { Status = new DnsDaemonStatus { DnssecSupported = supported } },
            },
        };

        var result = await Client(stub).GetStatusAsync(CancellationToken.None);

        Assert.Equal(supported, result.Value.DnssecSupported);
    }
}
