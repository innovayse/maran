namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>The host's answer carries no count of the logins sharing the account's uid.</summary>
/// <remarks>
/// Neither "none exist" nor a number: the answer came from an agent that may predate the field, so
/// the caller words it as unknown and never as zero.
/// </remarks>
public sealed record UnmeasuredUnmanagedLogins : UnmanagedLoginCount;
