namespace Maran.Sdk.Contracts;

/// <summary>
/// One file of a packaged module's screen bundle, opened for reading. The caller owns the stream
/// from the moment it receives the asset and disposes it after the response is written.
/// </summary>
/// <param name="Content">The file's bytes, positioned at the start.</param>
/// <param name="Length">The file's length in bytes.</param>
public sealed record ModuleSpaAsset(Stream Content, long Length);
