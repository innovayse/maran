namespace Maran.Modules.Notifications.Queries.GetSmtpSettings;

/// <summary>Reads the panel's outgoing mail settings, without the password.</summary>
/// <remarks>
/// Takes no parameters: there is at most one row of mail settings on a panel (R12), so there is
/// nothing to identify.
/// </remarks>
public sealed record GetSmtpSettingsQuery;
