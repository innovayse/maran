namespace Maran.Agent.Client.Services.SitesService;

/// <summary>What creating a site produced on the server.</summary>
/// <param name="DocumentRoot">
/// The absolute document root the agent allocated. Panel-facing: it is stored against the site and
/// shown to operators, not rendered into a customer-facing failure message.
/// </param>
public sealed record CreatedSiteDto(string DocumentRoot);
