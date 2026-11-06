using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Maran.Host.OpenApi;

/// <summary>
/// States, per operation, which of the panel's credentials the operation accepts, by asking the
/// authorization system the question a request would ask it.
/// </summary>
/// <remarks>
/// Not a blanket "everything needs a bearer token": an anonymous endpoint (sign-in, password reset) is
/// documented as needing nothing, and an endpoint that opts into the API-key scheme lists it as a second
/// way in. The answer is read from the policy the endpoint resolves to — its own, or the fallback an
/// endpoint with no metadata gets — so it moves when the policy does and cannot be a hand-kept list that
/// drifts. Two separate security requirements are alternatives (either credential admits the caller),
/// which is how a policy naming both schemes behaves.
/// </remarks>
public sealed class PanelSecurityOperationFilter : IOperationFilter
{
    /// <summary>Resolves a named policy, and the fallback, exactly as the authorization middleware does.</summary>
    private readonly IAuthorizationPolicyProvider _policies;

    /// <summary>Creates the filter.</summary>
    /// <param name="policies">The provider the authorization middleware itself asks.</param>
    public PanelSecurityOperationFilter(IAuthorizationPolicyProvider policies)
    {
        _policies = policies;
    }

    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            return;
        }

        var policy = ResolvePolicy(metadata.OfType<IAuthorizeData>().ToList());
        if (policy is null)
        {
            return;
        }

        var schemes = policy.AuthenticationSchemes;
        if (schemes.Count == 0 || schemes.Contains(JwtBearerDefaults.AuthenticationScheme))
        {
            operation.Security.Add(Requirement(OpenApiDocumentNames.BearerScheme));
        }

        if (schemes.Contains(ApiKeyDefaults.Scheme))
        {
            operation.Security.Add(Requirement(ApiKeyDefaults.Scheme));
        }
    }

    /// <summary>The policy a request to the endpoint is judged by.</summary>
    /// <param name="authorizeData">The endpoint's own authorization attributes.</param>
    /// <returns>
    /// The combined policy, the fallback when the endpoint declares none, or null when nothing applies.
    /// </returns>
    /// <remarks>
    /// The two asynchronous provider calls are waited on because Swashbuckle's <c>IOperationFilter.Apply</c> is
    /// synchronous and has no asynchronous overload; the provider answers from in-memory options, so the wait
    /// never blocks on I/O, and the filter runs only when the document is generated, never per request.
    /// </remarks>
    private AuthorizationPolicy? ResolvePolicy(List<IAuthorizeData> authorizeData)
    {
        if (authorizeData.Count > 0)
        {
            return AuthorizationPolicy.CombineAsync(_policies, authorizeData).GetAwaiter().GetResult();
        }

        return _policies.GetFallbackPolicyAsync().GetAwaiter().GetResult();
    }

    /// <summary>Builds the requirement naming one security scheme, with no OAuth scopes.</summary>
    /// <param name="scheme">The scheme's name in the document's components.</param>
    /// <returns>The requirement.</returns>
    private static OpenApiSecurityRequirement Requirement(string scheme)
    {
        var reference = new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = scheme },
        };
        return new OpenApiSecurityRequirement { [reference] = new List<string>() };
    }
}
