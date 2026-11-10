using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;
using Maran.Modules.ServerMigration.Domain.ValueObjects;

namespace Maran.Modules.ServerMigration.Tests.TestSupport;

/// <summary>Builds the servers, keys and test results the entity tests need, in each state a server can be in.</summary>
public static class RemoteServerTestValues
{
    /// <summary>The instant every server here is created at.</summary>
    public static readonly DateTimeOffset CreatedAt = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The instant a pin or a test here happens at.</summary>
    public static readonly DateTimeOffset LaterAt = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    /// <summary>A planted private key marker, so a test can hunt for it in text that must not hold it.</summary>
    public const string PrivateKeySentinel = "PRIVATE-KEY-SENTINEL-7f3a";

    /// <summary>A fingerprint of the right shape.</summary>
    public const string FingerprintA = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    /// <summary>A second fingerprint of the right shape.</summary>
    public const string FingerprintB = "SHA256:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    /// <summary>A server as registered: unconfirmed, no candidates yet.</summary>
    /// <returns>The server.</returns>
    public static RemoteServer Registered()
    {
        return new RemoteServer(
            Guid.NewGuid(),
            "old-host",
            "old.example.net",
            22,
            "maranmig",
            null,
            "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIexample",
            PrivateKeySentinel,
            CreatedAt);
    }

    /// <summary>A server that was scanned: two host keys on offer, none pinned.</summary>
    /// <returns>The server.</returns>
    public static RemoteServer Scanned()
    {
        var server = Registered();
        server.OfferHostKeys(
        [
            ("ssh-ed25519", "AAAAed", FingerprintA),
            ("ssh-rsa", "AAAArsa", FingerprintB),
        ]);
        return server;
    }

    /// <summary>A server whose first host key is pinned.</summary>
    /// <returns>The server, <see cref="RemoteServerState.Pinned"/>.</returns>
    public static RemoteServer Pinned()
    {
        var server = Scanned();
        Assert.True(server.PinHostKey(FingerprintA, LaterAt));
        return server;
    }

    /// <summary>A server whose last test succeeded.</summary>
    /// <returns>The server, <see cref="RemoteServerState.Ready"/>.</returns>
    public static RemoteServer Ready()
    {
        var server = Pinned();
        Assert.True(server.RecordTestSucceeded(Snapshot(), LaterAt).IsSuccess);
        return server;
    }

    /// <summary>What a successful test might establish.</summary>
    /// <returns>The snapshot.</returns>
    public static TestedSourceSnapshot Snapshot()
    {
        return new TestedSourceSnapshot(
            IsRoot: false,
            HomeFreeBytes: 1_000,
            HomeTotalBytes: 5_000,
            SystemRelease: "Debian GNU/Linux 12",
            Kind: RemoteSourceKind.GenericLinux,
            MaranVersion: null,
            DatabaseServer: "MariaDb",
            WebServer: "Nginx",
            MailFound: true);
    }
}
