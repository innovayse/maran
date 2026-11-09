using Maran.Agent.Client.Services.FilesService;
using Maran.Modules.Files.Domain.Enums;

namespace Maran.Modules.Files.Mappers;

/// <summary>Restates the module's archive format in the agent client's vocabulary.</summary>
public static class ArchiveFormatMapper
{
    /// <summary>The agent client's name for a container format.</summary>
    /// <param name="format">The module's format.</param>
    /// <returns>The same format in the agent client's vocabulary.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A format this module has no agent name for.</exception>
    public static AgentArchiveFormat ToAgent(ArchiveFormatKind format)
    {
        return format switch
        {
            ArchiveFormatKind.Zip => AgentArchiveFormat.Zip,
            ArchiveFormatKind.TarGz => AgentArchiveFormat.TarGz,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown archive format."),
        };
    }
}
