namespace Maran.Modules.Accounts.Errors;

/// <summary>
/// Machine-stable error codes raised by the Accounts module. Codes stay untranslated and drive
/// HTTP status mapping (<c>ApiResultExtensions.MapStatusCode</c>) and resource lookup
/// (<c>Resources/Messages*.resx</c>) — never shown raw to a customer (rules/csharp.md "The
/// backend owns all user-facing message text").
/// </summary>
public static class AccountsErrors
{
    /// <summary>The requested account does not exist.</summary>
    /// <param name="accountId">The id that was looked up.</param>
    public static Error NotFound(Guid accountId) =>
        Error.Of("AccountNotFound", $"Account {accountId} was not found.");

    /// <summary>An account with the requested name already exists.</summary>
    /// <param name="name">The name that collided.</param>
    public static Error NameTaken(string name) =>
        Error.Of("AccountNameTaken", $"Account name '{name}' is already taken.");

    /// <summary>An account with the requested primary domain already exists.</summary>
    /// <param name="domain">The domain that collided.</param>
    public static Error DomainTaken(string domain) =>
        Error.Of("AccountDomainTaken", $"Domain '{domain}' is already taken by another account.");

    /// <summary>The requested plan does not exist.</summary>
    /// <param name="planId">The id that was looked up.</param>
    public static Error PlanNotFound(Guid planId) =>
        Error.Of("PlanNotFound", $"Plan {planId} was not found.");
}
