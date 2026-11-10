using Maran.SharedKernel.Security;

namespace Maran.Sdk.Contracts;

/// <summary>A credential an import generated, shown to the administrator once and kept by nobody (decision D3).</summary>
/// <remarks>
/// A package carries no password, so an imported database user or login gets a new one. It is a
/// <see cref="SensitiveString"/> because this record is one compiler-generated <c>ToString()</c> from printing
/// every member.
/// </remarks>
/// <param name="Subject">What the credential opens, as the administrator knows it (a database user, a login).</param>
/// <param name="Secret">The generated credential.</param>
public sealed record MigrationIssuedSecret(string Subject, SensitiveString Secret);
