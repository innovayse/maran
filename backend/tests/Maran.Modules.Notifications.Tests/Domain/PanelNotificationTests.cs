using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.ValueObjects;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Notifications.Tests.Domain;

/// <summary>What a notification accepts: bounded text, and a deep link only from the closed set.</summary>
public sealed class PanelNotificationTests
{
    /// <summary>Builds a notification with the given subject and link.</summary>
    /// <param name="subject">The subject.</param>
    /// <param name="deepLink">The deep link.</param>
    /// <returns>The notification.</returns>
    private static PanelNotification Build(string subject, string? deepLink)
    {
        return new PanelNotification(
            Guid.NewGuid(),
            NotificationSource.Task,
            "SiteNotFound",
            NotificationSeverity.Warning,
            subject,
            PanelNotificationArguments.None,
            deepLink,
            DateTimeOffset.UnixEpoch);
    }

    /// <summary>A link from the closed set is kept.</summary>
    [Fact]
    public void A_known_deep_link_is_kept()
    {
        Assert.Equal(NotificationDeepLinks.Tasks, Build("x", NotificationDeepLinks.Tasks).DeepLink);
    }

    /// <summary>A URL, or any name outside the closed set, is stored as no link.</summary>
    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("Tasks")]
    [InlineData("")]
    public void An_unknown_deep_link_is_stored_as_none(string link)
    {
        Assert.Null(Build("x", link).DeepLink);
    }

    /// <summary>The subject loses control characters and is cut to its column.</summary>
    [Fact]
    public void The_subject_is_cleaned_and_bounded()
    {
        var notification = Build("a\nb" + new string('z', 500), null);

        Assert.DoesNotContain('\n', notification.Subject);
        Assert.Equal(PanelNotification.SubjectMaxLength, notification.Subject.Length);
    }
}
