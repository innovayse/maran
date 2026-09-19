namespace Maran.Modules.Databases.Common;

/// <summary>One grant-table row the repair left exactly as it found it, and what to do about it.</summary>
/// <param name="GrantHost">The database server's <c>Host</c> column, verbatim.</param>
/// <param name="DatabaseName">
/// The server's <c>Db</c> column, verbatim, escapes included. Deliberately not tidied: a row is
/// refused because it is not a value this panel could have written, so the bytes the server holds are
/// the thing an operator has to look at.
/// </param>
/// <param name="DbUsername">The server's <c>User</c> column, verbatim.</param>
/// <param name="Reason">
/// The machine-stable refusal name the agent reported. Kept beside the localized text because a
/// support conversation and a log line need one spelling that does not change with the reader's
/// language.
/// </param>
/// <param name="ReasonDisplayName">
/// What the agent decided, in the caller's language. Produced here and never in the SPA: the
/// vocabulary is the backend's (rules/architecture.md "The backend owns the data, the SPA renders
/// it"), and a bundle shipped before an agent that adds a reason could not hold its words.
/// </param>
/// <param name="ReasonAdvice">
/// What the operator can do about this row, in the caller's language. A refused row is one the panel
/// will never touch again, so a verdict with no next step leaves the reader with a wide grant and
/// nothing to act on.
/// </param>
/// <remarks>
/// <b>The three raw names can belong to another tenant, and that is why this DTO is administrator-only.</b>
/// The agent reads the whole of the server's database-level grant table, and a row is refused exactly
/// when this panel did not write it — so it can be another customer's database and user, or an
/// operator's own reporting credential. <c>DatabaseGrantsController</c> carries
/// <c>AuthorizationPolicies.AdminOnly</c> for this reason, and no customer-facing screen may reuse
/// this record.
/// </remarks>
public sealed record RefusedGrantDto(
    string GrantHost,
    string DatabaseName,
    string DbUsername,
    string Reason,
    string ReasonDisplayName,
    string ReasonAdvice);
