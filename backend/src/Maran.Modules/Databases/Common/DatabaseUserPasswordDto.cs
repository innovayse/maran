using Maran.SharedKernel.Security;

namespace Maran.Modules.Databases.Common;

/// <summary>A database user's new password, shown once: the only recovery a lost one has.</summary>
/// <param name="Id">The user whose password was replaced.</param>
/// <param name="FullName">The fully-qualified user name the password belongs to.</param>
/// <param name="Password">The new password, shown once and stored nowhere.</param>
public sealed record DatabaseUserPasswordDto(Guid Id, string FullName, SensitiveString Password);
