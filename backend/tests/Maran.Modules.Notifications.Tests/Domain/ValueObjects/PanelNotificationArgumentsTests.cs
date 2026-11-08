using Maran.Modules.Notifications.Domain.ValueObjects;

namespace Maran.Modules.Notifications.Tests.Domain.ValueObjects;

/// <summary>The few words a notification keeps: bounded, free of control characters, and exact on the way back.</summary>
public sealed class PanelNotificationArgumentsTests
{
    /// <summary>Values written and read back are the values that went in.</summary>
    [Fact]
    public void Values_survive_a_round_trip_through_the_stored_form()
    {
        var arguments = PanelNotificationArguments.From(["a", "", "c"]);

        var restored = PanelNotificationArguments.Parse(arguments.Serialize());

        Assert.Equal(["a", "", "c"], restored.Values);
    }

    /// <summary>A control character, the separator included, cannot reach the stored form.</summary>
    [Fact]
    public void Control_characters_including_the_separator_are_removed()
    {
        var arguments = PanelNotificationArguments.From(["a\u001fb\nc"]);

        Assert.Equal(["abc"], arguments.Values);
        Assert.DoesNotContain('\n', arguments.Serialize());
    }

    /// <summary>At most four values are kept, each cut to its bound.</summary>
    [Fact]
    public void Values_are_bounded_in_number_and_length()
    {
        var long200 = new string('x', PanelNotificationArguments.MaxValueLength + 50);

        var arguments = PanelNotificationArguments.From([long200, "2", "3", "4", "5"]);

        Assert.Equal(PanelNotificationArguments.MaxCount, arguments.Values.Count);
        Assert.Equal(PanelNotificationArguments.MaxValueLength, arguments.Values[0].Length);
        Assert.True(arguments.Serialize().Length <= PanelNotificationArguments.StoredMaxLength);
    }

    /// <summary>No values, and a missing list, are the same empty state, and it stores as an empty string.</summary>
    [Fact]
    public void No_values_store_as_an_empty_string_and_read_back_as_none()
    {
        Assert.Empty(PanelNotificationArguments.From(null).Values);
        Assert.Equal(string.Empty, PanelNotificationArguments.From([" "]).Serialize());
        Assert.Empty(PanelNotificationArguments.Parse(string.Empty).Values);
    }
}
