namespace Maran.Sdk.Contracts;

/// <summary>
/// What an operator viewing the panel as a customer login may do at one endpoint, declared on the
/// endpoint with <see cref="ViewAsAccessAttribute"/> (issue #334).
/// </summary>
/// <remarks>
/// A closed set of three, and an endpoint that declares none of them is <see cref="Refused"/>: viewing
/// is an allow-list, so the failure mode of a forgotten declaration is a refusal and not a door.
/// </remarks>
public enum ViewAsAccess
{
    /// <summary>
    /// The endpoint only reads. Honoured for a safe HTTP method (GET, HEAD) only: the same attribute
    /// on a POST, PUT or DELETE is refused, so a mutation cannot be waved through by calling it a read.
    /// </summary>
    Read,

    /// <summary>
    /// The endpoint changes something, and what it changes is safe to change from a view-as token:
    /// it creates no credential, no access path, no running work and nothing that outlives the
    /// look. Each use is a reviewed decision, named in the threat note.
    /// </summary>
    SafeMutation,

    /// <summary>The endpoint is not reachable from a view-as token. The reviewed form of the default.</summary>
    Refused,
}
