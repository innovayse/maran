using Maran.Modules.Notifications.Domain.Entities;
using Maran.Modules.Notifications.Domain.ValueObjects;
using Maran.Modules.Notifications.Resources;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Notifications.Services;

/// <summary>Words a stored notification for the current request's culture.</summary>
/// <remarks>
/// <para>
/// <b>Resolved when read, never stored.</b> The row keeps a code and a few values; the sentence is
/// chosen here for whoever is reading, so an administrator who switches language reads the same
/// entry in the new one.
/// </para>
/// <para>
/// <b>Which sentence.</b> An alert or a backup alert is worded by the resource key
/// <c>Feed&lt;Source&gt;&lt;Code&gt;&lt;Severity&gt;</c> (<c>FeedAlertDiskUsageCritical</c>): the severity
/// is part of the key because a raised alert and its resolution are different sentences and the row
/// carries the difference there and nowhere else. The sentence receives the subject as <c>{0}</c> and
/// the stored values as <c>{1}</c> to <c>{4}</c>. A code with no entry reads as the source's generic
/// sentence, never as the code. A task failure has one headline (<c>FeedTaskFailed</c>, the subject
/// as <c>{0}</c>) and its second line is the sentence of the failure code, resolved through the
/// panel's one <see cref="IErrorTextProvider"/> exactly as the tasks page does, with the generic
/// sentence for a code no loaded table describes.
/// </para>
/// <para>
/// For an alert or a backup alert the second line is the first stored value — the figure or the
/// service manager's words the producer attached — as plain text.
/// </para>
/// </remarks>
public sealed class NotificationTextResolver
{
    /// <summary>The feed's sentences in the panel's languages.</summary>
    private readonly IStringLocalizer<NotificationMessages> _text;

    /// <summary>The panel's one resolution from an error code to its sentence.</summary>
    private readonly IErrorTextProvider _errorTexts;

    /// <summary>Creates the resolver.</summary>
    /// <param name="text">The feed's sentences in the panel's languages.</param>
    /// <param name="errorTexts">The panel's error-message resolution, composed from every module's tables.</param>
    public NotificationTextResolver(IStringLocalizer<NotificationMessages> text, IErrorTextProvider errorTexts)
    {
        _text = text;
        _errorTexts = errorTexts;
    }

    /// <summary>Words one entry.</summary>
    /// <param name="notification">The stored entry.</param>
    /// <returns>The headline and the second line (or <c>null</c>).</returns>
    public (string Title, string? Detail) Resolve(PanelNotification notification)
    {
        var values = notification.Arguments.Values;
        var arguments = new object[1 + PanelNotificationArguments.MaxCount];
        arguments[0] = notification.Subject;
        for (var index = 0; index < PanelNotificationArguments.MaxCount; index++)
        {
            arguments[index + 1] = index < values.Count ? values[index] : string.Empty;
        }

        if (notification.Source == NotificationSource.Task)
        {
            var detail = _errorTexts.TryResolve(notification.Code, out var sentence)
                ? sentence
                : _text["FeedTaskFailureUnexplained"].Value;

            return (_text["FeedTaskFailed", arguments].Value, detail);
        }

        var specific = _text[$"Feed{notification.Source}{notification.Code}{notification.Severity}", arguments];
        var title = specific.ResourceNotFound
            ? _text[$"Feed{notification.Source}Generic", arguments].Value
            : specific.Value;

        return (title, values.Count > 0 && values[0].Length > 0 ? values[0] : null);
    }
}
