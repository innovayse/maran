using Maran.Modules.Notifications.Domain.Enums;

namespace Maran.Modules.Notifications.Common;

/// <summary>One way of protecting the mail server connection, as the settings form's picker offers it.</summary>
/// <param name="Security">
/// The mode itself: the enum, so the panel's one camelCase enum converter spells it exactly as the
/// settings read-back and the save request do, and a client can send back what it was given.
/// </param>
/// <param name="SecurityName">The mode as an administrator reads it, resolved for the request's culture.</param>
public sealed record SmtpSecurityChoiceDto(SmtpSecurity Security, string SecurityName);
