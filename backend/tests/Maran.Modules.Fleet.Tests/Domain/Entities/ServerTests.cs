using Maran.Modules.Fleet.Domain.Entities;
using Maran.Modules.Fleet.Domain.Enums;
using Maran.Modules.Fleet.Tests.TestSupport;
using Maran.SharedKernel.Constants;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Fleet.Tests.Domain.Entities;

/// <summary>
/// What a <see cref="Server"/> allows itself to become. The state machine is the security property of
/// the registry: nothing is dialled before an administrator approved it, so the refusals are tested as
/// carefully as the transitions.
/// </summary>
public sealed class ServerTests
{
    /// <summary>The local server is the well-known one, online, with no address, port or fingerprint.</summary>
    [Fact]
    public void The_local_server_is_online_and_has_no_address_port_or_fingerprint()
    {
        var local = Server.CreateLocal("panel-host", ServerTestValues.CreatedAt);

        Assert.Equal(FleetServers.LocalId, local.Id);
        Assert.Equal(ServerKind.Local, local.Kind);
        Assert.Equal(ServerState.Online, local.State);
        Assert.Equal("panel-host", local.Name);
        Assert.Null(local.Address);
        Assert.Null(local.Port);
        Assert.Null(local.Fingerprint);
        Assert.Null(local.ApprovedAt);
        Assert.True(local.MayBeDialled);
    }

    /// <summary>A declared node is pending, carries where to dial it and nothing it has not presented.</summary>
    [Fact]
    public void A_declared_node_is_pending_and_is_not_dialled()
    {
        var id = Guid.NewGuid();

        var node = Server.Declare(id, "web-2", "web-2.example.net", 8444, ServerTestValues.CreatedAt);

        Assert.Equal(id, node.Id);
        Assert.Equal(ServerKind.Node, node.Kind);
        Assert.Equal(ServerState.Pending, node.State);
        Assert.Equal("web-2.example.net", node.Address);
        Assert.Equal(8444, node.Port);
        Assert.Null(node.Fingerprint);
        Assert.Null(node.ApprovedAt);
        Assert.False(node.MayBeDialled);
    }

