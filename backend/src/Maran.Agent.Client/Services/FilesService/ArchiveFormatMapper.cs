using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.FilesService;

/// <summary>Writes the panel's archive format as the wire enum, beside the client that sends it.</summary>
internal static class ArchiveFormatMapper
{
    /// <summary>Writes the format.</summary>
    /// <param name="format">The format asked for.</param>
    /// <returns>The wire value.</returns>
    public static ArchiveFormat ToWire(AgentArchiveFormat format)
    {
        return format switch
        {
            AgentArchiveFormat.Zip => ArchiveFormat.Zip,
            AgentArchiveFormat.TarGz => ArchiveFormat.TarGz,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown archive format."),
        };
    }
}
