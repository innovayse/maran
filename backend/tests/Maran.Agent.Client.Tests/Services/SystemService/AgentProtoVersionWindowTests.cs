using Maran.Agent.Client.Services.SystemService;

namespace Maran.Agent.Client.Tests.Services.SystemService;

/// <summary>
/// The window of contract revisions this panel drives: which <c>proto_version</c> values the
/// handshake admits, and that the revision every unversioned agent reported is not among them.
/// </summary>
public sealed class AgentProtoVersionWindowTests
{
    /// <summary>
    /// Only the revisions between the two ends, ends included, are driven. While the window is one
    /// revision wide the two ends are the same number, so the upper end is the one row below it.
    /// </summary>
    /// <param name="revision">The revision an agent reports.</param>
    /// <param name="expected">Whether the window contains it.</param>
    [Theory]
    [InlineData(AgentProtoVersionWindow.Minimum - 1, false)]
    [InlineData(AgentProtoVersionWindow.Minimum, true)]
    [InlineData(AgentProtoVersionWindow.Maximum + 1, false)]
    [InlineData(0u, false)]
    public void Only_revisions_inside_the_window_are_driven(uint revision, bool expected)
    {
        Assert.Equal(expected, AgentProtoVersionWindow.Contains(revision));
    }

    /// <summary>Revision 1, which every agent built before the revision policy reported, is outside the window.</summary>
    [Fact]
    public void The_window_excludes_the_revision_every_unversioned_agent_reported()
    {
        Assert.False(AgentProtoVersionWindow.Contains(1));
    }

    /// <summary>A refusal is classified by the error code the window names.</summary>
    [Fact]
    public void The_incompatible_code_is_the_localized_message_key()
    {
        Assert.Equal("AgentIncompatible", AgentProtoVersionWindow.IncompatibleCode);
    }
}
