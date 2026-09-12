namespace Maran.ArchitectureTests.Fixtures.DisplayNames;

/// <summary>
/// The audit journal as it shipped: the recorded action reached the table as the constant the row
/// stores, so an administrator read <c>BackupRestored</c> and <c>AccountSuspended</c> in a panel
/// that was otherwise entirely in their own language.
/// </summary>
/// <remarks>
/// The action is a <see cref="string"/> and not an enum for a reason that matters to the law: the
/// set grows with every module, a marketplace one included, so no bundle can be shipped holding a
/// word for every value it will receive. That is why an SPA-owned key set is not an available
/// answer here and the backend has to name it — which the fix did, with <c>ActionName</c> from
/// <c>AuditActionDisplayNames</c>.
/// </remarks>
/// <param name="Id">The event's identity.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="Action">What was attempted, as the machine-stable name the row records.</param>
/// <param name="Subject">What it was attempted on.</param>
public sealed record AuditEventBeforeTheFixDto(Guid Id, DateTimeOffset OccurredAt, string Action, string Subject);
