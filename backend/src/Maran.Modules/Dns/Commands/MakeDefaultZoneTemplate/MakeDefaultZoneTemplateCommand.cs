namespace Maran.Modules.Dns.Commands.MakeDefaultZoneTemplate;

/// <summary>
/// Chooses the zone template a zone created for a new site is made from. Administrator only; choosing the
/// template that already is the default changes nothing.
/// </summary>
/// <param name="TemplateId">Which template to make the default, from the route.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record MakeDefaultZoneTemplateCommand(Guid TemplateId, string IpAddress, string UserAgent);
