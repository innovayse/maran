using Maran.Agent.Client.Services.SslService;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Services.SslService;

/// <summary>Mapping contract of the panel-certificate rpcs of AgentSslClient (issue #132).</summary>
public sealed class AgentSslClientPanelTests
{
    /// <summary>A panel install answered ok carries the expiry and sends both halves.</summary>
    [Fact]
    public async Task A_panel_install_answered_ok_carries_the_expiry_and_sends_both_halves()
    {
        var stub = new StubSslService
        {
            InstallPanelResponse = new InstallPanelCertificateResponse
            {
                Ok = new InstallPanelCertificateOk { ExpiresAtUnix = 1_900_000_000 },
            },
        };
        var client = new AgentSslClient(stub, NullLogger<AgentSslClient>.Instance);

        var result = await client.InstallPanelCertificateAsync("CERT", "KEY", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_900_000_000), result.Value.ExpiresAt);
        Assert.Equal("CERT", stub.LastPanelRequest!.CertificatePem);
        Assert.Equal("KEY", stub.LastPanelRequest.PrivateKeyPem);
    }

    /// <summary>A refused panel pair maps to the refusal's own code.</summary>
    [Fact]
    public async Task A_refused_panel_pair_maps_to_the_refusals_own_code()
    {
        var stub = new StubSslService
        {
            InstallPanelResponse = new InstallPanelCertificateResponse
            {
                Refused = new InstallCertificateRefused
                {
                    Reason = CertificateMaterialRefusal.KeyDoesNotMatchCertificate,
                },
            },
        };
        var client = new AgentSslClient(stub, NullLogger<AgentSslClient>.Instance);

        var result = await client.InstallPanelCertificateAsync("CERT", "KEY", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            CertificateMaterialRefusalTranslator.ToError(CertificateMaterialRefusal.KeyDoesNotMatchCertificate).Code,
            result.Error!.Code);
    }

    /// <summary>A challenge refused at the free-space floor keeps the floor's own code.</summary>
    [Fact]
    public async Task A_challenge_refused_at_the_free_space_floor_keeps_the_floors_own_code()
    {
        var stub = new StubSslService
        {
            WriteChallengeResponse = new WritePanelChallengeResponse
            {
                Error = new AgentError { Code = ErrorCode.BelowFreeSpaceFloor, Message = "below the floor" },
            },
        };
        var client = new AgentSslClient(stub, NullLogger<AgentSslClient>.Instance);

        var result = await client.WritePanelChallengeAsync("tok", "tok.abc", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentBelowFreeSpaceFloor", result.Error!.Code);
        Assert.Equal("tok", stub.LastChallengeRequest!.Token);
        Assert.Equal("tok.abc", stub.LastChallengeRequest.KeyAuthorization);
    }

    /// <summary>A challenge removal answered ok is a success, and an unset oneof is an invalid response.</summary>
    [Fact]
    public async Task A_challenge_removal_answered_ok_is_a_success_and_an_unset_oneof_is_invalid()
    {
        var answered = new StubSslService
        {
            RemoveChallengeResponse = new RemovePanelChallengeResponse { Ok = new RemovePanelChallengeOk() },
        };

        var removed = await new AgentSslClient(answered, NullLogger<AgentSslClient>.Instance)
            .RemovePanelChallengeAsync("tok", CancellationToken.None);
        var unset = await new AgentSslClient(new StubSslService(), NullLogger<AgentSslClient>.Instance)
            .RemovePanelChallengeAsync("tok", CancellationToken.None);

        Assert.True(removed.IsSuccess);
        Assert.Equal("AgentInvalidResponse", unset.Error!.Code);
    }
}
