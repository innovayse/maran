namespace Maran.Modules.Ssl.Queries.GetIssuanceOptions;

/// <summary>Reads which kinds of certificate can be issued for one of the caller's domains (issue #161).</summary>
/// <param name="Domain">
/// The domain to ask about. It must be a site the caller owns. Optional on the wire, and the default is what
/// makes it optional: <c>[ApiController]</c> infers <c>required</c> for a non-nullable reference-type
/// constructor parameter that has none, which would answer a missing field with a model-state error instead of
/// this module's own validation code.
/// </param>
public sealed record GetIssuanceOptionsQuery(string Domain = "");
