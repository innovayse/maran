using Maran.Modules.Notifications.Domain.Enums;
using Maran.Modules.Notifications.Domain.ValueObjects;

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
/// <param name="Credentials">
/// Whether the server takes no credentials or a user name and password. The password is held in
/// memory in plain text, decrypted on load, because sending is impossible without it; it is never
/// logged, never journalled, and never returned by a query.
/// </param>
/// <param name="FromAddress">The address the panel's mail is sent from.</param>
/// <param name="FromName">The display name beside the sender address; may be empty.</param>
/// <param name="AlertRecipient">Where alert mail goes, or <c>null</c> when no operator address is set.</param>
public sealed record SmtpProfile(
    string Host,
    int Port,
    SmtpSecurity Security,
    SmtpCredentials Credentials,
    string FromAddress,
    string FromName,
    string? AlertRecipient);
