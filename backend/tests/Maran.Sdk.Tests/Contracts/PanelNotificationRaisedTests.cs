using Maran.Sdk.Contracts;

namespace Maran.Sdk.Tests.Contracts;

/// <summary>The cleaning and capping <see cref="PanelNotificationRaised"/> applies before any consumer sees it.</summary>
public sealed class PanelNotificationRaisedTests
{
    /// <summary>Builds a message around the given text.</summary>
    /// <param name="subject">The subject.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The message.</returns>
    private static PanelNotificationRaised Build(string subject, params string[] arguments)
    {
        return new PanelNotificationRaised(
            NotificationSource.Alert,
            "DiskUsage",
            NotificationSeverity.Critical,
            subject,
            arguments,
            NotificationDeepLinks.Monitoring,
            DateTimeOffset.UnixEpoch);
    }

    /// <summary>Control characters are stripped from the subject and every argument.</summary>
    [Fact]
    public void Control_characters_are_stripped_from_text()
    {
        var message = Build("sub\u001fject", "a\u0000b");

        Assert.Equal("subject", message.Subject);
        Assert.Equal("ab", Assert.Single(message.Arguments));
    }

    /// <summary>Text longer than the cap is cut, and a value at the cap is kept (the accepting control).</summary>
    [Fact]
    public void Long_text_is_capped_and_text_at_the_cap_is_kept()
    {
        var atCap = new string('x', PanelNotificationRaised.MaxTextLength);

        Assert.Equal(atCap, Build(atCap).Subject);
        Assert.Equal(atCap, Build(atCap + "y").Subject);
        Assert.Equal(atCap, Assert.Single(Build("s", atCap + "y").Arguments));
    }

    /// <summary>At most four arguments are kept, in order.</summary>
    [Fact]
    public void At_most_four_arguments_are_kept_in_order()
    {
        var message = Build("s", "1", "2", "3", "4", "5");

        Assert.Equal(["1", "2", "3", "4"], message.Arguments);
    }
}
