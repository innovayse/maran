using Maran.SharedKernel.Security;

namespace Maran.Modules.Databases.Common;

/// <summary>What a password reset produced: the login it applies to, and the new value, shown once.</summary>
/// <remarks>
/// The other of the module's two password-carrying responses (see <see cref="CreatedDatabaseDto"/>
/// for why they are named types rather than optional fields), and the ONLY recovery path there is:
/// nobody keeps a copy of a database password, and creating the same database again is refused as
/// already existing without touching the credential. A customer who lost theirs gets a new one here
/// or not at all.
///
/// The value is a <see cref="SensitiveString"/> for the reason <see cref="CreatedDatabaseDto"/>
/// gives: this is a <c>record</c>, and a plain string property would be printed by its generated
/// <c>ToString()</c>.
/// </remarks>
/// <param name="Id">The database whose user was re-credentialled.</param>
/// <param name="DbUserName">The fully-qualified MySQL user the new password belongs to.</param>
/// <param name="Password">The new password, shown once and stored nowhere.</param>
public sealed record DatabasePasswordDto(Guid Id, string DbUserName, SensitiveString Password);
