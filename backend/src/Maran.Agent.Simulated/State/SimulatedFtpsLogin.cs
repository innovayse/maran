namespace Maran.Agent.Simulated.State;

/// <summary>One FTPS login as the simulated server holds it.</summary>
/// <param name="Account">System username of the owning account.</param>
/// <param name="Suffix">The login name suffix the customer chose.</param>
/// <param name="OpenSessions">How many transfer sessions the login has open right now.</param>
public sealed record SimulatedFtpsLogin(string Account, string Suffix, int OpenSessions)
{
    /// <summary>The login name as the server holds it: the account prefix, then the suffix.</summary>
    public string FullName
    {
        get { return $"{Account}_{Suffix}"; }
    }
}
