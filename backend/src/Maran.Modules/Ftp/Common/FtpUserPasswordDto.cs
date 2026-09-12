using Maran.SharedKernel.Security;

namespace Maran.Modules.Ftp.Common;

/// <summary>What a password reset produced: the login it applies to, and the new value, shown once.</summary>
/// <remarks>
/// The other of the module's two password-carrying responses (see <see cref="CreatedFtpUserDto"/>
/// for why they are named types rather than optional fields), and the ONLY recovery path there is:
/// nobody keeps a copy of an FTPS password, and creating the same login again is reported as already
/// existing without touching the credential. A customer who lost theirs gets a new one here or not
/// at all.
///
/// It carries no protocol label, unlike the module's two read shapes, and the absence is deliberate:
/// this answer is consumed by the dialog that reveals a credential for one login the caller has just
/// acted on, which already knows which list it came from. Adding the field would put a fact on the
/// wire that nothing reads.
///
/// The value is a <see cref="SensitiveString"/> for the reason <see cref="CreatedFtpUserDto"/>
/// gives: this is a <c>record</c>, and a plain string property would be printed by its generated
/// <c>ToString()</c>.
/// </remarks>
/// <param name="Id">The login that was re-credentialled.</param>
/// <param name="FullName">The system login the new password belongs to.</param>
/// <param name="Password">The new password, shown once and stored nowhere.</param>
public sealed record FtpUserPasswordDto(Guid Id, string FullName, SensitiveString Password);
