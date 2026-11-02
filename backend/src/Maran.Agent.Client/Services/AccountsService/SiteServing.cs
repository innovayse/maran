namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>
/// What one of an account's vhosts can be observed to be serving: three outcomes, because "serves
/// the suspension page", "serves the customer's own content" and "could not be compared" are three
/// different facts and a suspension attestation must treat the third as a refusal, not as the
/// second.
/// </summary>
public enum SiteServing
{
    /// <summary>The vhost on disk is byte for byte what the suspended template renders for the site.</summary>
    Stub,

    /// <summary>
    /// The agent compared the vhost with the suspended rendering and it differs: the site serves its
    /// own content.
    /// </summary>
    NotStub,

    /// <summary>
    /// The agent could not produce the comparison text (an unusable file name, an unresolved
    /// document root, no or duplicate <c>server_name</c> line, a template that refused), so nothing
    /// is known about what the vhost serves.
    /// </summary>
    Unobservable,
}
