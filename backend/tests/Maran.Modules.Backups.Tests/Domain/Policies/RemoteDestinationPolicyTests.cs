using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Domain.Policies;

namespace Maran.Modules.Backups.Tests.Domain.Policies;

/// <summary>
/// The panel's one statement of which destination kinds it can act on, held to answering both ways.
/// </summary>
/// <remarks>
/// A refusing gate needs an inverse control (rules/testing.md): a policy mutated to refuse
/// everything would satisfy any test that only ever hands it the kind it must turn away, and the
/// consequence of that mutation is a panel that cannot back anything up at all.
/// </remarks>
public sealed class RemoteDestinationPolicyTests
{
    /// <summary>A local destination is admitted, which is the accepting control.</summary>
    [Fact]
    public void A_local_destination_is_admitted()
    {
        Assert.True(RemoteDestinationPolicy.Admits(BackupDestinationKind.Local));
    }

    /// <summary>A remote destination is refused, which is what this build cannot act on.</summary>
    [Fact]
    public void A_remote_destination_is_refused()
    {
        Assert.False(RemoteDestinationPolicy.Admits(BackupDestinationKind.S3));
    }

    /// <summary>A kind that is not a declared member is refused rather than admitted by default.</summary>
    /// <remarks>
    /// The database and a cast can both produce one, and the allow-list shape is what makes the
    /// answer "no". Written against a value outside the enum on purpose: a new member added to
    /// <see cref="BackupDestinationKind"/> and forgotten here must fall on the refusing side.
    /// </remarks>
    [Fact]
    public void A_kind_that_is_not_a_declared_member_is_refused()
    {
        Assert.False(RemoteDestinationPolicy.Admits((BackupDestinationKind)97));
    }
}
