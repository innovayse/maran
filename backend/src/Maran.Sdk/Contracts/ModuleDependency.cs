namespace Maran.Sdk.Contracts;

/// <summary>
/// One module another module requires, and the versions of it that it works with.
/// </summary>
/// <param name="Id">The <see cref="Manifest.Id"/> of the module required.</param>
/// <param name="Range">
/// The accepted versions of that module, in the grammar of
/// <c>Maran.SharedKernel.Utilities.Versions.VersionRange</c> (for example <c>&gt;=1.2.0 &lt;2.0.0</c>).
/// A dependency that accepts any release is written <c>&gt;=0.0.0</c>; there is no empty range.
/// </param>
public sealed record ModuleDependency(string Id, string Range);
