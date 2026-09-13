namespace Maran.SharedKernel.Security;

/// <summary>
/// The one rule shared between the code that MINTS a secret and the code that REDACTS it out of the
/// agent's error text.
/// </summary>
/// <remarks>
/// The agent-client error boundary strips the exact value a call carried before logging what the
/// agent said, because a password has no shape a pattern could find — it is a short run of mixed
/// characters, indistinguishable from a table name — and the one thing the panel does know is the
/// value it minted seconds ago. That literal search is only safe above a length floor: stripping
/// every occurrence of a four-character value would mangle unrelated diagnostics and, worse, would
/// advertise where the value had been.
///
/// The floor therefore constrains the generators too, and this type is public so that they can be
/// held to it by a test instead of by a paragraph. The failure it prevents is silent: a generator
/// that dropped below the floor would still succeed, the log line would still be written, and the
/// only difference would be the customer's password sitting in it.
///
/// It lives in the SharedKernel rather than beside the boundary that redacts, because it is a rule
/// with two ends and the minting end is here: <see cref="ProvisionedPasswordGenerator"/> may not
/// reach up into the agent client to read it, and a floor only one end can see is a floor the other
/// end can fall through without a build error.
/// </remarks>
public static class SecretRedactionPolicy
{
    /// <summary>
    /// The shortest value that may be searched for literally, and so the shortest secret this
    /// product may mint.
    /// </summary>
    public const int ShortestRecognisableSecret = 8;
}
