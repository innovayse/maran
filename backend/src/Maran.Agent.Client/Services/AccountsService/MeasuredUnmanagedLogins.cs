namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>The agent counted the logins sharing the account's uid that the panel does not own.</summary>
/// <remarks>
/// Zero is a completeness claim here ("the host looked and found none"), which is why it may only be
/// built when the answer proves the agent counted.
/// </remarks>
/// <param name="Count">How many such passwd entries the host holds; zero means none were found.</param>
public sealed record MeasuredUnmanagedLogins(uint Count) : UnmanagedLoginCount;
