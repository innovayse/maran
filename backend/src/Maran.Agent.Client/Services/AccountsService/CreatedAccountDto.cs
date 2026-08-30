namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>What creating an account produced on the server.</summary>
/// <param name="HomeDirectory">The absolute home directory the agent created.</param>
/// <param name="Uid">The numeric uid the system assigned.</param>
public sealed record CreatedAccountDto(string HomeDirectory, uint Uid);
