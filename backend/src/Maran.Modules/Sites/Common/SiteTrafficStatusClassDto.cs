namespace Maran.Modules.Sites.Common;

/// <summary>One class of HTTP status (2xx, 4xx and so on) and how many requests of a day it covers.</summary>
/// <param name="Label">The class as the customer reads it, localized for the request's culture.</param>
/// <param name="Requests">How many requests were answered with a status of this class.</param>
public sealed record SiteTrafficStatusClassDto(string Label, long Requests);
