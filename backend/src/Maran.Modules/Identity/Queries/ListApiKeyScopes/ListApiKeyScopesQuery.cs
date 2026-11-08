using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Queries.ListApiKeyScopes;

/// <summary>
/// Lists the scopes an API key can be granted, for the create dialog's choice list: the reference data
/// the form selects from, produced by the panel rather than typed by the administrator
/// (rules/architecture.md "The backend owns the data, the SPA renders it"). The list is the owner's:
/// a reseller is offered the reseller-grade scopes and the administrator the administrator-grade ones.
/// </summary>
/// <param name="OwnerResellerId">
/// The reseller asking, established by the server from its own access token and stamped by the
/// reseller's action; <c>null</c> on the administrator's. Never bound from the request.
/// </param>
public sealed record ListApiKeyScopesQuery(
    [property: JsonIgnore][property: BindNever][BindNever] Guid? OwnerResellerId = null);
