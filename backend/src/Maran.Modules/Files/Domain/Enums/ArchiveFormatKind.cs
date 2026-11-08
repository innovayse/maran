using System.Text.Json.Serialization;

namespace Maran.Modules.Files.Domain.Enums;

/// <summary>The archive formats the manager writes: a closed set.</summary>
public enum ArchiveFormatKind
{
    /// <summary>A zip archive.</summary>
    [JsonStringEnumMemberName("zip")]
    Zip,

    /// <summary>A gzip-compressed tar archive.</summary>
    [JsonStringEnumMemberName("tar.gz")]
    TarGz,
}
