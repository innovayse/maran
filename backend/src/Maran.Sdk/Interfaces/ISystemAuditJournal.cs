namespace Maran.Sdk.Interfaces;

/// <summary>
/// The one thing a module's audit journal is asked by code outside that module: record something
/// the panel did on its own initiative, with nobody signed in. Implemented by each module's own
/// <c>*AuditJournal</c>, so the entry is still built where that module decides what its entries
/// carry (rules/csharp.md "A module writes its audit entries through its own journal").
/// </summary>
/// <remarks>
/// It exists so that a shared Sdk facility — <see cref="Alerts.OperatorAlertMailer"/> — can write
/// the entries an operator alert owes INTO THE CALLING MODULE'S journal, under that module's actor,
/// instead of building entries of its own. A facility that called <see cref="IAuditWriter"/> itself
/// would be a second place deciding what a module's entry is made of.
/// </remarks>
public interface ISystemAuditJournal
{
    /// <summary>Records something the panel did on its own initiative, with no signed-in caller.</summary>
    /// <param name="action">The action name, one of <see cref="Contracts.AuditActions"/>.</param>
    /// <param name="subject">What it was done to, as the module's journal records subjects.</param>
    /// <param name="succeeded">Whether it took effect.</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    Task RecordSystemAsync(string action, string subject, bool succeeded, CancellationToken cancellationToken);
}
