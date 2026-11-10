using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.Modules.ServerMigration.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.ServerMigration.Tests.Domain.Entities;

/// <summary>
/// What a <see cref="RemoteServer"/> allows itself to become. The pin is the security property of the registry:
/// nothing is connected to before an administrator confirmed a host key the server itself offered, so the refusals
/// are tested as carefully as the transitions, each beside an accepting control.
/// </summary>
public sealed class RemoteServerTests
{
    /// <summary>A server is registered unconfirmed, with no pin, no candidate and no observation.</summary>
    [Fact]
    public void A_registered_server_is_unconfirmed_and_holds_no_pin_candidate_or_observation()
    {
        var server = RemoteServerTestValues.Registered();

        Assert.Equal(RemoteServerState.HostKeyUnconfirmed, server.State);
        Assert.False(server.IsHostKeyPinned);
        Assert.False(server.MayDiscover);
        Assert.Null(server.HostKey);
        Assert.Null(server.HostKeyAlgorithm);
        Assert.Null(server.HostKeyFingerprint);
        Assert.Null(server.HostKeyConfirmedAt);
        Assert.Empty(server.HostKeyCandidates);
        Assert.Null(server.LastTestedAt);
        Assert.Null(server.LastFailure);
        Assert.Null(server.Kind);
        Assert.Null(server.IsRoot);
        Assert.Null(server.DiscoveredAt);
        Assert.Null(server.AccountsFound);
        Assert.Equal(RemoteServerTestValues.CreatedAt, server.CreatedAt);
    }

    /// <summary>A caller bug in the constructor is an exception, not a stored row.</summary>
    [Fact]
    public void A_server_with_no_identity_or_a_blank_or_oversized_field_is_refused()
    {
        Assert.Throws<ArgumentException>(() => { return Build(id: Guid.Empty); });
        Assert.ThrowsAny<ArgumentException>(() => { return Build(name: " "); });
        Assert.ThrowsAny<ArgumentException>(() => { return Build(name: new string('n', RemoteServerFieldLimits.NameMaxLength + 1)); });
        Assert.ThrowsAny<ArgumentException>(() => { return Build(host: ""); });
        Assert.ThrowsAny<ArgumentException>(() => { return Build(host: new string('h', RemoteServerFieldLimits.HostMaxLength + 1)); });
        Assert.ThrowsAny<ArgumentException>(() => { return Build(port: 0); });
        Assert.ThrowsAny<ArgumentException>(() => { return Build(port: 65536); });
        Assert.ThrowsAny<ArgumentException>(() => { return Build(sshUser: ""); });
        Assert.ThrowsAny<ArgumentException>(() => { return Build(sshUser: new string('u', RemoteServerFieldLimits.SshUserMaxLength + 1)); });
        Assert.ThrowsAny<ArgumentException>(() => { return Build(allowedFrom: " "); });
        Assert.ThrowsAny<ArgumentException>(() => { return Build(publicKey: ""); });
        Assert.ThrowsAny<ArgumentException>(() => { return Build(privateKey: ""); });
        Assert.ThrowsAny<ArgumentException>(() => { return Build(privateKey: new string('k', RemoteServer.PrivateKeyMaxLength + 1)); });
    }

    /// <summary>Control: every field exactly at its bound is accepted, so the refusals above are not a constructor that refuses all.</summary>
    [Fact]
    public void A_server_with_every_field_at_its_bound_is_accepted()
    {
        var server = Build(
            name: new string('n', RemoteServerFieldLimits.NameMaxLength),
            host: new string('h', RemoteServerFieldLimits.HostMaxLength),
            port: 65535,
            sshUser: new string('u', RemoteServerFieldLimits.SshUserMaxLength),
            allowedFrom: new string('a', RemoteServerFieldLimits.AllowedFromMaxLength),
            privateKey: new string('k', RemoteServer.PrivateKeyMaxLength));

        Assert.Equal(65535, server.Port);
        Assert.Equal(RemoteServerFieldLimits.AllowedFromMaxLength, server.AllowedFrom!.Length);
    }

    /// <summary>The server prints no key when it is written out by accident.</summary>
    [Fact]
    public void The_server_never_prints_its_private_key()
    {
        var server = RemoteServerTestValues.Registered();

        Assert.DoesNotContain(RemoteServerTestValues.PrivateKeySentinel, server.ToString(), StringComparison.Ordinal);
        Assert.Equal(RemoteServerTestValues.PrivateKeySentinel, server.PrivateKeyCipher);
    }

