namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>
/// How many passwd entries share an account's uid without being one of its jailed logins, as far as
/// the host's answer can say: either a count the agent measured, or the fact that this answer
/// carries none.
/// </summary>
/// <remarks>
/// <para>
/// The wire carries a <c>uint32</c>, so an agent that predates the field is indistinguishable from
/// one reporting zero by looking at the number alone. That ambiguity is decided ONCE, in
/// <see cref="AgentAccountsClient.GetSuspensionStateAsync"/>, where the raw answer is still in hand,
/// and it is carried from there as a type: <see cref="MeasuredUnmanagedLogins"/> holds a number that
/// may be stated as a fact, <see cref="UnmeasuredUnmanagedLogins"/> holds none, so no caller can
/// print an absent count as zero. Only this assembly can derive from it, so the two are the whole
/// set.
/// </para>
/// </remarks>
public abstract record UnmanagedLoginCount
{
    /// <summary>Closes the hierarchy to the two cases this assembly declares.</summary>
    private protected UnmanagedLoginCount()
    {
    }
}
