namespace Maran.Sdk.Interfaces;

/// <summary>
/// Answers one question and nothing else: which address the panel's own operator alerts go to. The
/// contract lives in the Sdk and its implementation in the module that owns the mail settings, the
/// same shape <see cref="IAuditWriter"/> and <see cref="IAccountDirectory"/> established — because a
/// module may never reference another module (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all, when <c>SendMailRequested</c> already carries a recipient.</b> Every
/// module that sends mail knows who to send it to: Identity has the user's own row. A machine alert
/// has no such person — it is raised by a sampler on a timer, with nobody signed in — so the address
/// is a stored panel setting, and it is stored beside the mail server because that is the one form
/// an administrator configures. This is the window onto that single field.
/// </para>
/// <para>
/// <b>It exposes the ADDRESS and never the settings.</b> Not the host, not the user name, and above
/// all not the submission password: a caller that only needs to know where to send cannot be handed
/// a credential for the operator's mail provider. Widening this return type to the settings record
/// would be the regression to watch for (rules/security.md item 8).
/// </para>
/// <para>
/// <b>Read-only, like every cross-module window.</b> It is not a second way to change where alerts
/// go; that is the settings command's job, inside the owning module.
/// </para>
/// </remarks>
public interface IAlertRecipientDirectory
{
    /// <summary>Reads the address the panel's operator alerts are addressed to.</summary>
    /// <param name="cancellationToken">Cancellation token for the read.</param>
    /// <returns>
    /// The address, or <c>null</c> when the panel has no mail settings at all or has settings with
    /// no alert address in them. A null is the ordinary state of a fresh installation, so a caller
    /// records that it did not send and carries on — it is not an error.
    /// </returns>
    Task<string?> GetAlertRecipientAsync(CancellationToken cancellationToken);
}
