using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// A package source whose bytes were downloaded against a signed catalogue entry. It carries what the
/// catalogue promised, so the loader can check the package's own manifest against it before it keeps
/// anything. A source that is not downloaded (a file the operator supplied) is a plain
/// <see cref="IModulePackageSource"/> and has nothing to compare with.
/// </summary>
public interface IVerifiedModulePackageSource : IModulePackageSource
{
    /// <summary>The identity, version, digest and size the catalogue declared for this package.</summary>
    ExpectedModulePackage Expected { get; }
}
