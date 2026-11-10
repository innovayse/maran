using System.Net;

namespace Maran.Host.IntegrationTests.Marketplace;

/// <summary>
/// One route of the marketplace controller as the authorization tests call it, and the answer an
/// administrator gets on a panel where the marketplace is still switched off.
/// </summary>
/// <param name="Verb">The HTTP verb.</param>
/// <param name="Template">The route template as the controller declares it, after the controller prefix.</param>
/// <param name="Path">The template with its parameters filled in, after the controller prefix.</param>
/// <param name="Body">The JSON body the call carries, or null for none.</param>
/// <param name="AdministratorStatus">The status an administrator gets with the switch off.</param>
/// <param name="AdministratorCode">
/// The problem code an administrator gets, or null when the call succeeds. A code is the module's own
/// answer: it proves the request got past authorization and was judged by the marketplace.
/// </param>
public sealed record MarketplaceRoute(
    string Verb,
    string Template,
    string Path,
    object? Body,
    HttpStatusCode AdministratorStatus,
    string? AdministratorCode)
{
    /// <summary>The route as the controller's attributes spell it: the verb and the template.</summary>
    public string Key
    {
        get
        {
            return Verb + " " + Template;
        }
    }

    /// <summary>The full path of the call.</summary>
    public string FullPath
    {
        get
        {
            return MarketplaceWorld.ApiRoot + Path;
        }
    }

    /// <summary>The HTTP method of the call.</summary>
    public HttpMethod Method
    {
        get
        {
            return new HttpMethod(Verb);
        }
    }
}