    /// <summary>A node cannot take the local server's identity, nor an empty one.</summary>
    [Fact]
    public void A_node_cannot_be_given_the_local_identity_or_an_empty_one()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return Server.Declare(FleetServers.LocalId, "web-2", "web-2.example.net", 8444, ServerTestValues.CreatedAt);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            return Server.Declare(Guid.Empty, "web-2", "web-2.example.net", 8444, ServerTestValues.CreatedAt);
        });
    }

    /// <summary>A name that is blank or longer than a list cell holds is a caller bug and is refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_refused(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            return Server.Declare(Guid.NewGuid(), name, "web-2.example.net", 8444, ServerTestValues.CreatedAt);
        });
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            return Server.CreateLocal(name, ServerTestValues.CreatedAt);
        });
    }

    /// <summary>The name limit is inclusive: the longest name is accepted and one more is refused.</summary>
    [Fact]
    public void The_name_limit_accepts_the_longest_name_and_refuses_one_more()
    {
        var longest = new string('n', Server.NameMaxLength);

        var accepted = Server.Declare(Guid.NewGuid(), longest, "web-2.example.net", 8444, ServerTestValues.CreatedAt);

        Assert.Equal(longest, accepted.Name);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return Server.Declare(
                Guid.NewGuid(), longest + "n", "web-2.example.net", 8444, ServerTestValues.CreatedAt);
        });
    }

    /// <summary>An address that is blank or longer than a host name can be is refused.</summary>
    [Fact]
    public void A_blank_or_overlong_address_is_refused()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            return Server.Declare(Guid.NewGuid(), "web-2", " ", 8444, ServerTestValues.CreatedAt);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            return Server.Declare(
                Guid.NewGuid(),
                "web-2",
                new string('a', Server.AddressMaxLength + 1),
                8444,
                ServerTestValues.CreatedAt);
        });
    }

    /// <summary>The port range is inclusive at both ends and refuses the values beside it.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(65535, true)]
    [InlineData(65536, false)]
    [InlineData(-1, false)]
    public void The_port_range_is_inclusive_at_both_ends(int port, bool accepted)
    {
        if (accepted)
        {
            var node = Server.Declare(Guid.NewGuid(), "web-2", "web-2.example.net", port, ServerTestValues.CreatedAt);
            Assert.Equal(port, node.Port);
        }
        else
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                return Server.Declare(Guid.NewGuid(), "web-2", "web-2.example.net", port, ServerTestValues.CreatedAt);
            });
        }
    }

    /// <summary>Renaming replaces the name and nothing else.</summary>
    [Fact]
    public void Renaming_changes_the_name_and_nothing_else()
    {
        var node = ServerTestValues.ApprovedNode();
        var state = node.State;
        var fingerprint = node.Fingerprint;

        node.Rename("web-two");

        Assert.Equal("web-two", node.Name);
        Assert.Equal(state, node.State);
        Assert.Equal(fingerprint, node.Fingerprint);
        Assert.Throws<ArgumentException>(() =>
        {
            node.Rename(" ");
        });
    }

    /// <summary>A host's enrolment records the certificate it presented and leaves the node pending.</summary>
    [Fact]
    public void Enrolment_records_the_presented_fingerprint_and_leaves_the_node_pending()
    {
        var node = ServerTestValues.PendingNode();

        var result = node.RecordEnrolment(ServerTestValues.Presented);

        Assert.True(result.IsSuccess);
        Assert.Equal(ServerTestValues.Presented.Value, node.Fingerprint);
        Assert.Equal(ServerState.Pending, node.State);
        Assert.False(node.MayBeDialled);
    }

    /// <summary>A second enrolment of a still-pending node replaces the first; nothing was approved against it.</summary>
    [Fact]
    public void A_second_enrolment_of_a_pending_node_replaces_the_first()
    {
        var node = ServerTestValues.EnrolledNode();

        var result = node.RecordEnrolment(ServerTestValues.Other);

        Assert.True(result.IsSuccess);
        Assert.Equal(ServerTestValues.Other.Value, node.Fingerprint);
    }

    /// <summary>An approved or disabled node's pin is never replaced by a host that asks to enrol.</summary>
    [Fact]
    public void Enrolment_is_refused_once_the_node_is_approved_or_disabled_and_the_pin_is_kept()
    {
        foreach (var node in new[] { ServerTestValues.ApprovedNode(), ServerTestValues.DisabledNode() })
        {
            var result = node.RecordEnrolment(ServerTestValues.Other);

            AssertRefused(result, ErrorCodes.ServerStateTransitionInvalid, ErrorType.Conflict);
            Assert.Equal(ServerTestValues.Presented.Value, node.Fingerprint);
        }
    }

    /// <summary>The local server never enrols a certificate.</summary>
    [Fact]
    public void The_local_server_cannot_enrol()
    {
        var local = Server.CreateLocal("panel-host", ServerTestValues.CreatedAt);

        var result = local.RecordEnrolment(ServerTestValues.Presented);

        AssertRefused(result, ErrorCodes.ServerStateTransitionInvalid, ErrorType.Conflict);
        Assert.Null(local.Fingerprint);
    }

    /// <summary>Approving the fingerprint the host presented admits the node, unobserved, with the approval time.</summary>
    [Fact]
    public void Approving_the_presented_fingerprint_admits_the_node_and_stamps_the_time()
    {
        var node = ServerTestValues.EnrolledNode();

        var result = node.Approve(ServerTestValues.Presented, ServerTestValues.ApprovedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(ServerState.Unreachable, node.State);
        Assert.Equal(ServerTestValues.ApprovedAt, node.ApprovedAt);
        Assert.True(node.MayBeDialled);
    }

    /// <summary>The fingerprint is compared as a digest, not as text: an upper-case typing is the same fingerprint.</summary>
    [Fact]
    public void An_upper_case_typing_of_the_presented_fingerprint_is_accepted()
    {
        var node = ServerTestValues.EnrolledNode();
        var typed = ServerTestValues.Parse(new string('A', 64));

        var result = node.Approve(typed, ServerTestValues.ApprovedAt);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A fingerprint that is not the presented one is refused, and the node stays pending and undialled.</summary>
    [Fact]
    public void Approving_a_different_fingerprint_is_refused_and_the_node_stays_pending()
    {
        var node = ServerTestValues.EnrolledNode();

        var result = node.Approve(ServerTestValues.Other, ServerTestValues.ApprovedAt);

        AssertRefused(result, ErrorCodes.ServerFingerprintMismatch, ErrorType.Validation);
        Assert.Equal(ServerState.Pending, node.State);
        Assert.Null(node.ApprovedAt);
        Assert.False(node.MayBeDialled);
    }

    /// <summary>A node no host has enrolled for has nothing to approve, whatever the administrator types.</summary>
    [Fact]
    public void Approving_a_node_no_host_has_enrolled_for_is_refused()
    {
        var node = ServerTestValues.PendingNode();

        var result = node.Approve(ServerTestValues.Presented, ServerTestValues.ApprovedAt);

        AssertRefused(result, ErrorCodes.ServerNotEnrolled, ErrorType.Conflict);
        Assert.Equal(ServerState.Pending, node.State);
        Assert.False(node.MayBeDialled);
    }

    /// <summary>Only a pending node is approved: an approved, a disabled and the local server refuse.</summary>
    [Fact]
    public void Approval_is_refused_for_every_server_that_is_not_a_pending_node()
    {
        var candidates = new[]
        {
            ServerTestValues.ApprovedNode(),
            ServerTestValues.DisabledNode(),
            Server.CreateLocal("panel-host", ServerTestValues.CreatedAt),
        };

        foreach (var server in candidates)
        {
            var before = server.State;

            var result = server.Approve(ServerTestValues.Presented, ServerTestValues.ApprovedAt);

            AssertRefused(result, ErrorCodes.ServerStateTransitionInvalid, ErrorType.Conflict);
            Assert.Equal(before, server.State);
        }
    }

    /// <summary>Disabling an approved node keeps its pin and stops it being dialled.</summary>
    [Fact]
    public void Disabling_an_approved_node_keeps_its_pin_and_stops_dialling()
    {
        var node = ServerTestValues.ApprovedNode();

        var result = node.Disable();

        Assert.True(result.IsSuccess);
        Assert.Equal(ServerState.Disabled, node.State);
        Assert.False(node.MayBeDialled);
        Assert.Equal(ServerTestValues.Presented.Value, node.Fingerprint);
    }

    /// <summary>A pending node is removed, not disabled, and a disabled one cannot be disabled twice.</summary>
    [Fact]
    public void Disabling_is_refused_for_a_pending_node_and_for_a_disabled_one()
    {
        AssertRefused(
            ServerTestValues.PendingNode().Disable(),
            ErrorCodes.ServerStateTransitionInvalid,
            ErrorType.Conflict);
        AssertRefused(
            ServerTestValues.DisabledNode().Disable(),
            ErrorCodes.ServerStateTransitionInvalid,
            ErrorType.Conflict);
    }

    /// <summary>The server the panel runs on cannot be switched off, and says so with its own code.</summary>
    [Fact]
    public void The_local_server_cannot_be_disabled()
    {
        var local = Server.CreateLocal("panel-host", ServerTestValues.CreatedAt);

        var result = local.Disable();

        AssertRefused(result, ErrorCodes.ServerLocalImmutable, ErrorType.Conflict);
        Assert.Equal(ServerState.Online, local.State);
        Assert.True(local.MayBeDialled);
    }

    /// <summary>Enabling a disabled node needs no second approval, and leaves it unobserved until a poll sees it.</summary>
    [Fact]
    public void Enabling_a_disabled_node_restores_dialling_without_a_second_approval()
    {
        var node = ServerTestValues.DisabledNode();

        var result = node.Enable();

        Assert.True(result.IsSuccess);
        Assert.Equal(ServerState.Unreachable, node.State);
        Assert.True(node.MayBeDialled);
        Assert.Equal(ServerTestValues.Presented.Value, node.Fingerprint);
    }

    /// <summary>Only a disabled node is enabled; a pending node must not be coerced into a dialled state.</summary>
    [Fact]
    public void Enabling_is_refused_for_every_server_that_is_not_disabled()
    {
        var candidates = new[]
        {
            ServerTestValues.PendingNode(),
            ServerTestValues.EnrolledNode(),
            ServerTestValues.ApprovedNode(),
            Server.CreateLocal("panel-host", ServerTestValues.CreatedAt),
        };

        foreach (var server in candidates)
        {
            var before = server.State;

            var result = server.Enable();

            AssertRefused(result, ErrorCodes.ServerStateTransitionInvalid, ErrorType.Conflict);
            Assert.Equal(before, server.State);
            Assert.Equal(before is not ServerState.Pending, server.MayBeDialled);
        }
    }

    /// <summary>
    /// Only the two states that are not dialled are pending and disabled, and the only road from pending to
    /// a dialled state is an approval of the presented fingerprint.
    /// </summary>
    [Fact]
    public void The_only_road_from_pending_to_a_dialled_state_is_an_approval_of_the_presented_fingerprint()
    {
        var node = ServerTestValues.PendingNode();

        Assert.False(node.Enable().IsSuccess);
        Assert.False(node.Disable().IsSuccess);
        Assert.False(node.Approve(ServerTestValues.Presented, ServerTestValues.ApprovedAt).IsSuccess);
        Assert.False(node.MayBeDialled);

        Assert.True(node.RecordEnrolment(ServerTestValues.Presented).IsSuccess);
        Assert.False(node.Approve(ServerTestValues.Other, ServerTestValues.ApprovedAt).IsSuccess);
        Assert.False(node.MayBeDialled);

        Assert.True(node.Approve(ServerTestValues.Presented, ServerTestValues.ApprovedAt).IsSuccess);
        Assert.True(node.MayBeDialled);
    }

    /// <summary>Asserts a result is the failure of the given code and kind.</summary>
    /// <param name="result">The result to judge.</param>
    /// <param name="code">The expected error code.</param>
    /// <param name="type">The expected error kind.</param>
    private static void AssertRefused(Result result, string code, ErrorType type)
    {
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(type, result.Error.Type);
    }
}
