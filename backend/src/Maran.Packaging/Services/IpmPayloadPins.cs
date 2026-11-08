using System.Security.Cryptography;
using Maran.Packaging.Models;
using Maran.Packaging.Resources;

namespace Maran.Packaging.Services;

/// <summary>
/// Holds an archive's payload to the manifest's pins: every listed file present with the listed size
/// and SHA-384, and nothing present that is not listed. A byte changed, a file added or a file removed
/// is refused, which is what ties the payload to the signature that covers the manifest.
/// </summary>
public static class IpmPayloadPins
{
    /// <summary>Compares the payload with the listing.</summary>
    /// <param name="manifest">The authenticated manifest.</param>
    /// <param name="archive">The archive it came with.</param>
    /// <returns>Success, or the first refusal: a missing file, a mismatching file, or an unlisted entry.</returns>
    public static Result Check(IpmManifest manifest, IpmArchive archive)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(archive);

        foreach (var file in manifest.Files)
        {
            if (!archive.Payload.TryGetValue(file.Path, out var bytes))
            {
                return Refuse(nameof(ErrorMessages.IpmMissingEntry));
            }

            var digest = Convert.ToHexStringLower(SHA384.HashData(bytes));

            if (bytes.LongLength != file.Size || !string.Equals(digest, file.Sha384, StringComparison.Ordinal))
            {
                return Refuse(nameof(ErrorMessages.IpmHashMismatch));
            }
        }

        var listed = manifest.Files
            .Select(file =>
            {
                return file.Path;
            })
            .ToHashSet(StringComparer.Ordinal);

        return archive.Payload.Keys.Any(name =>
        {
            return !listed.Contains(name);
        })
            ? Refuse(nameof(ErrorMessages.IpmUnlistedEntry))
            : Result.Ok();
    }

    /// <summary>A validation failure from a code.</summary>
    /// <param name="code">A key of the resx.</param>
    /// <returns>The failed result.</returns>
    private static Result Refuse(string code)
    {
        return Result.Fail(Error.Of(code, ErrorType.Validation));
    }
}
