namespace Maran.Modules.Accounts.Common;

/// <summary>One hosting account's home the repair deliberately left exactly as it found it.</summary>
/// <param name="AccountUsername">
/// The account's system user name, as the password database spells it — not necessarily an account
/// the panel currently recognises as its own naming.
/// </param>
/// <param name="Home">
/// The path examined, carried verbatim. Not tidied: a refused row is refused because it is not a
/// path this panel treats as safe to act on, and tidying it here would hide the thing an operator
/// has to look at.
/// </param>
/// <param name="Reason">The agent's machine-stable refusal name.</param>
/// <param name="ReasonDisplayName">What the agent decided, localized for the current request's culture.</param>
/// <param name="ReasonAdvice">What the operator can do about this row, localized for the current culture.</param>
public sealed record RefusedHomeDto(
    string AccountUsername,
    string Home,
    string Reason,
    string ReasonDisplayName,
    string ReasonAdvice);
