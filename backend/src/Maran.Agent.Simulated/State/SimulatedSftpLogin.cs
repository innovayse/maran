namespace Maran.Agent.Simulated.State;

/// <summary>One SFTP login as the simulated server holds it.</summary>
/// <param name="Account">System username of the owning account.</param>
/// <param name="Suffix">The login name suffix the customer chose.</param>
/// <param name="PublicKeys">The complete set of public-key lines the login may authenticate with.</param>
/// <param name="OpenSessions">How many transfer sessions the login has open right now.</param>
public sealed record SimulatedSftpLogin(
    string Account,
    string Suffix,
    IReadOnlyList<string> PublicKeys,
    int OpenSessions)
{
    /// <summary>The login name as the server holds it: the account prefix, then the suffix.</summary>
    public string FullName
    {
        get { return $"{Account}_{Suffix}"; }
    }
}
