using Maran.SharedKernel.Security;

namespace Maran.Modules.ServerMigration.Common;

/// <summary>A credential an import generated, returned once.</summary>
/// <param name="Subject">What it opens.</param>
/// <param name="Secret">The generated credential.</param>
public sealed record IssuedSecretDto(string Subject, SensitiveString Secret);
