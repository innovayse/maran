namespace Maran.Sdk.Contracts;

/// <summary>
/// Names of the authorization policies that admit a single-use export grant, so a module can declare on an
/// endpoint that a grant may reach it (issue #127).
/// </summary>
/// <remarks>
/// The same arrangement as <see cref="ApiKeyPolicyNames"/>: the Host defines what each policy requires, a module
/// only says which one applies, and a grant reaches an endpoint ONLY when the endpoint names one of these policies
/// together with the <see cref="MigrationGrantDefaults.Scheme"/> scheme. Every other policy authenticates with
/// the default scheme and a grant never satisfies it.
/// </remarks>
public static class MigrationGrantPolicyNames
{
    /// <summary>Requires a live grant for one export's package, and admits nothing else.</summary>
    public const string Pull = "MigrationGrantPull";
}