    /// <summary>A scan puts the keys on offer and pins none of them.</summary>
    [Fact]
    public void Offering_host_keys_lists_them_and_pins_nothing()
    {
        var server = RemoteServerTestValues.Scanned();

        Assert.Equal(2, server.HostKeyCandidates.Count);
        Assert.Contains(server.HostKeyCandidates, candidate => { return candidate.Fingerprint == RemoteServerTestValues.FingerprintA; });
        Assert.All(server.HostKeyCandidates, candidate => { Assert.Equal(server.Id, candidate.RemoteServerId); });
        Assert.False(server.IsHostKeyPinned);
        Assert.Equal(RemoteServerState.HostKeyUnconfirmed, server.State);
    }

    /// <summary>A second scan replaces the first and a repeated fingerprint is listed once.</summary>
    [Fact]
    public void A_new_offer_replaces_the_old_one_and_lists_a_fingerprint_once()
    {
        var server = RemoteServerTestValues.Scanned();

        server.OfferHostKeys(
        [
            ("ssh-ed25519", "AAAAnew", RemoteServerTestValues.FingerprintB),
            ("ecdsa-sha2-nistp256", "AAAAdup", RemoteServerTestValues.FingerprintB),
        ]);

        var only = Assert.Single(server.HostKeyCandidates);
        Assert.Equal("AAAAnew", only.Key);
        Assert.Equal(RemoteServerTestValues.FingerprintB, only.Fingerprint);
    }

    /// <summary>An offer of nothing, or of more than a scan can produce, is a caller bug.</summary>
    [Fact]
    public void An_empty_or_oversized_offer_is_refused_and_leaves_the_candidates_alone()
    {
        var server = RemoteServerTestValues.Scanned();

        Assert.Throws<ArgumentException>(() => { server.OfferHostKeys([]); });
        var tooMany = Enumerable.Range(0, RemoteServer.MaxHostKeyCandidates + 1)
            .Select(i => { return ("ssh-ed25519", "AAAA" + i, "SHA256:" + i); })
            .ToList();
        Assert.ThrowsAny<ArgumentException>(() => { server.OfferHostKeys(tooMany); });

        Assert.Equal(2, server.HostKeyCandidates.Count);
    }

    /// <summary>The offered fingerprint exactly typed pins that key and nothing else.</summary>
    [Fact]
    public void Pinning_an_offered_fingerprint_pins_that_key_and_moves_to_pinned()
    {
        var server = RemoteServerTestValues.Scanned();

        var pinned = server.PinHostKey(RemoteServerTestValues.FingerprintB, RemoteServerTestValues.LaterAt);

        Assert.True(pinned);
        Assert.Equal(RemoteServerState.Pinned, server.State);
        Assert.True(server.IsHostKeyPinned);
        Assert.True(server.MayDiscover);
        Assert.Equal(RemoteServerTestValues.FingerprintB, server.HostKeyFingerprint);
        Assert.Equal("ssh-rsa", server.HostKeyAlgorithm);
        Assert.Equal("AAAArsa", server.HostKey);
        Assert.Equal(RemoteServerTestValues.LaterAt, server.HostKeyConfirmedAt);
    }

