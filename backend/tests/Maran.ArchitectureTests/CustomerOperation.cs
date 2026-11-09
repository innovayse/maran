namespace Maran.ArchitectureTests;

/// <summary>
/// One operation a customer's token can reach: an HTTP method on a route, the module that serves it,
/// and the controller action it is.
/// </summary>
/// <param name="Method">The upper-case HTTP method, such as <c>GET</c>.</param>
/// <param name="Route">The full route template, such as <c>api/v1/sites/{id:guid}</c>, lower case as declared.</param>
/// <param name="Module">The module that serves it: the part of the assembly name after <c>Maran.Modules.</c>.</param>
/// <param name="Action">The action as <see cref="ControllerAuthorizationCensus.NameOf"/> spells it.</param>
public sealed record CustomerOperation(string Method, string Route, string Module, string Action)
{
    /// <summary>The operation's identity in the matrix: <c>METHOD route</c>.</summary>
    public string Key
    {
        get { return $"{Method} {Route}"; }
    }
}
