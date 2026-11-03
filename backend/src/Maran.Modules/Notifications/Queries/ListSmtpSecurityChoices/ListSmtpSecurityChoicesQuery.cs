namespace Maran.Modules.Notifications.Queries.ListSmtpSecurityChoices;

/// <summary>
/// Lists the ways the panel can protect its connection to the mail server, for the settings form's
/// picker: reference data the form selects from, produced by the panel rather than worded by the SPA
/// (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
/// <remarks>
/// The whole closed set, in the order the enum declares it. The save command still validates the
/// value it is given against the same enum, so this list is advice and the endpoint is the boundary.
/// </remarks>
public sealed record ListSmtpSecurityChoicesQuery;
