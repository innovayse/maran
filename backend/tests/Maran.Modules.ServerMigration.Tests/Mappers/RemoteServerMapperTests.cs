using System.Reflection;
using System.Text.Json;
using Maran.Agent.Client.Services.TransferService;
using Maran.Modules.ServerMigration.Common;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Mappers;
using Maran.Modules.ServerMigration.Tests.TestSupport;
using Maran.SharedKernel.Security;

namespace Maran.Modules.ServerMigration.Tests.Mappers;

/// <summary>
/// The wire shapes of a registered server, and the rule that no response of the module can carry the private key.
/// The hunt for the planted key is proven able to find it before its silence is believed.
/// </summary>
public sealed class RemoteServerMapperTests
{
    /// <summary>The planted private key, unmistakable in any output.</summary>
    private const string Sentinel = RemoteServerTestValues.PrivateKeySentinel;

    /// <summary>
    /// The outward members that hold a secret on purpose: the bearer token of an export grant and the credential an
    /// account issue hands over, each shown once at creation. Neither is a registered source's private key, which is
    /// what the scan guards.
    /// </summary>
    private static readonly string[] OneTimeSecrets = ["ExportGrantDto.Token", "IssuedSecretDto.Secret"];

    /// <summary>The wire shapes a server and its discovery are put in, built from a server holding the planted key.</summary>
    /// <returns>Every outward shape of the module that describes a server, serialised.</returns>
    private static IReadOnlyList<string> EveryShape()
    {
        var server = RemoteServerTestValues.Ready();
        var names = new EchoDisplayNames();
        var warning = new DiscoveryWarningDto(DiscoveryWarningKind.Truncated, "accounts", "message");
        var account = new DiscoveredAccount(Guid.NewGuid(), server.Id, "alice", "/home/alice", 5, 1, 2);
        account.AddSite(Guid.NewGuid(), "alice.example", ["www.alice.example"], DiscoveredSiteBackend.Php, "8.2", "/home/alice/p");
        account.AddDatabase(Guid.NewGuid(), "alice_db", 7);

        return
        [
            JsonSerializer.Serialize(RemoteServerMapper.ToDto(server, names)),
            JsonSerializer.Serialize(RemoteServerMapper.ToDetail(server, [warning], names)),
            JsonSerializer.Serialize(new RemovedRemoteServerDto(server.PublicKey, server.HostKeyFingerprint)),
            JsonSerializer.Serialize(new DiscoveryResultDto(RemoteServerTestValues.LaterAt, 1, [warning])),
            JsonSerializer.Serialize(DiscoveredAccountMapper.ToDto(account)),
            JsonSerializer.Serialize(DiscoveredAccountMapper.ToDetail(account, names)),
        ];
    }

    /// <summary>No shape the module sends carries the private key.</summary>
    [Fact]
    public void No_shape_the_module_sends_carries_the_private_key()
    {
        var shapes = EveryShape();

        Assert.Equal(6, shapes.Count);
        foreach (var json in shapes)
        {
            Assert.DoesNotContain(Sentinel, json, StringComparison.Ordinal);
        }
    }

    /// <summary>The positive control: the same hunt finds the key in the entity that holds it, and in a shape that does carry it.</summary>
    [Fact]
    public void The_hunt_finds_the_key_where_it_is()
    {
        var server = RemoteServerTestValues.Ready();

        Assert.Contains(Sentinel, JsonSerializer.Serialize(server), StringComparison.Ordinal);
        Assert.Contains(Sentinel, JsonSerializer.Serialize(new LeakingDetail(server.PrivateKeyCipher)), StringComparison.Ordinal);
    }

    /// <summary>No outward type of the module has a member that could hold a private key; the same scan flags a planted one.</summary>
    [Fact]
    public void No_outward_type_has_a_member_that_could_hold_a_private_key()
    {
        var holders = HoldersOfPrivateKeys(typeof(RemoteServerDto).Assembly
            .GetTypes()
            .Where(type =>
            {
                return type.IsPublic && type.Namespace == typeof(RemoteServerDto).Namespace;
            }));
        var offenders = holders.Where(holder => { return !OneTimeSecrets.Contains(holder); }).ToList();
        var planted = HoldersOfPrivateKeys([typeof(LeakingDetail)]);

        Assert.Empty(offenders);
        Assert.Equal(["LeakingDetail.PrivateKey"], planted);

        // The exemptions are alive: each names a member the scan still finds, so a renamed one cannot hide behind a stale entry.
        Assert.All(OneTimeSecrets, secret => { Assert.Contains(secret, holders); });
    }

    /// <summary>The credential handed to the agent prints as a mask, and reveals the key only to the one call that reads it.</summary>
    [Fact]
    public void The_credential_prints_as_a_mask()
    {
        var server = RemoteServerTestValues.Pinned();

        var credential = RemoteServerAgentMapper.CredentialOf(server);

        Assert.DoesNotContain(Sentinel, credential.ToString(), StringComparison.Ordinal);
        Assert.Equal(Sentinel, credential.PrivateKey.Reveal());
        Assert.Equal(server.HostKey, credential.HostKey);
        Assert.Equal(new AgentSourceEndpoint(server.Host, server.Port, server.SshUser), RemoteServerAgentMapper.EndpointOf(server));
    }

    /// <summary>A credential cannot be built for a server whose host key is not pinned; a pinned one can.</summary>
    [Fact]
    public void A_credential_cannot_be_built_without_a_pinned_host_key()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            return RemoteServerAgentMapper.CredentialOf(RemoteServerTestValues.Scanned());
        });
        Assert.NotNull(RemoteServerAgentMapper.CredentialOf(RemoteServerTestValues.Pinned()));
    }

    /// <summary>The audit subject names the host and the identifier and no key.</summary>
    [Fact]
    public void The_audit_subject_names_the_host_and_the_identifier_and_no_key()
    {
        var server = RemoteServerTestValues.Ready();

        var subject = RemoteServerAuditSubject.Of(server);

        Assert.Equal($"{server.Host} ({server.Id})", subject);
        Assert.DoesNotContain(Sentinel, subject, StringComparison.Ordinal);
        Assert.DoesNotContain(server.PublicKey, subject, StringComparison.Ordinal);
        Assert.Equal("old.example.net:22", RemoteServerAuditSubject.OfAttempt("old.example.net", 22));
    }

    /// <summary>Names the members of the given types whose name says they hold a private key or a secret.</summary>
    /// <param name="types">The types to scan.</param>
    /// <returns>Each offender as <c>Type.Member</c>.</returns>
    private static List<string> HoldersOfPrivateKeys(IEnumerable<Type> types)
    {
        return types
            .SelectMany(type =>
            {
                return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(property =>
                    {
                        return property.Name.Contains("PrivateKey", StringComparison.OrdinalIgnoreCase)
                            || property.Name.Contains("Cipher", StringComparison.OrdinalIgnoreCase)
                            || property.PropertyType == typeof(SensitiveString);
                    })
                    .Select(property =>
                    {
                        return $"{type.Name}.{property.Name}";
                    });
            })
            .ToList();
    }

    /// <summary>A shape that carries a private key, which the scan must flag. It exists only to prove the scan can see.</summary>
    /// <param name="PrivateKey">The key it should never carry.</param>
    private sealed record LeakingDetail(string PrivateKey);
}
