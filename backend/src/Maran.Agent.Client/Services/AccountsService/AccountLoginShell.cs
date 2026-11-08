namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>
/// The login shell an account's system user is given. Mirrors the two real cases of the wire's
/// <c>LoginShell</c>; its zero value, which the wire reads as "an old caller", has no member here
/// because this panel always states which shell it wants.
/// </summary>
public enum AccountLoginShell
{
    /// <summary>The account cannot start an interactive shell session.</summary>
    Nologin,

    /// <summary>The account may start a bash session (shell access was granted to it).</summary>
    Bash,
}