    /// <summary>A fingerprint that is not exactly an offered one pins nothing, however near it is.</summary>
    [Theory]
    [InlineData("SHA256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ")]
    [InlineData(" SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("SHA256:CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC")]
    [InlineData("")]
    public void A_fingerprint_that_is_not_exactly_an_offered_one_pins_nothing(string typed)
    {
        var server = RemoteServerTestValues.Scanned();

        var pinned = server.PinHostKey(typed, RemoteServerTestValues.LaterAt);

        Assert.False(pinned);
        Assert.False(server.IsHostKeyPinned);
        Assert.Null(server.HostKey);
        Assert.Null(server.HostKeyConfirmedAt);
        Assert.Equal(RemoteServerState.HostKeyUnconfirmed, server.State);
    }

    /// <summary>A server that was never scanned has nothing to pin, even for a well-formed fingerprint.</summary>
    [Fact]
    public void A_server_with_no_candidates_pins_nothing()
    {
        var server = RemoteServerTestValues.Registered();

        Assert.False(server.PinHostKey(RemoteServerTestValues.FingerprintA, RemoteServerTestValues.LaterAt));
        Assert.False(server.IsHostKeyPinned);
    }

    /// <summary>Rescanning leaves an existing pin alone: a rotated key is a decision, not an event.</summary>
    [Fact]
    public void Rescanning_a_pinned_server_keeps_its_pin()
    {
        var server = RemoteServerTestValues.Ready();

        server.OfferHostKeys([("ssh-ed25519", "AAAArotated", RemoteServerTestValues.FingerprintB)]);

        Assert.Equal(RemoteServerTestValues.FingerprintA, server.HostKeyFingerprint);
        Assert.Equal(RemoteServerState.Ready, server.State);
    }

    /// <summary>Confirming the key that is already trusted changes no observation, and a different key drops them all.</summary>
    [Fact]
    public void Repinning_the_same_key_keeps_the_observations_and_a_different_key_drops_them()
    {
        var same = RemoteServerTestValues.Ready();
        same.OfferHostKeys([("ssh-ed25519", "AAAAed", RemoteServerTestValues.FingerprintA)]);
        Assert.True(same.PinHostKey(RemoteServerTestValues.FingerprintA, RemoteServerTestValues.LaterAt));
        Assert.Equal(RemoteServerState.Ready, same.State);
        Assert.Equal("Debian GNU/Linux 12", same.SystemRelease);

        var other = RemoteServerTestValues.Ready();
        other.OfferHostKeys([("ssh-rsa", "AAAArsa", RemoteServerTestValues.FingerprintB)]);
        Assert.True(other.PinHostKey(RemoteServerTestValues.FingerprintB, RemoteServerTestValues.LaterAt));
        Assert.Equal(RemoteServerState.Pinned, other.State);
        Assert.Null(other.SystemRelease);
        Assert.Null(other.IsRoot);
        Assert.Null(other.LastFailure);
    }

    /// <summary>Confirming a key after the server presented another one clears the failure and returns to pinned.</summary>
    [Fact]
    public void Confirming_a_key_after_a_host_key_change_clears_the_failure()
    {
        var server = RemoteServerTestValues.Ready();
        Assert.True(server.RecordTestFailed(RemoteFailureKind.HostKeyChanged, RemoteServerTestValues.LaterAt).IsSuccess);
        Assert.Equal(RemoteServerState.HostKeyChanged, server.State);

        server.OfferHostKeys([("ssh-ed25519", "AAAAed", RemoteServerTestValues.FingerprintA)]);
        Assert.True(server.PinHostKey(RemoteServerTestValues.FingerprintA, RemoteServerTestValues.LaterAt));

        Assert.Equal(RemoteServerState.Pinned, server.State);
        Assert.Null(server.LastFailure);
    }

    /// <summary>A change of host, port or login clears the pin, the candidates and every observation.</summary>
    [Theory]
    [InlineData("new.example.net", 22, "maranmig")]
    [InlineData("old.example.net", 2222, "maranmig")]
    [InlineData("old.example.net", 22, "someoneelse")]
    public void Retargeting_clears_the_pin_the_candidates_and_the_observations(string host, int port, string user)
    {
        var server = RemoteServerTestValues.Ready();
        Assert.True(server.RecordDiscovery(3, RemoteServerTestValues.LaterAt).IsSuccess);

        var changed = server.Retarget("renamed", host, port, user, null);

        Assert.True(changed);
        Assert.Equal("renamed", server.Name);
        Assert.Equal(host, server.Host);
        Assert.Equal(port, server.Port);
        Assert.Equal(user, server.SshUser);
        Assert.Equal(RemoteServerState.HostKeyUnconfirmed, server.State);
        Assert.False(server.IsHostKeyPinned);
        Assert.Null(server.HostKey);
        Assert.Null(server.HostKeyFingerprint);
        Assert.Null(server.HostKeyConfirmedAt);
        Assert.Empty(server.HostKeyCandidates);
        Assert.Null(server.LastTestedAt);
        Assert.Null(server.Kind);
        Assert.Null(server.SystemRelease);
        Assert.Null(server.DiscoveredAt);
        Assert.Null(server.AccountsFound);
    }

    /// <summary>Control: a new label or a new key restriction alone keeps the pin and everything observed.</summary>
    [Fact]
    public void Renaming_or_restricting_the_key_alone_keeps_the_pin_and_the_observations()
    {
        var server = RemoteServerTestValues.Ready();

        var changed = server.Retarget("renamed", "old.example.net", 22, "maranmig", "203.0.113.7");

        Assert.False(changed);
        Assert.Equal("renamed", server.Name);
        Assert.Equal("203.0.113.7", server.AllowedFrom);
        Assert.Equal(RemoteServerState.Ready, server.State);
        Assert.Equal(RemoteServerTestValues.FingerprintA, server.HostKeyFingerprint);
        Assert.Equal(2, server.HostKeyCandidates.Count);
        Assert.Equal("Debian GNU/Linux 12", server.SystemRelease);
    }

    /// <summary>A retarget with a blank or oversized field is a caller bug and changes nothing.</summary>
    [Fact]
    public void Retargeting_to_an_invalid_target_is_refused_and_changes_nothing()
    {
        var server = RemoteServerTestValues.Ready();

        Assert.ThrowsAny<ArgumentException>(() => { return server.Retarget(" ", "x.example.net", 22, "u", null); });
        Assert.ThrowsAny<ArgumentException>(() => { return server.Retarget("n", "x.example.net", 70000, "u", null); });

        Assert.Equal("old-host", server.Name);
        Assert.Equal("old.example.net", server.Host);
        Assert.True(server.IsHostKeyPinned);
    }

    /// <summary>A successful test records what it found and makes the server ready.</summary>
    [Fact]
    public void A_successful_test_records_what_it_found_and_makes_the_server_ready()
    {
        var server = RemoteServerTestValues.Pinned();
        var snapshot = RemoteServerTestValues.Snapshot();

        var result = server.RecordTestSucceeded(snapshot, RemoteServerTestValues.LaterAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(RemoteServerState.Ready, server.State);
        Assert.Equal(RemoteServerTestValues.LaterAt, server.LastTestedAt);
        Assert.Null(server.LastFailure);
        Assert.Equal(snapshot.IsRoot, server.IsRoot);
        Assert.Equal(snapshot.HomeFreeBytes, server.HomeFreeBytes);
        Assert.Equal(snapshot.HomeTotalBytes, server.HomeTotalBytes);
        Assert.Equal(snapshot.SystemRelease, server.SystemRelease);
        Assert.Equal(RemoteSourceKind.GenericLinux, server.Kind);
        Assert.Equal("MariaDb", server.DatabaseServer);
        Assert.Equal("Nginx", server.WebServer);
        Assert.True(server.MailFound);
    }

    /// <summary>Text the source reported is shortened to its column and never refused.</summary>
    [Fact]
    public void Oversized_text_from_the_source_is_shortened_not_refused()
    {
        var server = RemoteServerTestValues.Pinned();
        var snapshot = RemoteServerTestValues.Snapshot() with { SystemRelease = new string('x', 500) };

        Assert.True(server.RecordTestSucceeded(snapshot, RemoteServerTestValues.LaterAt).IsSuccess);

        Assert.Equal(RemoteServer.SystemReleaseMaxLength, server.SystemRelease!.Length);
    }

    /// <summary>Nothing can have been tested without a pin, so a result recorded without one is refused.</summary>
    [Fact]
    public void A_test_result_without_a_pinned_key_is_refused_and_changes_nothing()
    {
        var server = RemoteServerTestValues.Scanned();

        var succeeded = server.RecordTestSucceeded(RemoteServerTestValues.Snapshot(), RemoteServerTestValues.LaterAt);
        var failed = server.RecordTestFailed(RemoteFailureKind.Unreachable, RemoteServerTestValues.LaterAt);

        AssertRefused(succeeded, MigrationErrorCodes.RemoteServerHostKeyUnconfirmed);
        AssertRefused(failed, MigrationErrorCodes.RemoteServerHostKeyUnconfirmed);
        Assert.Equal(RemoteServerState.HostKeyUnconfirmed, server.State);
        Assert.Null(server.LastTestedAt);
        Assert.Null(server.LastFailure);
    }

    /// <summary>Each way a test fails puts the server in the state that names it, and an address refusal reads as unreachable.</summary>
    [Theory]
    [InlineData(RemoteFailureKind.HostKeyChanged, RemoteServerState.HostKeyChanged)]
    [InlineData(RemoteFailureKind.AuthRefused, RemoteServerState.AuthRefused)]
    [InlineData(RemoteFailureKind.Unreachable, RemoteServerState.Unreachable)]
    [InlineData(RemoteFailureKind.AddressRefused, RemoteServerState.Unreachable)]
    public void A_failed_test_puts_the_server_in_the_state_that_names_it_and_drops_the_observations(
        RemoteFailureKind failure,
        RemoteServerState expected)
    {
        var server = RemoteServerTestValues.Ready();

        var result = server.RecordTestFailed(failure, RemoteServerTestValues.LaterAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, server.State);
        Assert.Equal(failure, server.LastFailure);
        Assert.Equal(RemoteServerTestValues.LaterAt, server.LastTestedAt);
        Assert.Null(server.Kind);
        Assert.Null(server.SystemRelease);
        Assert.Null(server.IsRoot);
        Assert.Null(server.MailFound);
        Assert.True(server.IsHostKeyPinned);
    }

    /// <summary>A failure kind this entity has no state for is refused rather than coerced.</summary>
    [Fact]
    public void An_unknown_failure_kind_is_refused()
    {
        var server = RemoteServerTestValues.Pinned();

        Assert.Throws<ArgumentOutOfRangeException>(() => { return server.RecordTestFailed((RemoteFailureKind)99, RemoteServerTestValues.LaterAt); });
    }

    /// <summary>A success after a failure clears the failure.</summary>
    [Fact]
    public void A_success_after_a_failure_clears_the_failure()
    {
        var server = RemoteServerTestValues.Pinned();
        Assert.True(server.RecordTestFailed(RemoteFailureKind.AuthRefused, RemoteServerTestValues.LaterAt).IsSuccess);

        Assert.True(server.RecordTestSucceeded(RemoteServerTestValues.Snapshot(), RemoteServerTestValues.LaterAt).IsSuccess);

        Assert.Null(server.LastFailure);
        Assert.Equal(RemoteServerState.Ready, server.State);
    }

    /// <summary>A discovery is recorded only for a pinned, untroubled server.</summary>
    [Fact]
    public void A_discovery_is_recorded_for_a_pinned_or_ready_server()
    {
        var pinned = RemoteServerTestValues.Pinned();
        var ready = RemoteServerTestValues.Ready();

        Assert.True(pinned.RecordDiscovery(0, RemoteServerTestValues.LaterAt).IsSuccess);
        Assert.True(ready.RecordDiscovery(12, RemoteServerTestValues.LaterAt).IsSuccess);

        Assert.Equal(0, pinned.AccountsFound);
        Assert.Equal(12, ready.AccountsFound);
        Assert.Equal(RemoteServerTestValues.LaterAt, ready.DiscoveredAt);
    }

    /// <summary>A discovery is refused while the key is unconfirmed or the last attempt ended in a refusal.</summary>
    [Fact]
    public void A_discovery_is_refused_for_a_server_that_is_not_pinned_or_is_in_trouble()
    {
        var unconfirmed = RemoteServerTestValues.Scanned();
        var refused = RemoteServerTestValues.Ready();
        Assert.True(refused.RecordTestFailed(RemoteFailureKind.AuthRefused, RemoteServerTestValues.LaterAt).IsSuccess);

        AssertRefused(unconfirmed.RecordDiscovery(1, RemoteServerTestValues.LaterAt), MigrationErrorCodes.RemoteServerNotReady);
        AssertRefused(refused.RecordDiscovery(1, RemoteServerTestValues.LaterAt), MigrationErrorCodes.RemoteServerNotReady);
        Assert.Null(unconfirmed.AccountsFound);
        Assert.Null(refused.DiscoveredAt);
    }

    /// <summary>A negative account count is a caller bug.</summary>
    [Fact]
    public void A_negative_account_count_is_refused()
    {
        var server = RemoteServerTestValues.Ready();

        Assert.Throws<ArgumentOutOfRangeException>(() => { return server.RecordDiscovery(-1, RemoteServerTestValues.LaterAt); });
    }

    /// <summary>The entity exposes no public setter: every change of state is a method.</summary>
    [Fact]
    public void No_property_of_the_entity_has_a_public_setter()
    {
        foreach (var type in new[] { typeof(RemoteServer), typeof(RemoteServerHostKeyCandidate), typeof(DiscoveredAccount), typeof(DiscoveredSite), typeof(DiscoveredDatabase), typeof(DiscoveryWarning) })
        {
            var open = type.GetProperties().Where(property => { return property.SetMethod?.IsPublic == true; }).ToList();
            Assert.Empty(open);
        }
    }

    /// <summary>Builds a server, each argument defaulting to a valid one.</summary>
    /// <returns>The server.</returns>
    private static RemoteServer Build(
        Guid? id = null,
        string name = "old-host",
        string host = "old.example.net",
        int port = 22,
        string sshUser = "maranmig",
        string? allowedFrom = null,
        string publicKey = "ssh-ed25519 AAAA",
        string privateKey = "key")
    {
        return new RemoteServer(id ?? Guid.NewGuid(), name, host, port, sshUser, allowedFrom, publicKey, privateKey, RemoteServerTestValues.CreatedAt);
    }

    /// <summary>Asserts a transition was refused with a conflict carrying the code.</summary>
    /// <param name="result">The transition's result.</param>
    /// <param name="code">The expected code.</param>
    private static void AssertRefused(Result result, string code)
    {
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }
}
