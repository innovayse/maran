using Maran.Sdk.Streaming;

namespace Maran.Sdk.Tests.Streaming;

/// <summary>
/// Behavioral contract of <see cref="EventStreamFrame"/>: the event's name, its JSON data line and
/// the blank line that ends it, with the payload spelled as the panel's responses spell it.
/// </summary>
public sealed class EventStreamFrameTests
{
    /// <summary>A null member is written as null, exactly as a response body writes it.</summary>
    [Fact]
    public void A_null_member_is_written_as_null()
    {
        var frame = EventStreamFrame.Render("task", new Payload("alpha", null, DayOfWeek.Friday));

        Assert.Equal("event: task\ndata: {\"name\":\"alpha\",\"note\":null,\"day\":\"friday\"}\n\n", frame);
    }

    /// <summary>A value holding newlines cannot close the frame early and forge an event of its own.</summary>
    [Fact]
    public void A_value_holding_newlines_cannot_forge_an_event()
    {
        var frame = EventStreamFrame.Render("task", new Payload("a\n\nevent: end", null, DayOfWeek.Friday));

        Assert.Single(frame.Split("\n\n", StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>The payload the tests render.</summary>
    /// <param name="Name">A plain text member.</param>
    /// <param name="Note">A member that is null.</param>
    /// <param name="Day">An enum member.</param>
    private sealed record Payload(string Name, string? Note, DayOfWeek Day);
}
