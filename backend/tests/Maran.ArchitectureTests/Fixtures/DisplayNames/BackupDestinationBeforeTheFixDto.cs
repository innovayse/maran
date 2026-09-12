using Maran.Modules.Backups.Domain.Enums;

namespace Maran.ArchitectureTests.Fixtures.DisplayNames;

/// <summary>
/// The destinations screen as it shipped: the row the panel seeded and named itself carried only
/// its stored <c>Name</c>, so a Russian panel showed the heading <c>Local storage</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the defect that decides the shape of the law. The member an operator saw was
/// <c>Name</c> — and a law that demanded a display name for every <c>Name</c> would demand a
/// translation of the words a customer typed into a form, which is the opposite of what the panel
/// owns. So the law does not look at <c>Name</c>. It refuses this type through the <c>Kind</c>
/// beside it, which is the member that says the row is drawn from a vocabulary the panel authored,
/// and the shortest change that satisfies the refusal — a <c>DisplayName</c> on the DTO — is
/// exactly the fix that was made.
/// </para>
/// <para>
/// It also decides one detail of the sibling rule: a bare <c>Name</c> counts as a display name only
/// for the member that IS the thing the DTO describes. Accepting it everywhere would have let this
/// very type pass, because it had a <c>Name</c> all along and that <c>Name</c> was the defect.
/// </para>
/// </remarks>
/// <param name="Id">The destination's identity.</param>
/// <param name="Name">The label as the row stores it.</param>
/// <param name="Kind">Which kind of storage it names.</param>
/// <param name="IsDefault">Whether backups naming no destination are written here.</param>
public sealed record BackupDestinationBeforeTheFixDto(Guid Id, string Name, BackupDestinationKind Kind, bool IsDefault);
