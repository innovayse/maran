namespace Maran.Modules.Dns.Commands.DeleteZoneTemplate;

/// <summary>
/// Deletes a zone template. A built-in template and the default template are refused. Administrator only.
/// Zones already created from the template are untouched: their records are the zones' own.
/// </summary>
/// <param name="TemplateId">Which template to delete, from the route.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record DeleteZoneTemplateCommand(Guid TemplateId, string IpAddress, string UserAgent);
