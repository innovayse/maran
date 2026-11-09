using Maran.Agent.Client.Services.AccountsService;

namespace Maran.Modules.Accounts.Mappers;

/// <summary>Restates the account's stored shell choice in the agent's vocabulary.</summary>
public static class AccountLoginShellMapper
{
    /// <summary>States the shell the agent is to give an account's system user.</summary>
    /// <param name="shellAccess">Whether the account was created with a login shell (<c>Account.ShellAccess</c>).</param>
    /// <returns>The agent's shell: a real one for an account with shell access, none otherwise.</returns>
    public static AccountLoginShell ToAgent(bool shellAccess)
    {
        return shellAccess ? AccountLoginShell.Bash : AccountLoginShell.Nologin;
    }
}
