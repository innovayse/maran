namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>One hosting account's home whose group was — or would be — narrowed to the web server's.</summary>
/// <param name="AccountUsername">The account's system user name.</param>
/// <param name="Home">The home directory that was, or would be, re-grouped.</param>
public sealed record RepairedHomeDto(string AccountUsername, string Home);
