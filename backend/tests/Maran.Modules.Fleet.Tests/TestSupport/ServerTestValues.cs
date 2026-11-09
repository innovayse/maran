using Maran.Modules.Fleet.Domain.Entities;
using Maran.Modules.Fleet.Domain.ValueObjects;

namespace Maran.Modules.Fleet.Tests.TestSupport;

/// <summary>Builds the servers and fingerprints the entity tests need, in each state a node can be in.</summary>
public static class ServerTestValues
{
    /// <summary>The instant every server here is created at.</summary>
    public static readonly DateTimeOffset CreatedAt = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The instant an approval here happens at.</summary>
    public static readonly DateTimeOffset ApprovedAt = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    /// <summary>A fingerprint of 64 lowercase hexadecimal digits.</summary>
    public static CertificateFingerprint Presented { get; } = Parse(new string('a', 64));

    /// <summary>A different, equally well-formed fingerprint.</summary>
    public static CertificateFingerprint Other { get; } = Parse(new string('b', 64));

    /// <summary>Parses a fingerprint the test knows is well formed.</summary>
    /// <param name="text">64 hexadecimal digits.</param>
    /// <returns>The fingerprint.</returns>
    public static CertificateFingerprint Parse(string text)
    {
        Assert.True(CertificateFingerprint.TryParse(text, out var fingerprint));
        return fingerprint!;
    }

    /// <summary>A node as an administrator declares it: pending, nothing presented yet.</summary>
    /// <returns>The node.</returns>
    public static Server PendingNode()
    {
        return Server.Declare(Guid.NewGuid(), "web-2", "web-2.example.net", 8444, CreatedAt);
    }

    /// <summary>A node a host has enrolled for, still waiting for the administrator.</summary>
    /// <returns>The node, pending, with <see cref="Presented"/> recorded.</returns>
    public static Server EnrolledNode()
    {
        var node = PendingNode();
        Assert.True(node.RecordEnrolment(Presented).IsSuccess);
        return node;
    }

    /// <summary>A node the administrator approved.</summary>
    /// <returns>The node, approved and not yet observed.</returns>
    public static Server ApprovedNode()
    {
        var node = EnrolledNode();
        Assert.True(node.Approve(Presented, ApprovedAt).IsSuccess);
        return node;
    }

    /// <summary>A node the administrator approved and then switched off.</summary>
    /// <returns>The node, disabled.</returns>
    public static Server DisabledNode()
    {
        var node = ApprovedNode();
        Assert.True(node.Disable().IsSuccess);
        return node;
    }
}
