using Maran.Modules.Notifications.Domain.Enums;

namespace Maran.Modules.Notifications.Models;

/// <summary>
/// The panel's mail settings as the sender needs them — a plain snapshot, detached from the tracked
/// entity and from the database context that loaded it.
/// </summary>
/// <remarks>
/// It exists because <see cref="Services.SmtpSettingsCache"/> is a singleton and
/// <see cref="Domain.Entities.SmtpSettings"/> is an entity belonging to a scoped
/// <c>NotificationsDbContext</c>: caching the entity itself would keep a disposed context's change
/// tracker alive and hand every later caller an object whose lazy state belongs to a request that
/// finished hours ago.
/// </remarks>
/// <param name="Host">Host name or address of the mail server.</param>
/// <param name="Port">TCP port the mail server listens on.</param>
/// <param name="Security">How the connection is protected.</param>
/// <param name="Username">The submission user name, or empty when the server takes no credentials.</param>
/// <param name="Password">
/// The submission password in plain text, decrypted on load. It is held in memory because sending is
/// impossible without it; it is never logged, never journalled, and never returned by a query.
/// </param>
/// <param name="FromAddress">The address the panel's mail is sent from.</param>
/// <param name="FromName">The display name beside the sender address; may be empty.</param>
/// <param name="AlertRecipient">Where alert mail goes.</param>
public sealed record SmtpProfile(
    string Host,
    int Port,
    SmtpSecurity Security,
    string Username,
    string Password,
    string FromAddress,
    string FromName,
    string AlertRecipient);
