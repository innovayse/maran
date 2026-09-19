namespace Maran.Agent.Client.Services.AccountsService;

/// <summary>One hosting account's home the repair deliberately left exactly as it found it.</summary>
/// <param name="AccountUsername">
/// The account's system user name, as the password database spells it. Not necessarily an account
/// the panel currently recognises: several refusal reasons exist precisely because the row at this
/// path is not what this agent's own account creation produced.
/// </param>
/// <param name="Home">
/// The path examined — the recorded home when it did not match the expected one, or the expected
/// home otherwise. Carried verbatim: a refused row is refused because it is not a path this panel
/// treats as safe to act on, and tidying it here would hide the thing an operator has to look at.
/// </param>
/// <param name="Reason">
/// Why the account was refused, as the machine-stable name of the agent's
/// <c>HomeGroupRepairRefusal</c> value — <c>HomeNotAtExpectedPath</c>, <c>HomeMissing</c>,
/// <c>Symlink</c>, <c>NotADirectory</c>, <c>DifferentMount</c>, <c>OwnerMismatch</c>, or
/// <c>Unspecified</c> for a value this build was not compiled knowing about.
/// <para>
/// A string and not the wire enum, so that an agent newer than this panel widens the set without the
/// panel mistaking an unknown value for the zero one. The panel names it for an operator by resource
/// key and falls back to this identifier, which is loud rather than wrong.
/// </para>
/// </param>
/// <remarks>
/// <b>This account may not be the caller's own.</b> The agent reads every hosting account on the
/// host; a refused row can name any of them. That is why the panel exposes this only to an
/// administrator and never to a customer.
/// </remarks>
public sealed record RefusedHomeDto(string AccountUsername, string Home, string Reason);
